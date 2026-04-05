#!/usr/bin/env python3
"""
test_connections.py — Verify all 8 services are reachable before coding.
Run: python3 scripts/test_connections.py
"""
import os, sys
from dotenv import load_dotenv
load_dotenv()

GREEN = "\033[92m"; RED = "\033[91m"; RESET = "\033[0m"; BOLD = "\033[1m"
results = {}

def check(name, fn):
    try:
        detail = fn()
        msg = f"  {GREEN}✅ {name}{RESET}"
        if detail: msg += f"  {detail}"
        print(msg); results[name] = "OK"
    except Exception as e:
        print(f"  {RED}❌ {name}: {e}{RESET}"); results[name] = f"FAIL: {e}"

print(f"\n{BOLD}Azentix — Connection Tests{RESET}\n")

print("1. Azure OpenAI")
def t_aoai():
    from openai import AzureOpenAI
    c = AzureOpenAI(azure_endpoint=os.getenv("AZURE_OPENAI_ENDPOINT",""),
                    api_key=os.getenv("AZURE_OPENAI_API_KEY",""), api_version="2024-08-01-preview")
    r = c.chat.completions.create(model=os.getenv("AZURE_OPENAI_DEPLOYMENT_NAME","gpt-4o-mini"),
        messages=[{"role":"user","content":"Say OK in one word"}], max_tokens=5)
    return f"→ '{r.choices[0].message.content.strip()}'"
check("Azure OpenAI gpt-4o-mini", t_aoai)

print("\n2. Supabase pgvector")
def t_supabase():
    import psycopg2
    conn = psycopg2.connect(os.getenv("SUPABASE_DB_CONNECTION",""), connect_timeout=10)
    cur  = conn.cursor()
    cur.execute("SELECT COUNT(*) FROM agent_memory")
    n = cur.fetchone()[0]; conn.close()
    return f"→ {n} rows in agent_memory"
check("Supabase PostgreSQL", t_supabase)

print("\n3. CloudAMQP RabbitMQ")
def t_rabbitmq():
    import pika
    conn = pika.BlockingConnection(pika.URLParameters(os.getenv("CLOUDAMQP_URL","")))
    ch   = conn.channel()
    ch.queue_declare(queue="azentix-ping-test", durable=False, auto_delete=True)
    conn.close()
    return "→ connection + queue declare OK"
check("CloudAMQP RabbitMQ", t_rabbitmq)

print("\n4. SAP Sandbox")
def t_sap():
    import requests
    r = requests.get("https://sandbox.api.sap.com/s4hanacloud/sap/opu/odata/sap/API_PRODUCT_SRV/A_Product?$top=1",
        headers={"APIKey":os.getenv("SAP_API_KEY",""),"Accept":"application/json"}, timeout=10)
    assert r.status_code in (200,401), f"HTTP {r.status_code}"
    return f"→ HTTP {r.status_code}"
check("SAP OData sandbox", t_sap)

print("\n5. Salesforce")
def t_salesforce():
    import requests
    r = requests.post("https://login.salesforce.com/services/oauth2/token",
        data={"grant_type":"password","client_id":os.getenv("SALESFORCE_CLIENT_ID",""),
              "client_secret":os.getenv("SALESFORCE_CLIENT_SECRET",""),
              "username":os.getenv("SALESFORCE_USERNAME",""),
              "password":os.getenv("SALESFORCE_PASSWORD","")}, timeout=15)
    assert r.status_code == 200, f"Auth failed: {r.text[:100]}"
    return f"→ {r.json().get('instance_url','?')}"
check("Salesforce OAuth", t_salesforce)

print("\n6. ServiceNow PDI")
def t_snow():
    import requests
    r = requests.get(os.getenv("SERVICENOW_INSTANCE_URL","") + "/api/now/table/incident?sysparm_limit=1",
        auth=(os.getenv("SERVICENOW_USERNAME",""),os.getenv("SERVICENOW_PASSWORD","")),
        headers={"Accept":"application/json"}, timeout=10)
    assert r.status_code == 200, f"HTTP {r.status_code}"
    return f"→ {r.json().get('result',[{}])[0].get('number','?')}"
check("ServiceNow Table API", t_snow)

print("\n7. HubSpot")
def t_hubspot():
    import requests
    r = requests.get("https://api.hubapi.com/crm/v3/objects/contacts?limit=1",
        headers={"Authorization":f"Bearer {os.getenv('HUBSPOT_ACCESS_TOKEN','')}"},timeout=10)
    assert r.status_code in (200,401), f"HTTP {r.status_code}"
    return f"→ HTTP {r.status_code}"
check("HubSpot CRM API", t_hubspot)

print("\n8. Stripe")
def t_stripe():
    import requests
    r = requests.get("https://api.stripe.com/v1/customers?limit=1",
        headers={"Authorization":f"Bearer {os.getenv('STRIPE_SECRET_KEY','')}"},timeout=10)
    assert r.status_code in (200,401), f"HTTP {r.status_code}"
    return f"→ HTTP {r.status_code}"
check("Stripe API (test mode)", t_stripe)

# Summary
print(f"\n{'='*50}")
ok    = sum(1 for v in results.values() if v=="OK")
total = len(results)
if ok == total:
    print(f"{GREEN}{BOLD}✅ All {total} connections OK — ready to run!{RESET}")
    sys.exit(0)
else:
    print(f"{RED}❌ {total-ok}/{total} failed:{RESET}")
    for k,v in results.items():
        if v != "OK": print(f"   {k}: {v}")
    sys.exit(1)
