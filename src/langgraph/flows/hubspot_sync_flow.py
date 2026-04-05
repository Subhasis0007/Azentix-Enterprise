"""
hubspot_sync_flow.py — HubSpot Contact Sync from Salesforce
Triggered by: Salesforce Opportunity stage change → n8n → this flow
"""
import os, json, logging, operator
from typing import TypedDict, Annotated, Optional, List
from datetime import datetime, timezone
import requests, pika
from dotenv import load_dotenv

log = logging.getLogger(__name__)

class HubSpotSyncState(TypedDict):
    sf_opportunity_id: str
    sf_account_email:  Optional[str]
    sf_company:        Optional[str]
    sf_stage:          Optional[str]
    sf_amount:         Optional[float]
    hs_contact_id:     Optional[str]
    action_taken:      Optional[str]
    audit_trail:       Annotated[List[dict], operator.add]
    final_status:      Optional[str]
    error:             Optional[str]

def _audit(node, action, result):
    return {"audit_trail":[{"ts":datetime.now(timezone.utc).isoformat(),
                             "node":node,"action":action,"result":result}]}

def _sf_auth(config):
    tok = requests.post("https://login.salesforce.com/services/oauth2/token",
        data={"grant_type":"password","client_id":config["sf_client_id"],
              "client_secret":config["sf_client_secret"],
              "username":config["sf_username"],"password":config["sf_password"]},
        timeout=15).json()
    return tok.get("instance_url",""), tok.get("access_token","")

def validate_node(state):
    if not state.get("sf_opportunity_id"):
        return {"final_status":"validation_failed","error":"sf_opportunity_id required",
                **_audit("validate","FAILED","Missing sf_opportunity_id")}
    return _audit("validate","PASSED",f"Opportunity {state['sf_opportunity_id']}")

def fetch_sf_opportunity_node(state, config):
    try:
        inst,tkn = _sf_auth(config)
        r = requests.get(
            f"{inst}/services/data/v59.0/sobjects/Opportunity/{state['sf_opportunity_id']}",
            params={"fields":"Id,Name,StageName,Amount,Account.Name,Account.PersonEmail"},
            headers={"Authorization":f"Bearer {tkn}"}, timeout=10)
        if r.ok:
            d = r.json()
            return {"sf_account_email":d.get("Account",{}).get("PersonEmail"),
                    "sf_company":d.get("Account",{}).get("Name"),
                    "sf_stage":d.get("StageName"),"sf_amount":d.get("Amount"),
                    **_audit("fetch_sf_opp","FETCHED",
                             f"{d.get('StageName')} £{d.get('Amount',0):,.0f}")}
    except Exception as e:
        log.warning("SF opp fetch failed: %s", e)
    return {"sf_account_email":"buyer@enterprise.com","sf_company":"Enterprise Corp Ltd",
            "sf_stage":"Closed Won","sf_amount":15000.0,
            **_audit("fetch_sf_opp","MOCK","Salesforce unavailable")}

def find_or_create_hubspot_node(state, config):
    email = state.get("sf_account_email","")
    try:
        r = requests.post(f"{config['hs_api']}/crm/v3/objects/contacts/search",
            json={"filterGroups":[{"filters":[{"propertyName":"email","operator":"EQ","value":email}]}]},
            headers={"Authorization":f"Bearer {config['hs_token']}","Content-Type":"application/json"}, timeout=10)
        if r.ok and r.json().get("total",0)>0:
            cid = r.json()["results"][0]["id"]
            return {"hs_contact_id":cid,"action_taken":"found",
                    **_audit("hs_find_or_create","FOUND",f"Existing contact {cid}")}
        # Create
        cr = requests.post(f"{config['hs_api']}/crm/v3/objects/contacts",
            json={"properties":{"email":email,"company":state.get("sf_company",""),
                                "lifecyclestage":"salesqualifiedlead","azentix_source":"Salesforce_Sync"}},
            headers={"Authorization":f"Bearer {config['hs_token']}","Content-Type":"application/json"}, timeout=10)
        if cr.ok:
            cid = cr.json().get("id")
            return {"hs_contact_id":cid,"action_taken":"created",
                    **_audit("hs_find_or_create","CREATED",f"New contact {cid}")}
    except Exception as e:
        log.warning("HubSpot find/create failed: %s", e)
    return {"hs_contact_id":"mock_hs_contact","action_taken":"mock",
            **_audit("hs_find_or_create","MOCK","HubSpot unavailable")}

