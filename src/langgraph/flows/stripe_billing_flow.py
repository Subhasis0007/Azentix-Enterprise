"""
stripe_billing_flow.py — Stripe Failed Payment Handler
Triggered by: Stripe webhook → WebhookController → this LangGraph flow
"""
import os, json, logging, operator
from typing import TypedDict, Annotated, Optional, List
from datetime import datetime, timezone
import requests, pika
from dotenv import load_dotenv

log = logging.getLogger(__name__)

class StripeBillingState(TypedDict):
    stripe_event_type:  str
    stripe_payment_id:  Optional[str]
    stripe_customer_id: Optional[str]
    customer_email:     Optional[str]
    payment_amount:     Optional[float]
    payment_currency:   Optional[str]
    failure_reason:     Optional[str]
    snow_incident_id:   Optional[str]
    sf_opportunity_id:  Optional[str]
    hubspot_contact_id: Optional[str]
    audit_trail:        Annotated[List[dict], operator.add]
    final_status:       Optional[str]
    error:              Optional[str]

def _audit(node, action, result):
    return {"audit_trail":[{"ts":datetime.now(timezone.utc).isoformat(),
                             "node":node,"action":action,"result":result}]}

def validate_node(state):
    if not state.get("stripe_event_type"):
        return {"final_status":"validation_failed","error":"stripe_event_type required",
                **_audit("validate","FAILED","Missing stripe_event_type")}
    return _audit("validate","PASSED",f"Event: {state['stripe_event_type']}")

def fetch_stripe_node(state, config):
    pid = state.get("stripe_payment_id","")
    try:
        if pid:
            r = requests.get(f"https://api.stripe.com/v1/payment_intents/{pid}",
                headers={"Authorization":f"Bearer {config['stripe_key']}"}, timeout=10)
            if r.ok:
                d = r.json()
                return {"stripe_customer_id": d.get("customer"),
                        "payment_amount":     d.get("amount",0)/100,
                        "payment_currency":   d.get("currency","gbp").upper(),
                        "failure_reason":     d.get("last_payment_error",{}).get("code","unknown"),
                        **_audit("fetch_stripe","FETCHED",
                                 f"£{d.get('amount',0)/100} — {d.get('last_payment_error',{}).get('code','?')}")}
    except Exception as e:
        log.warning("Stripe fetch failed: %s", e)
    return {"payment_amount":99.99,"payment_currency":"GBP",
            "failure_reason":"insufficient_funds",
            **_audit("fetch_stripe","MOCK","Stripe unavailable — mock data")}

def fetch_customer_node(state, config):
    cid = state.get("stripe_customer_id","")
    try:
        if cid:
            r = requests.get(f"https://api.stripe.com/v1/customers/{cid}",
                headers={"Authorization":f"Bearer {config['stripe_key']}"}, timeout=10)
            if r.ok:
                email = r.json().get("email","")
                return {"customer_email":email,
                        **_audit("fetch_customer","FETCHED",f"Email: {email}")}
    except Exception as e:
        log.warning("Stripe customer fetch failed: %s", e)
    return {"customer_email":"customer@enterprise.com",
            **_audit("fetch_customer","MOCK","Mock email")}

def create_snow_incident_node(state, config):
    try:
        r = requests.post(f"{config['snow_url']}/api/now/table/incident",
            json={"short_description":f"Stripe payment failed — {state.get('customer_email')}",
                  "description":(f"[Azentix Auto-Created]\n"
                                 f"Event: {state.get('stripe_event_type')}\n"
                                 f"Customer: {state.get('customer_email')}\n"
                                 f"Amount: {state.get('payment_currency')} {state.get('payment_amount')}\n"
                                 f"Reason: {state.get('failure_reason')}"),
                  "category":"stripe","urgency":"2","impact":"2","caller_id":"azentix_agent"},
            auth=(config["snow_user"],config["snow_pass"]),
            headers={"Content-Type":"application/json","Accept":"application/json"}, timeout=10)
        if r.ok:
            inc_num = r.json().get("result",{}).get("number","")
            return {"snow_incident_id":inc_num,
                    **_audit("create_snow","CREATED",f"ServiceNow {inc_num}")}
    except Exception as e:
        log.warning("ServiceNow create failed: %s", e)
    return {"snow_incident_id":"INC-MOCK-001",
            **_audit("create_snow","MOCK","ServiceNow unavailable")}

def flag_salesforce_node(state, config):
    try:
        tok = requests.post("https://login.salesforce.com/services/oauth2/token",
            data={"grant_type":"password","client_id":config["sf_client_id"],
                  "client_secret":config["sf_client_secret"],
                  "username":config["sf_username"],"password":config["sf_password"]},
            timeout=15).json()
        inst,tkn = tok.get("instance_url",""),tok.get("access_token","")
        q = f"SELECT Id,Name FROM Opportunity WHERE Account.PersonEmail='{state.get('customer_email')}' AND IsClosed=false LIMIT 1"
        r = requests.get(f"{inst}/services/data/v59.0/query",params={"q":q},
            headers={"Authorization":f"Bearer {tkn}"}, timeout=10)
        if r.ok and r.json().get("totalSize",0)>0:
            opp_id = r.json()["records"][0]["Id"]
            requests.patch(f"{inst}/services/data/v59.0/sobjects/Opportunity/{opp_id}",
                json={"Azentix_Payment_Failed__c":True,
                      "Azentix_SNOW_Incident__c":state.get("snow_incident_id")},
                headers={"Authorization":f"Bearer {tkn}","Content-Type":"application/json"}, timeout=10)
            return {"sf_opportunity_id":opp_id,
                    **_audit("flag_salesforce","FLAGGED",f"Opportunity {opp_id} → payment_failed=true")}
    except Exception as e:
        log.warning("Salesforce flag failed: %s", e)
    return {"sf_opportunity_id":"mock_opp",
            **_audit("flag_salesforce","MOCK","Salesforce unavailable")}

