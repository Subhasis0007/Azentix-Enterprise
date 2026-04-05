"""
test_connections.py — Run this before Month 3 code to verify all 5 systems + infra.
Usage: python scripts/test_connections.py
"""
import os, sys, json, time
from dotenv import load_dotenv
load_dotenv()

results = {}

def check(name, fn):
    try:
        fn()
        print(f"  ✅ {name}")
        results[name] = "OK"
    except Exception as e:
        print(f"  ❌ {name}: {e}")
        results[name] = f"FAIL: {e}"

print("\n🔍 Azentix Connection Test\n")

print("1. SAP Sandbox")
def test_sap():
    import requests
    r = requests.get("https://sandbox.api.sap.com/s4hanacloud/sap/opu/odata/sap/API_PRODUCT_SRV/A_Product?$top=1",
        headers={"APIKey": os.getenv("SAP_API_KEY", ""), "Accept": "application/json"}, timeout=10)
    assert r.status_code in (200, 401), f"HTTP {r.status_code}"
check("SAP OData endpoint", test_sap)

print("\n2. Supabase pgvector")
def test_supabase():
    import psycopg2
    conn = psycopg2.connect(os.getenv("SUPABASE_DB_CONNECTION", ""), connect_timeout=10)
    cur = conn.cursor()
    cur.execute("SELECT COUNT(*) FROM agent_memory")
    count = cur.fetchone()[0]
    conn.close()
    print(f"     → {count} rows in agent_memory")
check("Supabase PostgreSQL", test_supabase)

print("\n3. CloudAMQP RabbitMQ")
def test_rabbitmq():
    import pika
    conn = pika.BlockingConnection(pika.URLParameters(os.getenv("CLOUDAMQP_URL", "")))
    ch = conn.channel()
    ch.queue_declare(queue="azentix-test-ping", durable=False, auto_delete=True)
    conn.close()
check("CloudAMQP RabbitMQ", test_rabbitmq)

print("\n4. Salesforce")
def test_salesforce():
    import requests
    r = requests.post("https://login.salesforce.com/services/oauth2/token",
        data={"grant_type": "password", "client_id": os.getenv("SALESFORCE_CLIENT_ID", ""),
              "client_secret": os.getenv("SALESFORCE_CLIENT_SECRET", ""),
              "username": os.getenv("SALESFORCE_USERNAME", ""),
              "password": os.getenv("SALESFORCE_PASSWORD", "")}, timeout=15)
    assert r.status_code == 200, f"Auth failed: {r.text[:100]}"
    print(f"     → Instance: {r.json().get('instance_url', '?')}")
check("Salesforce OAuth", test_salesforce)

print("\n5. ServiceNow PDI")
def test_servicenow():
    import requests
    r = requests.get(os.getenv("SERVICENOW_INSTANCE_URL", "") + "/api/now/table/incident?sysparm_limit=1",
        auth=(os.getenv("SERVICENOW_USERNAME", ""), os.getenv("SERVICENOW_PASSWORD", "")),
        headers={"Accept": "application/json"}, timeout=10)
    assert r.status_code == 200, f"HTTP {r.status_code}"
check("ServiceNow Table API", test_servicenow)

print("\n6. HubSpot")
def test_hubspot():
    import requests
    r = requests.get("https://api.hubapi.com/crm/v3/objects/contacts?limit=1",
        headers={"Authorization": f"Bearer {os.getenv('HUBSPOT_ACCESS_TOKEN', '')}"}, timeout=10)
    assert r.status_code in (200, 401), f"HTTP {r.status_code}"
check("HubSpot CRM API", test_hubspot)

print("\n7. Stripe")
def test_stripe():
    import requests
    r = requests.get("https://api.stripe.com/v1/customers?limit=1",
        headers={"Authorization": f"Bearer {os.getenv('STRIPE_SECRET_KEY', '')}"}, timeout=10)
    assert r.status_code in (200, 401), f"HTTP {r.status_code}"
check("Stripe API", test_stripe)

print("\n8. Azure OpenAI")
def test_aoai():
    from openai import AzureOpenAI
    client = AzureOpenAI(azure_endpoint=os.getenv("AZURE_OPENAI_ENDPOINT", ""),
                          api_key=os.getenv("AZURE_OPENAI_API_KEY", ""), api_version="2024-08-01-preview")
    r = client.chat.completions.create(model=os.getenv("AZURE_OPENAI_DEPLOYMENT_NAME", "gpt-4o-mini"),
        messages=[{"role": "user", "content": "Say OK"}], max_tokens=5)
    assert r.choices[0].message.content
check("Azure OpenAI gpt-4o-mini", test_aoai)

print("\n" + "="*50)
ok = sum(1 for v in results.values() if v == "OK")
total = len(results)
print(f"Result: {ok}/{total} connections OK")
if ok < total:
    print("\nFailed:")
    for k, v in results.items():
        if v != "OK": print(f"  - {k}: {v}")
    sys.exit(1)
else:
    print("All systems connected. Ready for Month 3 code.")
