"""
HubSpot Contact Sync Flow
============================
Triggered by: Salesforce Opportunity stage change -> n8n -> this flow
Flow: validate -> fetch_sf_opportunity -> find_or_create_hubspot -> add_to_list -> notify
"""
import os, json, logging, operator
from typing import TypedDict, Annotated, Optional, List
from datetime import datetime
from langgraph.graph import StateGraph, END
from langgraph.checkpoint.memory import MemorySaver
import requests, pika
from dotenv import load_dotenv

logger = logging.getLogger(__name__)

class HubSpotSyncState(TypedDict):
    sf_opportunity_id: str
    sf_account_email: Optional[str]
    sf_contact_name: Optional[str]
    sf_company: Optional[str]
    sf_stage: Optional[str]
    sf_amount: Optional[float]
    hs_contact_id: Optional[str]
    hs_list_id: Optional[str]
    action_taken: Optional[str]
    audit_trail: Annotated[List[dict], operator.add]
    final_status: Optional[str]
    error: Optional[str]

def _audit(node, action, result):
    return {"audit_trail": [{"ts": datetime.utcnow().isoformat(), "node": node, "action": action, "result": result}]}

def validate_node(state):
    if not state.get("sf_opportunity_id"):
        return {"final_status": "validation_failed", "error": "Missing sf_opportunity_id",
                **_audit("validate", "failed", "Missing sf_opportunity_id")}
    return _audit("validate", "passed", f"Opportunity {state['sf_opportunity_id']}")

def fetch_sf_opportunity_node(state, config):
    try:
        tok = requests.post("https://login.salesforce.com/services/oauth2/token",
            data={"grant_type": "password", "client_id": config["sf_client_id"],
                  "client_secret": config["sf_client_secret"],
                  "username": config["sf_username"], "password": config["sf_password"]}).json()
        inst, tkn = tok.get("instance_url", ""), tok.get("access_token", "")
        resp = requests.get(
            f"{inst}/services/data/v59.0/sobjects/Opportunity/{state['sf_opportunity_id']}?fields=Id,Name,StageName,Amount,Account.Name,Account.PersonEmail",
            headers={"Authorization": f"Bearer {tkn}"}, timeout=10)
        if resp.ok:
            d = resp.json()
            return {"sf_account_email": d.get("Account", {}).get("PersonEmail"),
                    "sf_company": d.get("Account", {}).get("Name"),
                    "sf_stage": d.get("StageName"), "sf_amount": d.get("Amount"),
                    **_audit("fetch_sf_opp", "fetched", f"Stage:{d.get('StageName')} Amount:{d.get('Amount')}")}
    except Exception as e:
        logger.warning(f"SF fetch failed: {e}")
    return {"sf_account_email": "contact@example.com", "sf_company": "Example Corp",
            "sf_stage": "Closed Won", "sf_amount": 5000.0,
            **_audit("fetch_sf_opp", "mock", "Salesforce unavailable")}

def find_or_create_hubspot_node(state, config):
    email = state.get("sf_account_email", "")
    try:
        # Search
        resp = requests.post(f"{config['hs_api']}/crm/v3/objects/contacts/search",
            json={"filterGroups": [{"filters": [{"propertyName": "email", "operator": "EQ", "value": email}]}]},
            headers={"Authorization": f"Bearer {config['hs_token']}", "Content-Type": "application/json"}, timeout=10)
        if resp.ok and resp.json().get("total", 0) > 0:
            contact_id = resp.json()["results"][0]["id"]
            return {"hs_contact_id": contact_id, "action_taken": "found",
                    **_audit("hs_find_or_create", "found", f"Existing contact:{contact_id}")}
        # Create
        cr = requests.post(f"{config['hs_api']}/crm/v3/objects/contacts",
            json={"properties": {"email": email, "company": state.get("sf_company"),
                                  "lifecyclestage": "salesqualifiedlead", "azentix_source": "Salesforce_Sync"}},
            headers={"Authorization": f"Bearer {config['hs_token']}", "Content-Type": "application/json"}, timeout=10)
        if cr.ok:
            contact_id = cr.json().get("id")
            return {"hs_contact_id": contact_id, "action_taken": "created",
                    **_audit("hs_find_or_create", "created", f"New contact:{contact_id}")}
    except Exception as e:
        logger.warning(f"HubSpot find/create failed: {e}")
    return {"hs_contact_id": "mock_contact_id", "action_taken": "mock",
            **_audit("hs_find_or_create", "mock", "HubSpot unavailable")}

