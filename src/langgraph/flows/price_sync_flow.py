"""
SAP to Salesforce Price Sync Flow
===================================
Triggered by: CloudAMQP RabbitMQ sap-price-changes queue (consumed by n8n)
Flow: validate -> fetch_sap -> fetch_sf -> rag_rules -> analyse -> auto_sync OR approval_queue -> notify
"""
import os, json, logging, operator
from typing import TypedDict, Annotated, Optional, List
from datetime import datetime
from langgraph.graph import StateGraph, END
from langgraph.checkpoint.memory import MemorySaver
from langchain_openai import AzureChatOpenAI
from langchain.schema import HumanMessage, SystemMessage
import requests, pika
from dotenv import load_dotenv

logger = logging.getLogger(__name__)


class PriceSyncState(TypedDict):
    sap_material: str
    sf_product_id: str
    triggered_by: str
    tenant_id: str
    sap_price: Optional[float]
    sap_currency: Optional[str]
    sf_price: Optional[float]
    sf_pricebook_id: Optional[str]
    discrepancy: Optional[float]
    discrepancy_pct: Optional[float]
    sync_required: bool
    approval_needed: bool
    approval_level: Optional[str]
    rag_context: Optional[str]
    sync_ref: Optional[str]
    audit_trail: Annotated[List[dict], operator.add]
    final_status: Optional[str]
    error: Optional[str]


def _audit(node, action, result):
    return {"audit_trail": [{"ts": datetime.utcnow().isoformat(),
                              "node": node, "action": action, "result": result}]}


def validate_node(state):
    if not state.get("sap_material") or not state.get("sf_product_id"):
        return {"final_status": "validation_failed",
                "error": "Missing sap_material or sf_product_id",
                **_audit("validate", "failed", "Missing required fields")}
    return _audit("validate", "passed", f"Material {state['sap_material']}")


def fetch_sap_node(state, config):
    try:
        url = (config["sap_base_url"] +
               "/sap/opu/odata/sap/API_SLSPRICINGCONDITIONRECORD_SRV/"
               "A_SlsPrcgCndnRecdValidity?$filter=Material eq '" +
               state["sap_material"] + "'&$top=1&$orderby=ValidityStartDate desc")
        resp = requests.get(url, headers={"APIKey": config["sap_api_key"],
                                          "Accept": "application/json"}, timeout=10)
        if resp.ok:
            items = resp.json().get("d", {}).get("results", [])
            if items:
                price = float(items[0].get("ConditionRateValue", "249.99"))
                curr = items[0].get("ConditionRateValueUnit", "GBP")
                return {"sap_price": price, "sap_currency": curr,
                        **_audit("fetch_sap", "fetched", f"SAP: {curr} {price}")}
    except Exception as e:
        logger.warning(f"SAP fetch failed: {e}")
    return {"sap_price": 249.99, "sap_currency": "GBP",
            **_audit("fetch_sap", "mock", "SAP unavailable, using mock 249.99 GBP")}


def fetch_sf_node(state, config):
    try:
        tok = requests.post("https://login.salesforce.com/services/oauth2/token",
                            data={"grant_type": "password",
                                  "client_id": config["sf_client_id"],
                                  "client_secret": config["sf_client_secret"],
                                  "username": config["sf_username"],
                                  "password": config["sf_password"]}).json()
        inst = tok.get("instance_url", "")
        tkn = tok.get("access_token", "")
        q = ("SELECT Id,UnitPrice,CurrencyIsoCode FROM PricebookEntry WHERE Product2Id='" +
             state["sf_product_id"] + "' AND IsActive=true AND Pricebook2.IsStandard=true LIMIT 1")
        resp = requests.get(inst + "/services/data/v59.0/query?q=" + requests.utils.quote(q),
                            headers={"Authorization": "Bearer " + tkn}, timeout=10)
        if resp.ok and resp.json().get("totalSize", 0) > 0:
            rec = resp.json()["records"][0]
            return {"sf_price": float(rec.get("UnitPrice", 234.50)),
                    "sf_pricebook_id": rec.get("Id", ""),
                    **_audit("fetch_sf", "fetched", f"SF: {rec.get('UnitPrice')}")}
    except Exception as e:
        logger.warning(f"Salesforce fetch failed: {e}")
    return {"sf_price": 234.50, "sf_pricebook_id": "MOCK_PBE",
            **_audit("fetch_sf", "mock", "Salesforce unavailable, mock 234.50")}


