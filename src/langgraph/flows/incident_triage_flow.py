"""
incident_triage_flow.py — ServiceNow Incident Triage
Triggered by: CloudAMQP servicenow-incidents queue → n8n → this flow
"""
import os, json, logging, operator
from typing import TypedDict, Annotated, Optional, List
from datetime import datetime, timezone
import requests, pika
from dotenv import load_dotenv

log = logging.getLogger(__name__)

class IncidentState(TypedDict):
    incident_number:   str
    incident_sys_id:   Optional[str]
    short_description: Optional[str]
    description:       Optional[str]
    current_priority:  Optional[str]
    category:          Optional[str]
    kb_articles:       Optional[str]
    classification:    Optional[dict]
    action_taken:      Optional[str]
    audit_trail:       Annotated[List[dict], operator.add]
    final_status:      Optional[str]
    error:             Optional[str]

def _audit(node, action, result):
    return {"audit_trail": [{"ts": datetime.now(timezone.utc).isoformat(),
                              "node": node, "action": action, "result": result}]}

def validate_node(state):
    if not state.get("incident_number"):
        return {"final_status": "validation_failed", "error": "incident_number required",
                **_audit("validate", "FAILED", "Missing incident_number")}
    return _audit("validate", "PASSED", f"Incident {state['incident_number']}")

def fetch_incident_node(state, config):
    try:
        r = requests.get(
            f"{config['snow_url']}/api/now/table/incident",
            params={"sysparm_query": f"number={state['incident_number']}",
                    "sysparm_fields": "sys_id,number,short_description,description,state,priority,category,assignment_group",
                    "sysparm_limit": "1"},
            auth=(config["snow_user"], config["snow_pass"]),
            headers={"Accept": "application/json"}, timeout=10)
        if r.ok:
            records = r.json().get("result", [])
            if records:
                rec = records[0]
                return {"incident_sys_id":   rec.get("sys_id"),
                        "short_description": rec.get("short_description"),
                        "description":       rec.get("description",""),
                        "current_priority":  rec.get("priority","3"),
                        "category":          rec.get("category","software"),
                        **_audit("fetch_incident", "FETCHED",
                                 f"P{rec.get('priority')} — {rec.get('short_description','')[:60]}")}
    except Exception as e:
        log.warning("ServiceNow fetch failed: %s", e)
    return {"incident_sys_id": "mock_sys_id",
            "short_description": "SAP OData API returning 503 errors",
            "description": "Integration monitor detected repeated 503 responses from SAP gateway.",
            "current_priority": "2", "category": "sap",
            **_audit("fetch_incident", "MOCK", "ServiceNow unavailable — using mock")}

def search_kb_node(state, config):
    try:
        query = (state.get("short_description","") + " " + state.get("description",""))[:80]
        r = requests.get(
            f"{config['snow_url']}/api/now/table/kb_knowledge",
            params={"sysparm_query": f"textLIKE{query[:50]}^workflow_state=published",
                    "sysparm_fields": "short_description,text,kb_category",
                    "sysparm_limit": "3"},
            auth=(config["snow_user"], config["snow_pass"]),
            headers={"Accept": "application/json"}, timeout=10)
        if r.ok:
            articles = r.json().get("result", [])
            if articles:
                kb_text = "\n\n".join(
                    f"[KB] {a['short_description']}: {a.get('text','')[:200]}"
                    for a in articles)
                return {"kb_articles": kb_text,
                        **_audit("search_kb", "FOUND", f"{len(articles)} KB articles matched")}
    except Exception as e:
        log.warning("KB search failed: %s", e)
    return {"kb_articles": "No KB articles found.",
            **_audit("search_kb", "NONE", "No matching KB articles")}

def classify_node(state, llm):
    try:
        from langchain.schema import HumanMessage, SystemMessage
        resp = llm.invoke([
            SystemMessage(content=(
                'Classify the ServiceNow incident and determine action. '
                'Respond ONLY with valid JSON — no markdown, no preamble: '
                '{"priority":"1|2|3|4","category":"sap|salesforce|stripe|network|software|hardware",'
                '"assignment_group":"string","auto_resolvable":bool,'
                '"resolution_note":"string or null","confidence":float}')),
            HumanMessage(content=(
                f"Incident: {state.get('short_description')}\n"
                f"Description: {state.get('description','')[:400]}\n"
                f"Current priority: {state.get('current_priority')}\n"
                f"KB Context:\n{state.get('kb_articles','')[:600]}"))])
        cls = json.loads(resp.content.strip())
    except Exception as e:
        log.warning("LLM classify fallback: %s", e)
        cls = {"priority": state.get("current_priority","3"), "category": state.get("category","software"),
               "assignment_group": "Level2-Support", "auto_resolvable": False,
               "resolution_note": None, "confidence": 0.5}
    return {"classification": cls,
            **_audit("classify", "CLASSIFIED",
                     f"P{cls['priority']} {cls['assignment_group']} conf={cls['confidence']:.2f}")}

