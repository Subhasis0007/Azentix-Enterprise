"""
ServiceNow Incident Triage Flow
==================================
Triggered by: CloudAMQP RabbitMQ servicenow-incidents queue (consumed by n8n)
Flow: validate -> fetch_incident -> search_kb -> classify -> auto_resolve OR escalate -> notify
"""
import os, json, logging, operator
from typing import TypedDict, Annotated, Optional, List
from datetime import datetime
from langgraph.graph import StateGraph, END
from langgraph.checkpoint.memory import MemorySaver
from langchain_openai import AzureChatOpenAI
from langchain.schema import HumanMessage, SystemMessage
import requests, pika
from dotenv import load_dotenv

logger = logging.getLogger(__name__)


class IncidentTriageState(TypedDict):
    incident_number: str
    incident_sys_id: Optional[str]
    short_description: Optional[str]
    description: Optional[str]
    current_state: Optional[str]
    current_priority: Optional[str]
    category: Optional[str]
    assignment_group: Optional[str]
    kb_articles: Optional[str]
    ai_classification: Optional[dict]
    action_taken: Optional[str]
    audit_trail: Annotated[List[dict], operator.add]
    final_status: Optional[str]
    error: Optional[str]


def _audit(node, action, result):
    return {"audit_trail": [{"ts": datetime.utcnow().isoformat(),
                              "node": node, "action": action, "result": result}]}


def validate_node(state):
    if not state.get("incident_number"):
        return {"final_status": "validation_failed", "error": "Missing incident_number",
                **_audit("validate", "failed", "Missing incident_number")}
    return _audit("validate", "passed", f"Incident {state['incident_number']}")


def fetch_incident_node(state, config):
    try:
        url = (config["snow_url"] + "/api/now/table/incident" +
               f"?sysparm_query=number={state['incident_number']}" +
               "&sysparm_fields=sys_id,number,short_description,description,state,priority,category,assignment_group" +
               "&sysparm_limit=1")
        resp = requests.get(url, auth=(config["snow_user"], config["snow_pass"]),
                            headers={"Accept": "application/json"}, timeout=10)
        if resp.ok:
            records = resp.json().get("result", [])
            if records:
                rec = records[0]
                return {
                    "incident_sys_id": rec.get("sys_id"),
                    "short_description": rec.get("short_description"),
                    "description": rec.get("description", ""),
                    "current_state": rec.get("state"),
                    "current_priority": rec.get("priority"),
                    "category": rec.get("category"),
                    "assignment_group": rec.get("assignment_group", {}).get("display_value", ""),
                    **_audit("fetch_incident", "fetched", f"P{rec.get('priority')} - {rec.get('short_description','')[:60]}")
                }
    except Exception as e:
        logger.warning(f"ServiceNow fetch failed: {e}")
    return {
        "incident_sys_id": "mock_sys_id",
        "short_description": "Mock incident for testing",
        "current_priority": "3", "current_state": "1", "category": "software",
        **_audit("fetch_incident", "mock", "ServiceNow unavailable, using mock")
    }


def search_kb_node(state, config):
    try:
        query = (state.get("short_description", "") + " " + state.get("description", ""))[:100]
        url = (config["snow_url"] + "/api/now/table/kb_knowledge" +
               f"?sysparm_query=textLIKE{requests.utils.quote(query[:50])}^workflow_state=published" +
               "&sysparm_fields=short_description,text,kb_category&sysparm_limit=3")
        resp = requests.get(url, auth=(config["snow_user"], config["snow_pass"]),
                            headers={"Accept": "application/json"}, timeout=10)
        if resp.ok:
            articles = resp.json().get("result", [])
            if articles:
                kb_text = "\n\n".join([f"[KB] {a['short_description']}: {a['text'][:200]}" for a in articles])
                return {"kb_articles": kb_text,
                        **_audit("search_kb", "found", f"{len(articles)} KB articles")}
    except Exception as e:
        logger.warning(f"KB search failed: {e}")
    return {"kb_articles": "No KB articles found.",
            **_audit("search_kb", "none", "No KB articles found")}


def classify_node(state, llm):
    resp = llm.invoke([
        SystemMessage(content=(
            'Classify ServiceNow incident and determine action. '
            'JSON only: {"priority":"1|2|3|4","category":"software|hardware|network|sap|salesforce|stripe",'
            '"assignment_group":"string","auto_resolvable":bool,'
            '"resolution_note":"string if auto_resolvable","escalate":bool,"confidence":float}'
        )),
        HumanMessage(content=(
            f"Incident: {state.get('short_description')}\n"
            f"Description: {state.get('description','')[:300]}\n"
            f"Current priority: {state.get('current_priority')}\n"
            f"KB Context:\n{state.get('kb_articles','')[:500]}"
        ))
    ])
    try:
        classification = json.loads(resp.content)
    except Exception:
        classification = {
            "priority": state.get("current_priority", "3"),
            "category": state.get("category", "software"),
            "assignment_group": "Level2-Support",
            "auto_resolvable": False, "escalate": False, "confidence": 0.5
        }
    return {"ai_classification": classification,
            **_audit("classify", "done",
                     f"P{classification.get('priority')} | auto_resolve={classification.get('auto_resolvable')} | conf={classification.get('confidence')}")}