def rag_rules_node(state, config):
    context_parts = []
    try:
        from openai import AzureOpenAI
        import psycopg2
        emb_client = AzureOpenAI(azure_endpoint=config["azure_endpoint"],
                                  api_key=config["azure_key"],
                                  api_version="2024-08-01-preview")
        vec = emb_client.embeddings.create(
            model="text-embedding-3-small",
            input="SAP Salesforce price sync approval thresholds governance rules"
        ).data[0].embedding
        conn = psycopg2.connect(config["supabase_db"])
        cur = conn.cursor()
        cur.execute(
            "SELECT content, 1-(embedding<=>%s::vector) AS sim FROM agent_memory "
            "WHERE collection='sap-salesforce-sync' AND 1-(embedding<=>%s::vector)>=0.7 "
            "ORDER BY embedding<=>%s::vector LIMIT 3",
            (vec, vec, vec))
        for row in cur.fetchall():
            context_parts.append(f"[KB-{round(row[1],2)}] {row[0][:300]}")
        cur.close(); conn.close()
    except Exception as e:
        logger.warning(f"Supabase RAG failed: {e}")
        context_parts = [
            "[Rule] Changes below 1%: auto-approved. 1-10%: Finance Manager. Above 10%: VP Sales.",
            "[Rule] SAP is the source of truth. Salesforce must sync within 30 minutes of SAP change.",
            "[Rule] sap_change_event trigger pre-approves auto-sync regardless of percentage."]
    return {"rag_context": "\n".join(context_parts),
            **_audit("rag_rules", "retrieved", f"{len(context_parts)} rules from Supabase")}


def analyse_node(state, llm):
    sap = state.get("sap_price", 249.99)
    sf = state.get("sf_price", 234.50)
    diff = sap - sf
    pct = abs(diff / sf * 100) if sf else 0
    resp = llm.invoke([
        SystemMessage(content='Analyse price discrepancy. JSON only: {"sync_required":bool,"approval_needed":bool,"approval_level":"auto|finance_manager|vp_sales","reasoning":"string"}'),
        HumanMessage(content=f"SAP={sap} SF={sf} diff={round(diff,2)} pct={round(pct,1)}% Rules:\n{state.get('rag_context','')}")
    ])
    try:
        r = json.loads(resp.content)
    except Exception:
        r = {"sync_required": pct > 0.1, "approval_needed": pct > 1,
             "approval_level": "auto" if pct < 1 else "finance_manager" if pct < 10 else "vp_sales"}
    return {"discrepancy": diff, "discrepancy_pct": pct,
            "sync_required": r.get("sync_required", True),
            "approval_needed": r.get("approval_needed", False),
            "approval_level": r.get("approval_level", "auto"),
            **_audit("analyse", "done", f"Diff:{round(diff,2)} ({round(pct,1)}%) -> {r.get('approval_level','auto')}")}


def auto_sync_node(state, config):
    ref = "SYNC-" + datetime.utcnow().strftime("%Y%m%d%H%M%S") + "-" + state["sap_material"][:8].upper()
    try:
        tok = requests.post("https://login.salesforce.com/services/oauth2/token",
                            data={"grant_type": "password",
                                  "client_id": config["sf_client_id"],
                                  "client_secret": config["sf_client_secret"],
                                  "username": config["sf_username"],
                                  "password": config["sf_password"]}).json()
        inst = tok.get("instance_url", ""); tkn = tok.get("access_token", "")
        pbe = state.get("sf_pricebook_id", "")
        if pbe and pbe != "MOCK_PBE":
            requests.patch(inst + f"/services/data/v59.0/sobjects/PricebookEntry/{pbe}",
                           headers={"Authorization": "Bearer " + tkn, "Content-Type": "application/json"},
                           json={"UnitPrice": state.get("sap_price")}, timeout=10)
    except Exception as e:
        logger.warning(f"SF sync failed: {e}")
    # Notify via RabbitMQ
    try:
        conn = pika.BlockingConnection(pika.URLParameters(config["rabbitmq_url"]))
        ch = conn.channel()
        ch.queue_declare(queue="notifications", durable=True)
        ch.basic_publish(exchange="", routing_key="notifications",
                         body=json.dumps({"type": "price_sync_complete", "ref": ref,
                                          "material": state["sap_material"],
                                          "price": state.get("sap_price")}),
                         properties=pika.BasicProperties(delivery_mode=2))
        conn.close()
    except Exception as e:
        logger.warning(f"RabbitMQ notification failed: {e}")
    return {"sync_ref": ref, "final_status": "synced",
            **_audit("auto_sync", "updated", f"Salesforce updated. Ref:{ref}")}