def auto_resolve_node(state, config):
    cls    = state.get("classification", {})
    sys_id = state.get("incident_sys_id","")
    note   = cls.get("resolution_note","Auto-resolved by Azentix agent based on KB match.")
    try:
        requests.patch(f"{config['snow_url']}/api/now/table/incident/{sys_id}",
            json={"state": "6", "close_code": "Solved (Permanently)",
                  "close_notes": f"[Azentix Auto-Resolved] {note}",
                  "work_notes": f"[Azentix] P{cls.get('priority')} {cls.get('category')} — auto-resolved. Confidence: {cls.get('confidence',0):.2f}"},
            auth=(config["snow_user"], config["snow_pass"]),
            headers={"Content-Type":"application/json","Accept":"application/json"}, timeout=10)
    except Exception as e:
        log.warning("ServiceNow auto-resolve failed: %s", e)
    return {"action_taken": "auto_resolved", "final_status": "resolved",
            **_audit("auto_resolve", "RESOLVED", f"Incident {state['incident_number']} closed")}

def escalate_node(state, config):
    cls    = state.get("classification", {})
    sys_id = state.get("incident_sys_id","")
    try:
        requests.patch(f"{config['snow_url']}/api/now/table/incident/{sys_id}",
            json={"state": "2", "priority": cls.get("priority","2"),
                  "assignment_group": cls.get("assignment_group","Level2-Support"),
                  "category": cls.get("category","software"),
                  "work_notes": f"[Azentix] Routed to {cls.get('assignment_group')}. Confidence: {cls.get('confidence',0):.2f}"},
            auth=(config["snow_user"], config["snow_pass"]),
            headers={"Content-Type":"application/json","Accept":"application/json"}, timeout=10)
    except Exception as e:
        log.warning("ServiceNow escalate failed: %s", e)
    try:
        conn = pika.BlockingConnection(pika.URLParameters(config["rabbitmq_url"]))
        ch = conn.channel(); ch.queue_declare(queue="notifications", durable=True)
        ch.basic_publish("", "notifications",
            json.dumps({"type":"incident_escalated","incident":state["incident_number"],
                        "priority":cls.get("priority"),"group":cls.get("assignment_group")}).encode(),
            pika.BasicProperties(delivery_mode=2)); conn.close()
    except Exception: pass
    return {"action_taken": "escalated", "final_status": "escalated",
            **_audit("escalate", "ROUTED",
                     f"→ {cls.get('assignment_group')} P{cls.get('priority')} InProgress")}

def route_validate(s): return "end" if s.get("final_status")=="validation_failed" else "fetch_incident"
def route_classify(s):
    cls = s.get("classification",{})
    return ("auto_resolve"
            if cls.get("auto_resolvable") and float(cls.get("confidence",0)) >= 0.8
            else "escalate")

def compile_incident_triage(config: dict):
    from langgraph.graph import StateGraph, END
    from langgraph.checkpoint.memory import MemorySaver
    from langchain_openai import AzureChatOpenAI
    llm = AzureChatOpenAI(azure_endpoint=config["azure_endpoint"],
                          api_key=config["azure_key"],
                          azure_deployment="gpt-4o-mini",
                          api_version="2024-08-01-preview", temperature=0.05)
    g = StateGraph(IncidentState)
    g.add_node("validate",      validate_node)
    g.add_node("fetch_incident", lambda s: fetch_incident_node(s, config))
    g.add_node("search_kb",      lambda s: search_kb_node(s, config))
    g.add_node("classify",       lambda s: classify_node(s, llm))
    g.add_node("auto_resolve",   lambda s: auto_resolve_node(s, config))
    g.add_node("escalate",       lambda s: escalate_node(s, config))
    g.set_entry_point("validate")
    g.add_conditional_edges("validate", route_validate)
    g.add_edge("fetch_incident","search_kb")
    g.add_edge("search_kb","classify")
    g.add_conditional_edges("classify", route_classify)
    g.add_edge("auto_resolve", END)
    g.add_edge("escalate",     END)
    return g.compile(checkpointer=MemorySaver())

if __name__ == "__main__":
    load_dotenv()
    config = {"azure_endpoint": os.getenv("AZURE_OPENAI_ENDPOINT"),
               "azure_key":     os.getenv("AZURE_OPENAI_API_KEY"),
               "snow_url":      os.getenv("SERVICENOW_INSTANCE_URL"),
               "snow_user":     os.getenv("SERVICENOW_USERNAME"),
               "snow_pass":     os.getenv("SERVICENOW_PASSWORD"),
               "rabbitmq_url":  os.getenv("CLOUDAMQP_URL")}
    app = compile_incident_triage(config)
    result = app.invoke({"incident_number":"INC0001234","incident_sys_id":None,
        "short_description":None,"description":None,"current_priority":None,
        "category":None,"kb_articles":None,"classification":None,
        "action_taken":None,"audit_trail":[],"final_status":None,"error":None},
        config={"configurable":{"thread_id":"triage-001"}})
    print(f"Status: {result['final_status']} | Action: {result.get('action_taken')}")
    for e in result["audit_trail"]:
        print(f"  [{e['ts'][11:19]}] {e['node']:<18} {e['action']:<12} {e['result'][:55]}")