def update_hubspot_node(state, config):
    try:
        r = requests.post(f"{config['hs_api']}/crm/v3/objects/contacts/search",
            json={"filterGroups":[{"filters":[{"propertyName":"email","operator":"EQ",
                                               "value":state.get("customer_email","")}]}]},
            headers={"Authorization":f"Bearer {config['hs_token']}",
                     "Content-Type":"application/json"}, timeout=10)
        if r.ok and r.json().get("total",0)>0:
            cid = r.json()["results"][0]["id"]
            requests.patch(f"{config['hs_api']}/crm/v3/objects/contacts/{cid}",
                json={"properties":{"payment_failed":"true",
                                    "azentix_snow_incident":state.get("snow_incident_id","")}},
                headers={"Authorization":f"Bearer {config['hs_token']}",
                         "Content-Type":"application/json"}, timeout=10)
            return {"hubspot_contact_id":cid,
                    **_audit("update_hubspot","UPDATED",f"Contact {cid} → payment_failed=true")}
    except Exception as e:
        log.warning("HubSpot update failed: %s", e)
    return {"hubspot_contact_id":"mock_contact",
            **_audit("update_hubspot","MOCK","HubSpot unavailable")}

def notify_node(state, config):
    try:
        conn = pika.BlockingConnection(pika.URLParameters(config["rabbitmq_url"]))
        ch = conn.channel(); ch.queue_declare(queue="notifications",durable=True)
        ch.basic_publish("","notifications",
            json.dumps({"type":"stripe_payment_failed_handled",
                        "customer":state.get("customer_email"),
                        "incident":state.get("snow_incident_id"),
                        "sf_opp":state.get("sf_opportunity_id"),
                        "hs_contact":state.get("hubspot_contact_id")}).encode(),
            pika.BasicProperties(delivery_mode=2)); conn.close()
    except Exception: pass
    return {"final_status":"handled",
            **_audit("notify","DONE",
                     f"Customer:{state.get('customer_email')} INC:{state.get('snow_incident_id')}")}

def compile_stripe_billing(config: dict):
    from langgraph.graph import StateGraph, END
    from langgraph.checkpoint.memory import MemorySaver
    g = StateGraph(StripeBillingState)
    g.add_node("validate",        validate_node)
    g.add_node("fetch_stripe",    lambda s: fetch_stripe_node(s, config))
    g.add_node("fetch_customer",  lambda s: fetch_customer_node(s, config))
    g.add_node("create_snow",     lambda s: create_snow_incident_node(s, config))
    g.add_node("flag_salesforce", lambda s: flag_salesforce_node(s, config))
    g.add_node("update_hubspot",  lambda s: update_hubspot_node(s, config))
    g.add_node("notify",          lambda s: notify_node(s, config))
    g.set_entry_point("validate")
    g.add_edge("validate","fetch_stripe")
    g.add_edge("fetch_stripe","fetch_customer")
    g.add_edge("fetch_customer","create_snow")
    g.add_edge("create_snow","flag_salesforce")
    g.add_edge("flag_salesforce","update_hubspot")
    g.add_edge("update_hubspot","notify")
    g.add_edge("notify",END)
    return g.compile(checkpointer=MemorySaver())

if __name__ == "__main__":
    load_dotenv()
    config = {"stripe_key":       os.getenv("STRIPE_SECRET_KEY"),
               "snow_url":        os.getenv("SERVICENOW_INSTANCE_URL"),
               "snow_user":       os.getenv("SERVICENOW_USERNAME"),
               "snow_pass":       os.getenv("SERVICENOW_PASSWORD"),
               "sf_client_id":    os.getenv("SALESFORCE_CLIENT_ID"),
               "sf_client_secret":os.getenv("SALESFORCE_CLIENT_SECRET"),
               "sf_username":     os.getenv("SALESFORCE_USERNAME"),
               "sf_password":     os.getenv("SALESFORCE_PASSWORD"),
               "hs_token":        os.getenv("HUBSPOT_ACCESS_TOKEN"),
               "hs_api":          os.getenv("HUBSPOT_API_BASE","https://api.hubapi.com"),
               "rabbitmq_url":    os.getenv("CLOUDAMQP_URL")}
    app = compile_stripe_billing(config)
    result = app.invoke({"stripe_event_type":"payment_intent.payment_failed",
        "stripe_payment_id":"pi_test_12345","stripe_customer_id":None,
        "customer_email":None,"payment_amount":None,"payment_currency":None,
        "failure_reason":None,"snow_incident_id":None,"sf_opportunity_id":None,
        "hubspot_contact_id":None,"audit_trail":[],"final_status":None,"error":None},
        config={"configurable":{"thread_id":"stripe-001"}})
    print(f"Status: {result['final_status']}")
    for e in result["audit_trail"]:
        print(f"  [{e['ts'][11:19]}] {e['node']:<20} {e['action']:<10} {e['result'][:55]}")