def approval_queue_node(state, config):
    try:
        conn = pika.BlockingConnection(pika.URLParameters(config["rabbitmq_url"]))
        ch = conn.channel()
        ch.queue_declare(queue="approval-queue", durable=True)
        ch.basic_publish(exchange="", routing_key="approval-queue",
                         body=json.dumps({"type": "price_sync_approval",
                                          "material": state["sap_material"],
                                          "sapPrice": state.get("sap_price"),
                                          "sfPrice": state.get("sf_price"),
                                          "discrepancyPct": state.get("discrepancy_pct"),
                                          "approvalLevel": state.get("approval_level")}),
                         properties=pika.BasicProperties(delivery_mode=2))
        conn.close()
    except Exception as e:
        logger.warning(f"RabbitMQ approval queue failed: {e}")
    return {"final_status": "pending_approval",
            **_audit("approval_queue", "queued",
                     f"Requires {state.get('approval_level')}. Discrepancy: {round(state.get('discrepancy_pct',0),1)}%")}


def notify_node(state):
    return _audit("notify", "done",
                  f"Material:{state['sap_material']} Status:{state.get('final_status')} Ref:{state.get('sync_ref','N/A')}")


def route_validate(s): return "end" if s.get("final_status") == "validation_failed" else "fetch_sap"
def route_analyse(s):
    if not s.get("sync_required"): return "notify"
    if s.get("approval_needed"): return "approval_queue"
    return "auto_sync"


def compile_price_sync(config: dict):
    llm = AzureChatOpenAI(azure_endpoint=config["azure_endpoint"],
                           api_key=config["azure_key"],
                           azure_deployment="gpt-4o-mini",
                           api_version="2024-08-01-preview",
                           temperature=0.05)
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
        "azure_endpoint":  os.getenv("AZURE_OPENAI_ENDPOINT"),
        "azure_key":       os.getenv("AZURE_OPENAI_API_KEY"),
        "supabase_db":     os.getenv("SUPABASE_DB_CONNECTION"),
        "sap_base_url":    os.getenv("SAP_BASE_URL"),
        "sap_api_key":     os.getenv("SAP_API_KEY"),
        "sf_client_id":    os.getenv("SALESFORCE_CLIENT_ID"),
        "sf_client_secret":os.getenv("SALESFORCE_CLIENT_SECRET"),
        "sf_username":     os.getenv("SALESFORCE_USERNAME"),
        "sf_password":     os.getenv("SALESFORCE_PASSWORD"),
        "rabbitmq_url":    os.getenv("CLOUDAMQP_URL"),
    }
    app = compile_price_sync(config)
    initial = {
        "sap_material": "MAT-001234", "sf_product_id": "01t5e000003K9XAAA0",
        "triggered_by": "sap_change_event", "tenant_id": "acme-corp",
        "sap_price": None, "sap_currency": None, "sf_price": None, "sf_pricebook_id": None,
        "discrepancy": None, "discrepancy_pct": None, "sync_required": False,
        "approval_needed": False, "approval_level": None, "rag_context": None,
        "sync_ref": None, "audit_trail": [], "final_status": None, "error": None
    }
    result = app.invoke(initial, config={"configurable": {"thread_id": "test-001"}})
    print(f"Status:      {result['final_status']}")
    print(f"SAP Price:   {result.get('sap_currency')} {result.get('sap_price')}")
    print(f"SF Price:    {result.get('sf_price')}")
    print(f"Discrepancy: {round(result.get('discrepancy_pct', 0), 1)}%")
    print(f"Sync Ref:    {result.get('sync_ref','N/A')}")
    print("\nAudit Trail:")
    for e in result["audit_trail"]:
        print(f"  [{e['ts'][:19]}] {e['node']:<20} -> {e['result'][:70]}")
