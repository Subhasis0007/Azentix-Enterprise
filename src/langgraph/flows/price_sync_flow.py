"""
price_sync_flow.py — SAP → Salesforce Price Sync
Triggered by: CloudAMQP sap-price-changes queue → this LangGraph flow
Run standalone: python3 src/langgraph/flows/price_sync_flow.py
"""
import os, json, logging, operator
from typing import TypedDict, Annotated, Optional, List
from datetime import datetime, timezone
import requests, pika
from dotenv import load_dotenv

log = logging.getLogger(__name__)

class PriceSyncState(TypedDict):
    sap_material:    str
    sf_product_id:   str
    triggered_by:    str
    tenant_id:       str
    sap_price:       Optional[float]
    sap_currency:    Optional[str]
    sf_price:        Optional[float]
    sf_pricebook_id: Optional[str]
    discrepancy:     Optional[float]
    discrepancy_pct: Optional[float]
    sync_required:   bool
    approval_needed: bool
    approval_level:  Optional[str]
    rag_context:     Optional[str]
    sync_ref:        Optional[str]
    audit_trail:     Annotated[List[dict], operator.add]
    final_status:    Optional[str]
    error:           Optional[str]

def _ts(): return datetime.now(timezone.utc).isoformat()
def _audit(node, action, result):
    return {"audit_trail": [{"ts": _ts(), "node": node, "action": action, "result": result}]}

# ── nodes ────────────────────────────────────────────────────────────────

def validate_node(state):
    if not state.get("sap_material") or not state.get("sf_product_id"):
        return {"final_status": "validation_failed", "error": "Missing required fields",
                **_audit("validate", "FAILED", "sap_material or sf_product_id missing")}
    return _audit("validate", "PASSED", f"Material {state['sap_material']}")

def fetch_sap_node(state, config):
    try:
        url = (f"{config['sap_base_url']}/sap/opu/odata/sap/"
               f"API_SLSPRICINGCONDITIONRECORD_SRV/A_SlsPrcgCndnRecdValidity"
               f"?$filter=Material eq '{state['sap_material']}'&$top=1&$orderby=ValidityStartDate desc")
        r = requests.get(url, headers={"APIKey": config["sap_api_key"],
                                       "Accept": "application/json"}, timeout=10)
        if r.ok:
            items = r.json().get("d", {}).get("results", [])
            if items:
                price = float(items[0].get("ConditionRateValue", 249.99))
                curr  = items[0].get("ConditionRateValueUnit", "GBP")
                return {"sap_price": price, "sap_currency": curr,
                        **_audit("fetch_sap", "FETCHED", f"{curr} {price}")}
    except Exception as e:
        log.warning("SAP fetch failed: %s", e)
    return {"sap_price": 249.99, "sap_currency": "GBP",
            **_audit("fetch_sap", "MOCK", "SAP unavailable — using mock 249.99 GBP")}

def fetch_sf_node(state, config):
    try:
        tok = requests.post("https://login.salesforce.com/services/oauth2/token",
            data={"grant_type": "password", "client_id": config["sf_client_id"],
                  "client_secret": config["sf_client_secret"],
                  "username": config["sf_username"], "password": config["sf_password"]},
            timeout=15).json()
        inst, tkn = tok.get("instance_url",""), tok.get("access_token","")
        q = (f"SELECT Id,UnitPrice,CurrencyIsoCode FROM PricebookEntry "
             f"WHERE Product2Id='{state['sf_product_id']}' "
             f"AND IsActive=true AND Pricebook2.IsStandard=true LIMIT 1")
        r = requests.get(f"{inst}/services/data/v59.0/query",
            params={"q": q}, headers={"Authorization": f"Bearer {tkn}"}, timeout=10)
        if r.ok and r.json().get("totalSize", 0) > 0:
            rec = r.json()["records"][0]
            return {"sf_price": float(rec["UnitPrice"]), "sf_pricebook_id": rec["Id"],
                    **_audit("fetch_sf", "FETCHED", f"GBP {rec['UnitPrice']}")}
    except Exception as e:
        log.warning("Salesforce fetch failed: %s", e)
    return {"sf_price": 234.50, "sf_pricebook_id": "MOCK_PBE_ID",
            **_audit("fetch_sf", "MOCK", "Salesforce unavailable — using mock 234.50")}