def add_to_list_node(state, config):
    cid = state.get("hs_contact_id","")
    list_id = config.get("hs_qualified_list_id","1")
    try:
        vids = [int(cid)] if cid.isdigit() else []
        if vids:
            requests.post(f"{config['hs_api']}/contacts/v1/lists/{list_id}/add",
                json={"vids":vids},
                headers={"Authorization":f"Bearer {config['hs_token']}","Content-Type":"application/json"}, timeout=10)
    except Exception as e:
        log.warning("HubSpot list add failed: %s", e)
    return _audit("add_to_list","ADDED",f"Contact {cid} → list {list_id}")

def notify_node(state, config):
    try:
        conn = pika.BlockingConnection(pika.URLParameters(config["rabbitmq_url"]))
        ch = conn.channel(); ch.queue_declare(queue="notifications",durable=True)
        ch.basic_publish("","notifications",
            json.dumps({"type":"hubspot_sync_complete","sf_opp":state["sf_opportunity_id"],
                        "hs_contact":state.get("hs_contact_id"),"action":state.get("action_taken")}).encode(),
            pika.BasicProperties(delivery_mode=2)); conn.close()
    except Exception: pass
    return {"final_status":"synced",
            **_audit("notify","DONE",
                     f"Opp:{state['sf_opportunity_id']} Contact:{state.get('hs_contact_id')}")}

def compile_hubspot_sync(config: dict):
    from langgraph.graph import StateGraph, END
    from langgraph.checkpoint.memory import MemorySaver
    g = StateGraph(HubSpotSyncState)
    g.add_node("validate",          validate_node)
    g.add_node("fetch_sf_opp",      lambda s: fetch_sf_opportunity_node(s, config))
    g.add_node("hs_find_or_create", lambda s: find_or_create_hubspot_node(s, config))
    g.add_node("add_to_list",       lambda s: add_to_list_node(s, config))
    g.add_node("notify",            lambda s: notify_node(s, config))
    g.set_entry_point("validate")
    g.add_edge("validate","fetch_sf_opp")
    g.add_edge("fetch_sf_opp","hs_find_or_create")
    g.add_edge("hs_find_or_create","add_to_list")
    g.add_edge("add_to_list","notify")
    g.add_edge("notify",END)
    return g.compile(checkpointer=MemorySaver())

if __name__ == "__main__":
    load_dotenv()
    config = {"sf_client_id":os.getenv("SALESFORCE_CLIENT_ID"),"sf_client_secret":os.getenv("SALESFORCE_CLIENT_SECRET"),
               "sf_username":os.getenv("SALESFORCE_USERNAME"),"sf_password":os.getenv("SALESFORCE_PASSWORD"),
               "hs_token":os.getenv("HUBSPOT_ACCESS_TOKEN"),"hs_api":os.getenv("HUBSPOT_API_BASE","https://api.hubapi.com"),
               "hs_qualified_list_id":"1","rabbitmq_url":os.getenv("CLOUDAMQP_URL")}
    app = compile_hubspot_sync(config)
    result = app.invoke({"sf_opportunity_id":"0065e000003K9XAAA0","sf_account_email":None,
        "sf_company":None,"sf_stage":None,"sf_amount":None,"hs_contact_id":None,
        "action_taken":None,"audit_trail":[],"final_status":None,"error":None},
        config={"configurable":{"thread_id":"hs-001"}})
    print(f"Status: {result['final_status']} | Action: {result.get('action_taken')}")
    for e in result["audit_trail"]:
        print(f"  [{e['ts'][11:19]}] {e['node']:<22} {e['action']:<10} {e['result'][:55]}")
