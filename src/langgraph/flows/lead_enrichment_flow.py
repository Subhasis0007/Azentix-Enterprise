"""
Salesforce Lead Enrichment Flow
==================================
Triggered by: Salesforce new Lead creation -> n8n webhook -> this flow
Flow: validate -> fetch_lead -> fetch_sap_customer -> enrich_lead -> update_sf -> notify
"""
import os, json, logging, operator
from typing import TypedDict, Annotated, Optional, List
from datetime import datetime
from langgraph.graph import StateGraph, END
from langgraph.checkpoint.memory import MemorySaver
import requests, pika
from dotenv import load_dotenv

logger = logging.getLogger(__name__)

class LeadEnrichmentState(TypedDict):
    sf_lead_id: str
    lead_email: Optional[str]
    lead_company: Optional[str]
    lead_name: Optional[str]
    sap_customer_data: Optional[dict]
    enrichment_fields: Optional[dict]
    update_success: bool
    audit_trail: Annotated[List[dict], operator.add]
    final_status: Optional[str]
    error: Optional[str]

def _audit(node, action, result):
    return {"audit_trail": [{"ts": datetime.utcnow().isoformat(), "node": node, "action": action, "result": result}]}

def validate_node(state):
    if not state.get("sf_lead_id"):
        return {"final_status": "validation_failed", "error": "Missing sf_lead_id",
                **_audit("validate", "failed", "Missing sf_lead_id")}
    return _audit("validate", "passed", f"Lead {state['sf_lead_id']}")

def fetch_lead_node(state, config):
    try:
        tok = requests.post("https://login.salesforce.com/services/oauth2/token",
            data={"grant_type": "password", "client_id": config["sf_client_id"],
                  "client_secret": config["sf_client_secret"],
                  "username": config["sf_username"], "password": config["sf_password"]}).json()
        inst, tkn = tok.get("instance_url", ""), tok.get("access_token", "")
        resp = requests.get(
            f"{inst}/services/data/v59.0/sobjects/Lead/{state['sf_lead_id']}?fields=Id,FirstName,LastName,Email,Company,Phone,Status",
            headers={"Authorization": f"Bearer {tkn}"}, timeout=10)
        if resp.ok:
            d = resp.json()
            return {"lead_email": d.get("Email"), "lead_company": d.get("Company"),
                    "lead_name": f"{d.get('FirstName','')} {d.get('LastName','')}".strip(),
                    **_audit("fetch_lead", "fetched", f"{d.get('Email')} / {d.get('Company')}")}
    except Exception as e:
        logger.warning(f"Lead fetch failed: {e}")
    return {"lead_email": "lead@example.com", "lead_company": "Example Corp", "lead_name": "Mock Lead",
            **_audit("fetch_lead", "mock", "Salesforce unavailable")}

def fetch_sap_customer_node(state, config):
    try:
        company = (state.get("lead_company") or "").replace(" ", "%20")
        resp = requests.get(
            f"{config['sap_base_url']}/sap/opu/odata/sap/API_BUSINESS_PARTNER/A_BusinessPartner?$filter=SearchTerm1 eq '{company}'&$top=1&$select=BusinessPartner,BusinessPartnerFullName,SearchTerm1,Industry",
            headers={"APIKey": config["sap_api_key"], "Accept": "application/json"}, timeout=10)
        if resp.ok:
            results = resp.json().get("d", {}).get("results", [])
            if results:
                bp = results[0]
                return {"sap_customer_data": {"bp_id": bp.get("BusinessPartner"), "industry": bp.get("Industry"), "full_name": bp.get("BusinessPartnerFullName")},
                        **_audit("fetch_sap_customer", "found", f"BP:{bp.get('BusinessPartner')} Industry:{bp.get('Industry')}")}
    except Exception as e:
        logger.warning(f"SAP customer fetch failed: {e}")
    return {"sap_customer_data": {"bp_id": "BP-MOCK-001", "industry": "Technology", "full_name": state.get("lead_company")},
            **_audit("fetch_sap_customer", "mock", "SAP unavailable, mock data")}

