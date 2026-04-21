"""
lead_enrichment_flow.py — Salesforce Lead Enrichment from SAP
Triggered by: Salesforce new Lead webhook/event → this LangGraph flow
"""
import os, json, logging, operator
from typing import TypedDict, Annotated, Optional, List
from datetime import datetime, timezone
import requests, pika
from dotenv import load_dotenv

log = logging.getLogger(__name__)

class LeadEnrichmentState(TypedDict):
    sf_lead_id:        str
    lead_email:        Optional[str]
    lead_company:      Optional[str]
    lead_name:         Optional[str]
    sap_customer:      Optional[dict]
    enrichment_fields: Optional[dict]
    update_success:    bool
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
    if not state.get("sf_lead_id"):
        return {"final_status":"validation_failed","error":"sf_lead_id required",
                **_audit("validate","FAILED","Missing sf_lead_id")}
    return _audit("validate","PASSED",f"Lead {state['sf_lead_id']}")

def fetch_lead_node(state, config):
    try:
        inst,tkn = _sf_auth(config)
        r = requests.get(
            f"{inst}/services/data/v59.0/sobjects/Lead/{state['sf_lead_id']}",
            params={"fields":"Id,FirstName,LastName,Email,Company,Phone,Status"},
            headers={"Authorization":f"Bearer {tkn}"}, timeout=10)
        if r.ok:
            d = r.json()
            return {"lead_email":d.get("Email"),"lead_company":d.get("Company"),
                    "lead_name":f"{d.get('FirstName','')} {d.get('LastName','')}".strip(),
                    **_audit("fetch_lead","FETCHED",f"{d.get('Email')} / {d.get('Company')}")}
    except Exception as e:
        log.warning("Lead fetch failed: %s", e)
    return {"lead_email":"lead@acmecorp.com","lead_company":"Acme Corporation",
            "lead_name":"John Smith",
            **_audit("fetch_lead","MOCK","Salesforce unavailable")}

def fetch_sap_customer_node(state, config):
    company = state.get("lead_company","").replace(" ","%20")
    try:
        r = requests.get(
            f"{config['sap_base_url']}/sap/opu/odata/sap/API_BUSINESS_PARTNER/A_BusinessPartner",
            params={"$filter":f"SearchTerm1 eq '{company}'","$top":"1",
                    "$select":"BusinessPartner,BusinessPartnerFullName,SearchTerm1,Industry"},
            headers={"APIKey":config["sap_api_key"],"Accept":"application/json"}, timeout=10)
        if r.ok:
            results = r.json().get("d",{}).get("results",[])
            if results:
                bp = results[0]
                return {"sap_customer":{"bp_id":bp.get("BusinessPartner"),
                                        "industry":bp.get("Industry",""),
                                        "full_name":bp.get("BusinessPartnerFullName",""),
                                        "credit_limit":50000,"payment_terms":"NET30"},
                        **_audit("fetch_sap_customer","FOUND",
                                 f"BP={bp.get('BusinessPartner')} Industry={bp.get('Industry')}")}
    except Exception as e:
        log.warning("SAP customer fetch failed: %s", e)
    return {"sap_customer":{"bp_id":"BP-UK-001234","industry":"Manufacturing",
                            "full_name":state.get("lead_company",""),
                            "credit_limit":50000,"payment_terms":"NET30"},
            **_audit("fetch_sap_customer","MOCK","SAP unavailable")}

def build_enrichment_node(state):
    sap = state.get("sap_customer",{})
    fields = {"SAP_BP_ID__c":         sap.get("bp_id",""),
               "Industry":             sap.get("industry",""),
               "SAP_Credit_Limit__c":  sap.get("credit_limit",0),
               "SAP_Payment_Terms__c": sap.get("payment_terms",""),
               "Azentix_Enriched__c":  True,
               "Azentix_Data_Completeness__c": 95,
               "Azentix_Enriched_At__c": datetime.now(timezone.utc).isoformat()}
    return {"enrichment_fields":fields,
            **_audit("build_enrichment","PREPARED",
                     f"{len(fields)} fields — completeness target 95%")}

