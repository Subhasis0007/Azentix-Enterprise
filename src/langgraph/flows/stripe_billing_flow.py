"""
Stripe Failed Payment Flow
============================
Triggered by: Stripe webhook -> n8n -> this flow
Flow: validate -> fetch_stripe -> fetch_customer -> create_snow_incident -> flag_salesforce -> update_hubspot -> notify
"""
import os, json, logging, operator
from typing import TypedDict, Annotated, Optional, List
from datetime import datetime
from langgraph.graph import StateGraph, END
from langgraph.checkpoint.memory import MemorySaver
import requests, pika
from dotenv import load_dotenv

logger = logging.getLogger(__name__)


class StripeBillingState(TypedDict):
    stripe_event_type: str
    stripe_payment_id: Optional[str]
    stripe_customer_id: Optional[str]
    customer_email: Optional[str]
    payment_amount: Optional[float]
    payment_currency: Optional[str]
    failure_reason: Optional[str]
    snow_incident_id: Optional[str]
    sf_opportunity_id: Optional[str]
    hubspot_contact_id: Optional[str]
    audit_trail: Annotated[List[dict], operator.add]
    final_status: Optional[str]
    error: Optional[str]


def _audit(node, action, result):
    return {"audit_trail": [{"ts": datetime.utcnow().isoformat(),
                              "node": node, "action": action, "result": result}]}


def validate_node(state):
    if not state.get("stripe_event_type"):
        return {"final_status": "validation_failed", "error": "Missing stripe_event_type",
                **_audit("validate", "failed", "Missing stripe_event_type")}
    return _audit("validate", "passed", f"Event: {state['stripe_event_type']}")


def fetch_stripe_node(state, config):
    payment_id = state.get("stripe_payment_id", "")
    try:
        if payment_id:
            resp = requests.get(f"https://api.stripe.com/v1/payment_intents/{payment_id}",
                                headers={"Authorization": f"Bearer {config['stripe_key']}"}, timeout=10)
            if resp.ok:
                data = resp.json()
                return {
                    "stripe_customer_id": data.get("customer"),
                    "payment_amount": data.get("amount", 0) / 100,
                    "payment_currency": data.get("currency", "gbp").upper(),
                    "failure_reason": data.get("last_payment_error", {}).get("message", "Unknown"),
                    **_audit("fetch_stripe", "fetched", f"Amount:{data.get('amount',0)/100} Reason:{data.get('last_payment_error',{}).get('code','?')}")
                }
    except Exception as e:
        logger.warning(f"Stripe fetch failed: {e}")
    return {"payment_amount": 99.99, "payment_currency": "GBP",
            "failure_reason": "insufficient_funds",
            **_audit("fetch_stripe", "mock", "Stripe unavailable, mock data")}


def fetch_customer_node(state, config):
    cust_id = state.get("stripe_customer_id", "")
    try:
        if cust_id:
            resp = requests.get(f"https://api.stripe.com/v1/customers/{cust_id}",
                                headers={"Authorization": f"Bearer {config['stripe_key']}"}, timeout=10)
            if resp.ok:
                data = resp.json()
                return {"customer_email": data.get("email"),
                        **_audit("fetch_customer", "fetched", f"Email:{data.get('email','?')}")}
    except Exception as e:
        logger.warning(f"Customer fetch failed: {e}")
    return {"customer_email": "customer@example.com",
            **_audit("fetch_customer", "mock", "Mock customer email")}


def create_snow_incident_node(state, config):
    try:
        payload = {
            "short_description": f"Stripe payment failed - {state.get('customer_email')}",
            "description": (f"[Azentix Auto-Created]\nStripe event: {state.get('stripe_event_type')}\n"
                            f"Customer: {state.get('customer_email')}\n"
                            f"Amount: {state.get('payment_currency')} {state.get('payment_amount')}\n"
                            f"Reason: {state.get('failure_reason')}"),
            "category": "stripe", "urgency": "2", "impact": "2",
            "caller_id": "azentix_agent"
        }
        resp = requests.post(config["snow_url"] + "/api/now/table/incident",
                             json=payload, auth=(config["snow_user"], config["snow_pass"]),
                             headers={"Content-Type": "application/json", "Accept": "application/json"}, timeout=10)
        if resp.ok:
            inc = resp.json().get("result", {})
            return {"snow_incident_id": inc.get("number"),
                    **_audit("create_snow", "created", f"Incident:{inc.get('number')}")}
    except Exception as e:
        logger.warning(f"ServiceNow incident creation failed: {e}")
    return {"snow_incident_id": "INC-MOCK",
            **_audit("create_snow", "mock", "ServiceNow unavailable, mock INC-MOCK")}


def flag_salesforce_node(state, config):
    try:
        tok = requests.post("https://login.salesforce.com/services/oauth2/token",
                            data={"grant_type": "password",
                                  "client_id": config["sf_client_id"],
                                  "client_secret": config["sf_client_secret"],
                                  "username": config["sf_username"],
                                  "password": config["sf_password"]}).json()
        inst = tok.get("instance_url", ""); tkn = tok.get("access_token", "")
        q = f"SELECT Id,Name FROM Opportunity WHERE Account.PersonEmail='{state.get('customer_email')}' AND IsClosed=false LIMIT 1"
        resp = requests.get(inst + "/services/data/v59.0/query?q=" + requests.utils.quote(q),
                            headers={"Authorization": f"Bearer {tkn}"}, timeout=10)
        if resp.ok and resp.json().get("totalSize", 0) > 0:
            opp = resp.json()["records"][0]
            requests.patch(inst + f"/services/data/v59.0/sobjects/Opportunity/{opp['Id']}",
                           json={"Azentix_Payment_Failed__c": True,
                                 "Azentix_ServiceNow_Incident__c": state.get("snow_incident_id")},
                           headers={"Authorization": f"Bearer {tkn}", "Content-Type": "application/json"}, timeout=10)
            return {"sf_opportunity_id": opp["Id"],
                    **_audit("flag_salesforce", "flagged", f"Opportunity:{opp['Id']}")}
    except Exception as e:
        logger.warning(f"Salesforce flag failed: {e}")
    return {"sf_opportunity_id": "mock_opp",
            **_audit("flag_salesforce", "mock", "Salesforce unavailable")}


