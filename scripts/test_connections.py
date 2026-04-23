#!/usr/bin/env python3
"""
test_connections.py — Verify all 8 services are reachable before coding.
Run: python3 scripts/test_connections.py
"""
import os, sys
from importlib import import_module
from urllib.parse import urlparse
from dotenv import load_dotenv
load_dotenv()

GREEN = "\033[92m"; RED = "\033[91m"; RESET = "\033[0m"; BOLD = "\033[1m"
YELLOW = "\033[93m"
results = {}


class SkipCheck(Exception):
    pass


def require_env(*keys):
    missing = [key for key in keys if not os.getenv(key)]
    if missing:
        raise SkipCheck(f"missing env: {', '.join(missing)}")


def import_or_skip(module_name):
    try:
        return import_module(module_name)
    except ImportError:
        raise SkipCheck(f"missing dependency: {module_name}")


def require_url(name, value):
    parsed = urlparse(value)
    if parsed.scheme not in {"http", "https"} or not parsed.netloc:
        raise ValueError(f"{name} must be a valid http/https URL")
    return value.rstrip("/")

def check(name, fn):
    try:
        detail = fn()
        msg = f"  {GREEN}✅ {name}{RESET}"
        if detail: msg += f"  {detail}"
        print(msg); results[name] = "OK"
    except SkipCheck as e:
        print(f"  {YELLOW}⚠ {name}: {e}{RESET}"); results[name] = f"SKIP: {e}"
    except Exception as e:
        print(f"  {RED}❌ {name}: {e}{RESET}"); results[name] = f"FAIL: {e}"

print(f"\n{BOLD}Azentix — Connection Tests{RESET}\n")

MODEL_PROVIDER = (os.getenv("MODEL_PROVIDER") or "ollama").strip().lower()


def is_azure_mode():
    return MODEL_PROVIDER == "azure"

print("1. Azure OpenAI")
def t_aoai():
    if not is_azure_mode():
        raise SkipCheck("MODEL_PROVIDER is not azure")
    require_env("AZURE_OPENAI_ENDPOINT", "AZURE_OPENAI_API_KEY")
    from openai import AzureOpenAI
    endpoint = require_url("AZURE_OPENAI_ENDPOINT", os.getenv("AZURE_OPENAI_ENDPOINT", ""))
    c = AzureOpenAI(azure_endpoint=endpoint,
                    api_key=os.getenv("AZURE_OPENAI_API_KEY",""), api_version="2024-08-01-preview")
    r = c.chat.completions.create(model=os.getenv("AZURE_OPENAI_DEPLOYMENT_NAME","gpt-4o-mini"),
        messages=[{"role":"user","content":"Say OK in one word"}], max_tokens=5)
    return f"→ '{r.choices[0].message.content.strip()}'"
check("Azure OpenAI gpt-4o-mini", t_aoai)

print("\n1b. Ollama")
def t_ollama():
    if is_azure_mode():
        raise SkipCheck("MODEL_PROVIDER is azure")
    import requests
    from openai import OpenAI

    base_url = os.getenv("OLLAMA_BASE_URL", "http://localhost:11434/v1")
    api_key = os.getenv("OLLAMA_API_KEY", "ollama")
    chat_model = os.getenv("OLLAMA_CHAT_MODEL", "llama3.2:1b")

    # Verify model is pulled in local Ollama daemon.
    tags = requests.get(base_url.replace("/v1", "/api/tags"), timeout=10)
    if not tags.ok:
        raise RuntimeError(f"Ollama tags endpoint failed: HTTP {tags.status_code}")
    names = {m.get("name") for m in tags.json().get("models", [])}
    if chat_model not in names:
        raise RuntimeError(f"Model '{chat_model}' not found. Pull it with: ollama pull {chat_model}")

    client = OpenAI(base_url=base_url, api_key=api_key)
    resp = client.chat.completions.create(
        model=chat_model,
        messages=[{"role": "user", "content": "Reply exactly with OK"}],
        max_tokens=5,
    )
    return f"→ '{resp.choices[0].message.content.strip()}' using {chat_model}"
check("Ollama local model", t_ollama)