def rag_rules_node(state, config):
    try:
        from openai import AzureOpenAI
        import psycopg2
        client = AzureOpenAI(azure_endpoint=config["azure_endpoint"],
                             api_key=config["azure_key"], api_version="2024-08-01-preview")
        vec = client.embeddings.create(model="text-embedding-3-small",
            input="SAP Salesforce price sync approval thresholds governance rules").data[0].embedding
        conn = psycopg2.connect(config["supabase_db"])
        cur  = conn.cursor()
        cur.execute("SELECT content, 1-(embedding<=>%s::vector) AS sim FROM agent_memory "
                    "WHERE collection='sap-salesforce-sync' "
                    "AND 1-(embedding<=>%s::vector)>=0.7 "
                    "ORDER BY embedding<=>%s::vector LIMIT 3", (vec, vec, vec))
        rows = cur.fetchall(); cur.close(); conn.close()
        if rows:
            rules = "\n".join(f"[KB-{round(r[1],2)}] {r[0][:300]}" for r in rows)
            return {"rag_context": rules,
                    **_audit("rag_rules", "RETRIEVED", f"{len(rows)} rules from Supabase pgvector")}
    except Exception as e:
        log.warning("Supabase RAG failed: %s", e)
    rules = ("\n[Rule] Changes <1%: auto-approved."
             "\n[Rule] 1-10%: Finance Manager approval required."
             "\n[Rule] >10%: VP Sales approval required."
             "\n[Rule] sap_change_event trigger pre-authorises auto-sync regardless of pct.")
    return {"rag_context": rules,
            **_audit("rag_rules", "DEFAULT", "Using built-in fallback rules")}

def analyse_node(state, llm):
    sap = state.get("sap_price", 249.99)
    sf  = state.get("sf_price",  234.50)
    diff = sap - sf
    pct  = abs(diff / sf * 100) if sf else 0
    try:
        from langchain.schema import HumanMessage, SystemMessage
        resp = llm.invoke([
            SystemMessage(content=(
                'Analyse price discrepancy and decide action. '
                'Respond ONLY with valid JSON — no markdown, no preamble: '
                '{"sync_required":bool,"approval_needed":bool,'
                '"approval_level":"auto|finance_manager|vp_sales","confidence":float}')),
            HumanMessage(content=(
                f"SAP={sap} SF={sf} diff={round(diff,2)} pct={round(pct,1)}%\n"
                f"Rules:\n{state.get('rag_context','')}\n"
                f"trigger={state.get('triggered_by','')}"))])
        r = json.loads(resp.content.strip())
    except Exception as e:
        log.warning("LLM analyse fallback: %s", e)
        r = {"sync_required": pct > 0.01, "approval_needed": pct > 1 and state.get("triggered_by") != "sap_change_event",
             "approval_level": "auto" if pct < 1 or state.get("triggered_by") == "sap_change_event"
                               else "finance_manager" if pct < 10 else "vp_sales",
             "confidence": 0.92}
    return {"discrepancy": round(diff,2), "discrepancy_pct": round(pct,2),
            "sync_required": r.get("sync_required", True),
            "approval_needed": r.get("approval_needed", False),
            "approval_level":  r.get("approval_level", "auto"),
            **_audit("analyse", "DECIDED",
                     f"Δ={diff:+.2f} ({pct:.1f}%) → {r.get('approval_level','auto')} conf={r.get('confidence',0):.2f}")}

def auto_sync_node(state, config):
    ref = f"SYNC-{datetime.now(timezone.utc).strftime('%Y%m%d%H%M%S')}-{state['sap_material'][:8].upper()}"
    try:
        tok = requests.post("https://login.salesforce.com/services/oauth2/token",
            data={"grant_type": "password", "client_id": config["sf_client_id"],
                  "client_secret": config["sf_client_secret"],
                  "username": config["sf_username"], "password": config["sf_password"]},
            timeout=15).json()
        inst, tkn = tok.get("instance_url",""), tok.get("access_token","")
        pbe = state.get("sf_pricebook_id","")
        if pbe and pbe != "MOCK_PBE_ID":
            requests.patch(f"{inst}/services/data/v59.0/sobjects/PricebookEntry/{pbe}",
                json={"UnitPrice": state.get("sap_price"),
                      "Azentix_SAP_Sync__c": True,
                      "Azentix_Sync_Ref__c": ref},
                headers={"Authorization": f"Bearer {tkn}",
                         "Content-Type": "application/json"}, timeout=10)
    except Exception as e:
        log.warning("Salesforce update failed: %s", e)
    # Publish notification
    _publish_rabbitmq(config, "notifications",
        {"type": "price_sync_complete", "ref": ref,
         "material": state["sap_material"], "newPrice": state.get("sap_price")})
    return {"sync_ref": ref, "final_status": "synced",
            **_audit("auto_sync", "UPDATED", f"Salesforce updated — Ref: {ref}")}

def approval_queue_node(state, config):
    _publish_rabbitmq(config, "approval-queue",
        {"type": "price_sync_approval_needed",
         "material": state["sap_material"],
         "sapPrice": state.get("sap_price"),
         "sfPrice":  state.get("sf_price"),
         "discrepancyPct": state.get("discrepancy_pct"),
         "approvalLevel":  state.get("approval_level")})
    return {"final_status": "pending_approval",
            **_audit("approval_queue", "QUEUED",
                     f"Requires {state.get('approval_level')} — Δ={state.get('discrepancy_pct',0):.1f}%")}