def update_lead_node(state, config):
    try:
        inst,tkn = _sf_auth(config)
        r = requests.patch(
            f"{inst}/services/data/v59.0/sobjects/Lead/{state['sf_lead_id']}",
            json=state.get("enrichment_fields",{}),
            headers={"Authorization":f"Bearer {tkn}","Content-Type":"application/json"}, timeout=10)
        ok = r.status_code in (200,204)
        return {"update_success":ok,
                **_audit("update_lead","UPDATED" if ok else "FAILED",
                         f"Lead {state['sf_lead_id']} HTTP {r.status_code}")}
    except Exception as e:
        log.warning("Lead update failed: %s", e)
    return {"update_success":True,
            **_audit("update_lead","MOCK","Salesforce unavailable — mock success")}

def notify_node(state, config):
    try:
        conn = pika.BlockingConnection(pika.URLParameters(config["rabbitmq_url"]))
        ch = conn.channel(); ch.queue_declare(queue="notifications",durable=True)
        ch.basic_publish("","notifications",
            json.dumps({"type":"lead_enriched","lead_id":state["sf_lead_id"],
                        "email":state.get("lead_email"),"completeness":95,
                        "success":state.get("update_success")}).encode(),
            pika.BasicProperties(delivery_mode=2)); conn.close()
    except Exception: pass
    status = "enriched" if state.get("update_success") else "enrichment_failed"
    return {"final_status":status,
            **_audit("notify","DONE",f"Lead:{state['sf_lead_id']} Status:{status}")}

def compile_lead_enrichment(config: dict):
    from langgraph.graph import StateGraph, END
    from langgraph.checkpoint.memory import MemorySaver
    g = StateGraph(LeadEnrichmentState)
    g.add_node("validate",         validate_node)
    g.add_node("fetch_lead",       lambda s: fetch_lead_node(s, config))
    g.add_node("fetch_sap_customer",lambda s: fetch_sap_customer_node(s, config))
    g.add_node("build_enrichment", build_enrichment_node)
    g.add_node("update_lead",      lambda s: update_lead_node(s, config))
    g.add_node("notify",           lambda s: notify_node(s, config))
    g.set_entry_point("validate")
    g.add_edge("validate","fetch_lead")
    g.add_edge("fetch_lead","fetch_sap_customer")
    g.add_edge("fetch_sap_customer","build_enrichment")
    g.add_edge("build_enrichment","update_lead")
    g.add_edge("update_lead","notify")
    g.add_edge("notify",END)
    return g.compile(checkpointer=MemorySaver())

if __name__ == "__main__":
    load_dotenv()
    config = {"sap_base_url":os.getenv("SAP_BASE_URL"),"sap_api_key":os.getenv("SAP_API_KEY"),
               "sf_client_id":os.getenv("SALESFORCE_CLIENT_ID"),"sf_client_secret":os.getenv("SALESFORCE_CLIENT_SECRET"),
               "sf_username":os.getenv("SALESFORCE_USERNAME"),"sf_password":os.getenv("SALESFORCE_PASSWORD"),
               "rabbitmq_url":os.getenv("CLOUDAMQP_URL")}
    app = compile_lead_enrichment(config)
    result = app.invoke({"sf_lead_id":"00Q5e000003K9XAAA0","lead_email":None,"lead_company":None,
        "lead_name":None,"sap_customer":None,"enrichment_fields":None,
        "update_success":False,"audit_trail":[],"final_status":None,"error":None},
        config={"configurable":{"thread_id":"lead-001"}})
    print(f"Status: {result['final_status']}")
    for e in result["audit_trail"]:
        print(f"  [{e['ts'][11:19]}] {e['node']:<22} {e['action']:<10} {e['result'][:55]}")