print("\n2. Supabase pgvector")
def t_supabase():
    require_env("SUPABASE_DB_CONNECTION")
    psycopg2 = import_or_skip("psycopg2")
    conn = psycopg2.connect(os.getenv("SUPABASE_DB_CONNECTION",""), connect_timeout=10)
    cur  = conn.cursor()
    cur.execute("SELECT COUNT(*) FROM agent_memory")
    n = cur.fetchone()[0]; conn.close()
    return f"→ {n} rows in agent_memory"
check("Supabase PostgreSQL", t_supabase)

print("\n3. CloudAMQP RabbitMQ")
def t_rabbitmq():
    require_env("CLOUDAMQP_URL")
    pika = import_or_skip("pika")
    conn = pika.BlockingConnection(pika.URLParameters(os.getenv("CLOUDAMQP_URL","")))
    ch   = conn.channel()
    ch.queue_declare(queue="azentix-ping-test", durable=False, auto_delete=True)
    conn.close()
    return "→ connection + queue declare OK"
check("CloudAMQP RabbitMQ", t_rabbitmq)

print("\n4. SAP Sandbox")
def t_sap():
    import requests
    require_env("SAP_API_KEY")
    r = requests.get("https://sandbox.api.sap.com/s4hanacloud/sap/opu/odata/sap/API_PRODUCT_SRV/A_Product?$top=1",
        headers={"APIKey":os.getenv("SAP_API_KEY",""),"Accept":"application/json"}, timeout=10)
    assert r.status_code in (200,401), f"HTTP {r.status_code}"
    return f"→ HTTP {r.status_code}"
check("SAP OData sandbox", t_sap)

print("\n5. Salesforce")
def t_salesforce():
    import requests
    require_env("SALESFORCE_CLIENT_ID", "SALESFORCE_CLIENT_SECRET", "SALESFORCE_USERNAME", "SALESFORCE_PASSWORD")
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
    require_env("SERVICENOW_INSTANCE_URL", "SERVICENOW_USERNAME", "SERVICENOW_PASSWORD")
    base_url = require_url("SERVICENOW_INSTANCE_URL", os.getenv("SERVICENOW_INSTANCE_URL", ""))
    r = requests.get(base_url + "/api/now/table/incident?sysparm_limit=1",
        auth=(os.getenv("SERVICENOW_USERNAME",""),os.getenv("SERVICENOW_PASSWORD","")),
        headers={"Accept":"application/json"}, timeout=10)
    assert r.status_code == 200, f"HTTP {r.status_code}"
    return f"→ {r.json().get('result',[{}])[0].get('number','?')}"
check("ServiceNow Table API", t_snow)

print("\n7. HubSpot")
def t_hubspot():
    import requests
    require_env("HUBSPOT_ACCESS_TOKEN")
    r = requests.get("https://api.hubapi.com/crm/v3/objects/contacts?limit=1",
        headers={"Authorization":f"Bearer {os.getenv('HUBSPOT_ACCESS_TOKEN','')}"},timeout=10)
    assert r.status_code in (200,401), f"HTTP {r.status_code}"
    return f"→ HTTP {r.status_code}"
check("HubSpot CRM API", t_hubspot)

print("\n8. Stripe")
def t_stripe():
    import requests
    require_env("STRIPE_SECRET_KEY")
    r = requests.get("https://api.stripe.com/v1/customers?limit=1",
        headers={"Authorization":f"Bearer {os.getenv('STRIPE_SECRET_KEY','')}"},timeout=10)
    assert r.status_code in (200,401), f"HTTP {r.status_code}"
    return f"→ HTTP {r.status_code}"
check("Stripe API (test mode)", t_stripe)

# Summary
print(f"\n{'='*50}")
ok    = sum(1 for v in results.values() if v=="OK")
skipped = sum(1 for v in results.values() if v.startswith("SKIP:"))
total = len(results)
failed = total - ok - skipped
if failed == 0:
    print(f"{GREEN}{BOLD}✅ Connectivity checks completed with {ok} OK and {skipped} skipped.{RESET}")
    sys.exit(0)
else:
    print(f"{RED}❌ {failed}/{total} failed:{RESET}")
    for k,v in results.items():
        if v.startswith("FAIL:"): print(f"   {k}: {v}")
    sys.exit(1)