def update_hubspot_node(state, config):
    try:
        payload = json.dumps({"filterGroups": [{"filters": [
            {"propertyName": "email", "operator": "EQ", "value": state.get("customer_email")}
        ]}]})
        resp = requests.post(config["hs_api"] + "/crm/v3/objects/contacts/search",
                             data=payload, headers={"Authorization": f"Bearer {config['hs_token']}",
                                                    "Content-Type": "application/json"}, timeout=10)
        if resp.ok and resp.json().get("total", 0) > 0:
            contact_id = resp.json()["results"][0]["id"]
            requests.patch(config["hs_api"] + f"/crm/v3/objects/contacts/{contact_id}",
                           json={"properties": {"payment_failed": "true",
                                                "snow_incident": state.get("snow_incident_id")}},
                           headers={"Authorization": f"Bearer {config['hs_token']}",
                                    "Content-Type": "application/json"}, timeout=10)
            return {"hubspot_contact_id": contact_id,
                    **_audit("update_hubspot", "updated", f"Contact:{contact_id}")}
    except Exception as e:
        logger.warning(f"HubSpot update failed: {e}")
    return {"hubspot_contact_id": "mock_contact",
            **_audit("update_hubspot", "mock", "HubSpot unavailable")}


def notify_node(state, config):
    try:
        conn = pika.BlockingConnection(pika.URLParameters(config["rabbitmq_url"]))
        ch = conn.channel()
        ch.queue_declare(queue="notifications", durable=True)
        ch.basic_publish(exchange="", routing_key="notifications",
                         body=json.dumps({"type": "stripe_payment_failed_handled",
                                          "customer": state.get("customer_email"),
                                          "incident": state.get("snow_incident_id"),
                                          "sfOpportunity": state.get("sf_opportunity_id")}),
                         properties=pika.BasicProperties(delivery_mode=2))
        conn.close()
    except Exception as e:
        logger.warning(f"RabbitMQ notify failed: {e}")
    return {"final_status": "handled",
            **_audit("notify", "done",
                     f"Customer:{state.get('customer_email')} INC:{state.get('snow_incident_id')}")}


def compile_stripe_billing(config: dict):
    g = StateGraph(StripeBillingState)
    g.add_node("validate",         validate_node)
    g.add_node("fetch_stripe",     lambda s: fetch_stripe_node(s, config))
    g.add_node("fetch_customer",   lambda s: fetch_customer_node(s, config))
    g.add_node("create_snow",      lambda s: create_snow_incident_node(s, config))
    g.add_node("flag_salesforce",  lambda s: flag_salesforce_node(s, config))
    g.add_node("update_hubspot",   lambda s: update_hubspot_node(s, config))
    g.add_node("notify",           lambda s: notify_node(s, config))
    g.set_entry_point("validate")
    g.add_edge("validate",        "fetch_stripe")
    g.add_edge("fetch_stripe",    "fetch_customer")
    g.add_edge("fetch_customer",  "create_snow")
    g.add_edge("create_snow",     "flag_salesforce")
    g.add_edge("flag_salesforce", "update_hubspot")
    g.add_edge("update_hubspot",  "notify")
    g.add_edge("notify", END)
    return g.compile(checkpointer=MemorySaver())


if __name__ == "__main__":
    load_dotenv()
    config = {
        "azure_endpoint":  os.getenv("AZURE_OPENAI_ENDPOINT"),
        "azure_key":       os.getenv("AZURE_OPENAI_API_KEY"),
        "stripe_key":      os.getenv("STRIPE_SECRET_KEY"),
        "snow_url":        os.getenv("SERVICENOW_INSTANCE_URL"),
        "snow_user":       os.getenv("SERVICENOW_USERNAME"),
        "snow_pass":       os.getenv("SERVICENOW_PASSWORD"),
        "sf_client_id":    os.getenv("SALESFORCE_CLIENT_ID"),
        "sf_client_secret":os.getenv("SALESFORCE_CLIENT_SECRET"),
        "sf_username":     os.getenv("SALESFORCE_USERNAME"),
        "sf_password":     os.getenv("SALESFORCE_PASSWORD"),
        "hs_token":        os.getenv("HUBSPOT_ACCESS_TOKEN"),
        "hs_api":          os.getenv("HUBSPOT_API_BASE", "https://api.hubapi.com"),
        "rabbitmq_url":    os.getenv("CLOUDAMQP_URL"),
    }
    app = compile_stripe_billing(config)
    result = app.invoke({
        "stripe_event_type": "payment_intent.payment_failed",
        "stripe_payment_id": "pi_test_12345", "stripe_customer_id": None,
        "customer_email": None, "payment_amount": None, "payment_currency": None,
        "failure_reason": None, "snow_incident_id": None, "sf_opportunity_id": None,
        "hubspot_contact_id": None, "audit_trail": [], "final_status": None, "error": None
    }, config={"configurable": {"thread_id": "stripe-001"}})
    print(f"Status:   {result['final_status']}")
    print(f"Incident: {result.get('snow_incident_id')}")
    print(f"Customer: {result.get('customer_email')}")
    print("\nAudit Trail:")
    for e in result["audit_trail"]:
        print(f"  [{e['ts'][:19]}] {e['node']:<20} -> {e['result'][:70]}")