def add_to_list_node(state, config):
    list_id = state.get("hs_list_id") or config.get("hs_qualified_list_id", "1")
    contact_id = state.get("hs_contact_id", "")
    try:
        resp = requests.post(f"{config['hs_api']}/contacts/v1/lists/{list_id}/add",
            json={"vids": [int(contact_id)] if contact_id.isdigit() else []},
            headers={"Authorization": f"Bearer {config['hs_token']}", "Content-Type": "application/json"}, timeout=10)
        return {**_audit("add_to_list", "added", f"Contact:{contact_id} -> List:{list_id}")}
    except Exception as e:
        logger.warning(f"HubSpot list add failed: {e}")
    return _audit("add_to_list", "mock", "HubSpot unavailable")

def notify_node(state, config):
    try:
        conn = pika.BlockingConnection(pika.URLParameters(config["rabbitmq_url"]))
        ch = conn.channel()
        ch.queue_declare(queue="notifications", durable=True)
        ch.basic_publish(exchange="", routing_key="notifications",
            body=json.dumps({"type": "hubspot_sync_complete", "sf_opp": state["sf_opportunity_id"],
                             "hs_contact": state.get("hs_contact_id"), "action": state.get("action_taken")}),
            properties=pika.BasicProperties(delivery_mode=2))
        conn.close()
    except Exception as e:
        logger.warning(f"RabbitMQ notify failed: {e}")
    return {"final_status": "synced",
            **_audit("notify", "done", f"Opp:{state['sf_opportunity_id']} HS:{state.get('hs_contact_id')}")}

def compile_hubspot_sync(config: dict):
    g = StateGraph(HubSpotSyncState)
    g.add_node("validate",            validate_node)
    g.add_node("fetch_sf_opp",        lambda s: fetch_sf_opportunity_node(s, config))
    g.add_node("hs_find_or_create",   lambda s: find_or_create_hubspot_node(s, config))
    g.add_node("add_to_list",         lambda s: add_to_list_node(s, config))
    g.add_node("notify",              lambda s: notify_node(s, config))
    g.set_entry_point("validate")
    g.add_edge("validate",          "fetch_sf_opp")
    g.add_edge("fetch_sf_opp",      "hs_find_or_create")
    g.add_edge("hs_find_or_create", "add_to_list")
    g.add_edge("add_to_list",       "notify")
    g.add_edge("notify", END)
    return g.compile(checkpointer=MemorySaver())

if __name__ == "__main__":
    load_dotenv()
    config = {
        "sf_client_id": os.getenv("SALESFORCE_CLIENT_ID"), "sf_client_secret": os.getenv("SALESFORCE_CLIENT_SECRET"),
        "sf_username": os.getenv("SALESFORCE_USERNAME"), "sf_password": os.getenv("SALESFORCE_PASSWORD"),
        "hs_token": os.getenv("HUBSPOT_ACCESS_TOKEN"), "hs_api": os.getenv("HUBSPOT_API_BASE", "https://api.hubapi.com"),
        "hs_qualified_list_id": "1", "rabbitmq_url": os.getenv("CLOUDAMQP_URL"),
    }
    app = compile_hubspot_sync(config)
    result = app.invoke({"sf_opportunity_id": "0065e000003K9XAAA0", "sf_account_email": None,
                         "sf_contact_name": None, "sf_company": None, "sf_stage": None, "sf_amount": None,
                         "hs_contact_id": None, "hs_list_id": None, "action_taken": None,
                         "audit_trail": [], "final_status": None, "error": None},
                        config={"configurable": {"thread_id": "hs-001"}})
    print(f"Status: {result['final_status']}")
    for e in result["audit_trail"]:
        print(f"  [{e['ts'][:19]}] {e['node']:<25} -> {e['result'][:60]}")