def auto_resolve_node(state, config):
    cls = state.get("ai_classification", {})
    sys_id = state.get("incident_sys_id", "")
    note = cls.get("resolution_note", "Auto-resolved by Azentix AI agent based on KB match.")
    try:
        payload = {"state": "6", "close_code": "Solved (Permanently)",
                   "close_notes": f"[Azentix Auto-Resolved] {note}",
                   "work_notes": f"[Azentix Agent] Classified as P{cls.get('priority')} {cls.get('category')}. Auto-resolved."}
        requests.patch(config["snow_url"] + f"/api/now/table/incident/{sys_id}",
                       json=payload, auth=(config["snow_user"], config["snow_pass"]),
                       headers={"Content-Type": "application/json", "Accept": "application/json"}, timeout=10)
    except Exception as e:
        logger.warning(f"ServiceNow update failed: {e}")
    return {"action_taken": "auto_resolved", "final_status": "resolved",
            **_audit("auto_resolve", "resolved", f"Incident {state['incident_number']} auto-resolved")}


def escalate_node(state, config):
    cls = state.get("ai_classification", {})
    sys_id = state.get("incident_sys_id", "")
    try:
        payload = {"state": "2", "priority": cls.get("priority", "2"),
                   "assignment_group": cls.get("assignment_group", "Level2-Support"),
                   "category": cls.get("category", "software"),
                   "work_notes": f"[Azentix Agent] Escalated. Confidence: {cls.get('confidence')}. Assignment: {cls.get('assignment_group')}."}
        requests.patch(config["snow_url"] + f"/api/now/table/incident/{sys_id}",
                       json=payload, auth=(config["snow_user"], config["snow_pass"]),
                       headers={"Content-Type": "application/json", "Accept": "application/json"}, timeout=10)
        # Notify via RabbitMQ
        mq = pika.BlockingConnection(pika.URLParameters(config["rabbitmq_url"]))
        ch = mq.channel()
        ch.queue_declare(queue="notifications", durable=True)
        ch.basic_publish(exchange="", routing_key="notifications",
                         body=json.dumps({"type": "incident_escalated",
                                          "incident": state["incident_number"],
                                          "priority": cls.get("priority"),
                                          "group": cls.get("assignment_group")}),
                         properties=pika.BasicProperties(delivery_mode=2))
        mq.close()
    except Exception as e:
        logger.warning(f"Escalation step failed: {e}")
    return {"action_taken": "escalated", "final_status": "escalated",
            **_audit("escalate", "routed", f"Routed to {cls.get('assignment_group')} P{cls.get('priority')}")}


def notify_node(state):
    return _audit("notify", "done",
                  f"Incident:{state['incident_number']} Action:{state.get('action_taken')} Status:{state.get('final_status')}")


def route_validate(s): return "end" if s.get("final_status") == "validation_failed" else "fetch_incident"
def route_classify(s):
    cls = s.get("ai_classification", {})
    if cls.get("auto_resolvable") and float(cls.get("confidence", 0)) >= 0.8:
        return "auto_resolve"
    return "escalate"


def compile_incident_triage(config: dict):
    llm = AzureChatOpenAI(azure_endpoint=config["azure_endpoint"], api_key=config["azure_key"],
                           azure_deployment="gpt-4o-mini", api_version="2024-08-01-preview", temperature=0.05)
    g = StateGraph(IncidentTriageState)
    g.add_node("validate",      validate_node)
    g.add_node("fetch_incident",lambda s: fetch_incident_node(s, config))
    g.add_node("search_kb",     lambda s: search_kb_node(s, config))
    g.add_node("classify",      lambda s: classify_node(s, llm))
    g.add_node("auto_resolve",  lambda s: auto_resolve_node(s, config))
    g.add_node("escalate",      lambda s: escalate_node(s, config))
    g.add_node("notify",        notify_node)
    g.set_entry_point("validate")
    g.add_conditional_edges("validate", route_validate)
    g.add_edge("fetch_incident", "search_kb")
    g.add_edge("search_kb",      "classify")
    g.add_conditional_edges("classify", route_classify)
    g.add_edge("auto_resolve", "notify")
    g.add_edge("escalate",     "notify")
    g.add_edge("notify", END)
    return g.compile(checkpointer=MemorySaver())


if __name__ == "__main__":
    load_dotenv()
    config = {
        "azure_endpoint": os.getenv("AZURE_OPENAI_ENDPOINT"),
        "azure_key":      os.getenv("AZURE_OPENAI_API_KEY"),
        "snow_url":       os.getenv("SERVICENOW_INSTANCE_URL"),
        "snow_user":      os.getenv("SERVICENOW_USERNAME"),
        "snow_pass":      os.getenv("SERVICENOW_PASSWORD"),
        "rabbitmq_url":   os.getenv("CLOUDAMQP_URL"),
    }
    app = compile_incident_triage(config)
    initial = {
        "incident_number": "INC0001234",
        "incident_sys_id": None, "short_description": None, "description": None,
        "current_state": None, "current_priority": None, "category": None,
        "assignment_group": None, "kb_articles": None, "ai_classification": None,
        "action_taken": None, "audit_trail": [], "final_status": None, "error": None
    }
    result = app.invoke(initial, config={"configurable": {"thread_id": "triage-001"}})
    print(f"Status:  {result['final_status']}")
    print(f"Action:  {result.get('action_taken')}")
    print(f"Priority:{result.get('ai_classification', {}).get('priority')}")
    print("\nAudit Trail:")
    for e in result["audit_trail"]:
        print(f"  [{e['ts'][:19]}] {e['node']:<20} -> {e['result'][:70]}")