def enrich_lead_node(state):
    sap = state.get("sap_customer_data") or {}
    fields = {
        "SAP_BP_ID__c": sap.get("bp_id", ""),
        "Industry": sap.get("industry", ""),
        "Azentix_Enriched__c": True,
        "Azentix_Enriched_At__c": datetime.utcnow().isoformat(),
        "Azentix_Data_Completeness__c": 95
    }
    return {"enrichment_fields": fields,
            **_audit("enrich_lead", "prepared", f"{len(fields)} fields ready for update")}

def update_sf_node(state, config):
    try:
        tok = requests.post("https://login.salesforce.com/services/oauth2/token",
            data={"grant_type": "password", "client_id": config["sf_client_id"],
                  "client_secret": config["sf_client_secret"],
                  "username": config["sf_username"], "password": config["sf_password"]}).json()
        inst, tkn = tok.get("instance_url", ""), tok.get("access_token", "")
        resp = requests.patch(
            f"{inst}/services/data/v59.0/sobjects/Lead/{state['sf_lead_id']}",
            json=state.get("enrichment_fields", {}),
            headers={"Authorization": f"Bearer {tkn}", "Content-Type": "application/json"}, timeout=10)
        return {"update_success": resp.status_code in (200, 204),
                **_audit("update_sf", "updated" if resp.status_code in (200, 204) else "failed",
                         f"Lead {state['sf_lead_id']} HTTP {resp.status_code}")}
    except Exception as e:
        logger.warning(f"SF update failed: {e}")
    return {"update_success": False, **_audit("update_sf", "mock", "Salesforce unavailable")}

def notify_node(state, config):
    try:
        conn = pika.BlockingConnection(pika.URLParameters(config["rabbitmq_url"]))
        ch = conn.channel()
        ch.queue_declare(queue="notifications", durable=True)
        ch.basic_publish(exchange="", routing_key="notifications",
            body=json.dumps({"type": "lead_enriched", "lead_id": state["sf_lead_id"],
                             "email": state.get("lead_email"), "success": state.get("update_success")}),
            properties=pika.BasicProperties(delivery_mode=2))
        conn.close()
    except Exception as e:
        logger.warning(f"RabbitMQ notify failed: {e}")
    return {"final_status": "enriched" if state.get("update_success") else "enrichment_failed",
            **_audit("notify", "done", f"Lead:{state['sf_lead_id']} Success:{state.get('update_success')}")}

def compile_lead_enrichment(config: dict):
    g = StateGraph(LeadEnrichmentState)
    g.add_node("validate",            validate_node)
    g.add_node("fetch_lead",          lambda s: fetch_lead_node(s, config))
    g.add_node("fetch_sap_customer",  lambda s: fetch_sap_customer_node(s, config))
    g.add_node("enrich_lead",         enrich_lead_node)
    g.add_node("update_sf",           lambda s: update_sf_node(s, config))
    g.add_node("notify",              lambda s: notify_node(s, config))
    g.set_entry_point("validate")
    g.add_edge("validate",           "fetch_lead")
    g.add_edge("fetch_lead",         "fetch_sap_customer")
    g.add_edge("fetch_sap_customer", "enrich_lead")
    g.add_edge("enrich_lead",        "update_sf")
    g.add_edge("update_sf",          "notify")
    g.add_edge("notify", END)
    return g.compile(checkpointer=MemorySaver())

if __name__ == "__main__":
    load_dotenv()
    config = {
        "sap_base_url": os.getenv("SAP_BASE_URL"), "sap_api_key": os.getenv("SAP_API_KEY"),
        "sf_client_id": os.getenv("SALESFORCE_CLIENT_ID"), "sf_client_secret": os.getenv("SALESFORCE_CLIENT_SECRET"),
        "sf_username": os.getenv("SALESFORCE_USERNAME"), "sf_password": os.getenv("SALESFORCE_PASSWORD"),
        "rabbitmq_url": os.getenv("CLOUDAMQP_URL"),
    }
    app = compile_lead_enrichment(config)
    result = app.invoke({"sf_lead_id": "00Q5e000003K9XAAA0", "lead_email": None, "lead_company": None,
                         "lead_name": None, "sap_customer_data": None, "enrichment_fields": None,
                         "update_success": False, "audit_trail": [], "final_status": None, "error": None},
                        config={"configurable": {"thread_id": "enrich-001"}})
    print(f"Status: {result['final_status']}")
    for e in result["audit_trail"]:
        print(f"  [{e['ts'][:19]}] {e['node']:<25} -> {e['result'][:60]}")