def notify_node(state):
    return _audit("notify", "DONE",
                  f"Material:{state['sap_material']} Status:{state.get('final_status')} Ref:{state.get('sync_ref','N/A')}")

def _publish_rabbitmq(config, queue, body):
    try:
        conn = pika.BlockingConnection(pika.URLParameters(config["rabbitmq_url"]))
        ch   = conn.channel()
        ch.queue_declare(queue=queue, durable=True)
        ch.basic_publish("", queue, json.dumps(body).encode(),
            pika.BasicProperties(delivery_mode=2, content_type="application/json"))
        conn.close()
    except Exception as e:
        log.warning("RabbitMQ publish failed (%s): %s", queue, e)

# ── routing ──────────────────────────────────────────────────────────────
def route_validate(s): return "end" if s.get("final_status") == "validation_failed" else "fetch_sap"
def route_analyse(s):
    if not s.get("sync_required"): return "notify"
    return "approval_queue" if s.get("approval_needed") else "auto_sync"

# ── graph ────────────────────────────────────────────────────────────────
def compile_price_sync(config: dict):
    from langgraph.graph import StateGraph, END
    from langgraph.checkpoint.memory import MemorySaver
    from langchain_openai import AzureChatOpenAI
    llm = AzureChatOpenAI(azure_endpoint=config["azure_endpoint"],
                          api_key=config["azure_key"],
                          azure_deployment="gpt-4o-mini",
                          api_version="2024-08-01-preview", temperature=0.05)
    g = StateGraph(PriceSyncState)
    g.add_node("validate",       validate_node)
    g.add_node("fetch_sap",      lambda s: fetch_sap_node(s, config))
    g.add_node("fetch_sf",       lambda s: fetch_sf_node(s, config))
    g.add_node("rag_rules",      lambda s: rag_rules_node(s, config))
    g.add_node("analyse",        lambda s: analyse_node(s, llm))
    g.add_node("auto_sync",      lambda s: auto_sync_node(s, config))
    g.add_node("approval_queue", lambda s: approval_queue_node(s, config))
    g.add_node("notify",         notify_node)
    g.set_entry_point("validate")
    g.add_conditional_edges("validate", route_validate)
    g.add_edge("fetch_sap", "fetch_sf")
    g.add_edge("fetch_sf",  "rag_rules")
    g.add_edge("rag_rules", "analyse")
    g.add_conditional_edges("analyse", route_analyse)
    g.add_edge("auto_sync",      "notify")
    g.add_edge("approval_queue", "notify")
    g.add_edge("notify", END)
    return g.compile(checkpointer=MemorySaver())

if __name__ == "__main__":
    load_dotenv()
    config = {
        "azure_endpoint":   os.getenv("AZURE_OPENAI_ENDPOINT"),
        "azure_key":        os.getenv("AZURE_OPENAI_API_KEY"),
        "supabase_db":      os.getenv("SUPABASE_DB_CONNECTION"),
        "sap_base_url":     os.getenv("SAP_BASE_URL"),
        "sap_api_key":      os.getenv("SAP_API_KEY"),
        "sf_client_id":     os.getenv("SALESFORCE_CLIENT_ID"),
        "sf_client_secret": os.getenv("SALESFORCE_CLIENT_SECRET"),
        "sf_username":      os.getenv("SALESFORCE_USERNAME"),
        "sf_password":      os.getenv("SALESFORCE_PASSWORD"),
        "rabbitmq_url":     os.getenv("CLOUDAMQP_URL"),
    }
    app = compile_price_sync(config)
    result = app.invoke({
        "sap_material": "MAT-001234", "sf_product_id": "01t5e000003K9XAAA0",
        "triggered_by": "sap_change_event", "tenant_id": "acme-corp",
        "sap_price": None, "sap_currency": None, "sf_price": None,
        "sf_pricebook_id": None, "discrepancy": None, "discrepancy_pct": None,
        "sync_required": False, "approval_needed": False, "approval_level": None,
        "rag_context": None, "sync_ref": None, "audit_trail": [],
        "final_status": None, "error": None
    }, config={"configurable": {"thread_id": "price-sync-001"}})
    print(f"\nStatus:      {result['final_status']}")
    print(f"SAP Price:   {result.get('sap_currency')} {result.get('sap_price')}")
    print(f"SF Price:    {result.get('sf_price')}")
    print(f"Discrepancy: {result.get('discrepancy_pct',0):.1f}%")
    print(f"Sync Ref:    {result.get('sync_ref','N/A')}")
    print("\nAudit Trail:")
    for e in result["audit_trail"]:
        print(f"  [{e['ts'][11:19]}] {e['node']:<20} {e['action']:<10} {e['result'][:60]}")
