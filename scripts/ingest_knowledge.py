#!/usr/bin/env python3
"""
ingest_knowledge.py — Seed Supabase pgvector with knowledge base articles.
Run once after Supabase setup: python3 scripts/ingest_knowledge.py
"""
import os, sys, uuid
from dotenv import load_dotenv
load_dotenv()

KNOWLEDGE = [
    {"collection": "sap-salesforce-sync", "source": "sync-governance-v1",
     "content": "SAP to Salesforce price sync rules: Changes below 1% of current price are auto-approved and sync immediately without human review. Changes between 1% and 10% require Finance Manager approval within 2 business hours. Changes above 10% require VP Sales approval and a written business justification logged in the sync record."},
    {"collection": "sap-salesforce-sync", "source": "sync-governance-v1",
     "content": "SAP is the single source of truth for all pricing. The Salesforce Standard Pricebook must be updated within 30 minutes of any SAP condition record change. The event trigger sap_change_event pre-authorises auto-sync regardless of percentage — it indicates the change was planned and approved upstream in SAP."},
    {"collection": "sap-salesforce-sync", "source": "sync-governance-v1",
     "content": "Every price sync must produce an audit record containing: SAP material number, previous price, new price, percentage change, approval level applied, sync reference number, and timestamp. Sync references follow the format SYNC-YYYYMMDDHHMMSS-MATERIALNUMBER."},
    {"collection": "servicenow-kb", "source": "incident-routing-v2",
     "content": "P1 Critical incidents (business down, direct revenue impact) must be assigned to the On-Call Engineer group and escalated by phone within 5 minutes. P2 High incidents must be assigned to the correct group within 15 minutes. P3 Medium and P4 Low incidents can be auto-classified and routed by the Azentix agent."},
    {"collection": "servicenow-kb", "source": "incident-routing-v2",
     "content": "SAP-related incidents containing keywords OData, RFC, BAPI, ICM, or gateway errors should be routed to the SAP-Basis group. Salesforce integration incidents route to CRM-Integration. Payment or billing incidents route to Finance-Operations. Network incidents route to Network-Ops."},
    {"collection": "servicenow-kb", "source": "incident-routing-v2",
     "content": "Auto-resolution is permitted for: standard password reset requests, VPN connectivity with documented workaround, routine software installation, and exact duplicate incidents (same description within 1 hour). Auto-resolve confidence threshold is 0.80. Below 0.80 always escalate to the appropriate group."},
    {"collection": "servicenow-kb", "source": "sap-known-issues-v1",
     "content": "SAP 503 Service Unavailable on OData endpoints: Check ICM service in SM51. Common fix: restart ICM in transaction SMICM. If issue persists check RFC destination in SM59. Gateway overload during batch windows (22:00-02:00 UTC) is a known pattern — retry after window."},
    {"collection": "stripe-policies", "source": "payment-policies-v1",
     "content": "Failed Stripe payments require immediate ServiceNow incident creation with urgency=2 impact=2. The incident description must include the Stripe payment intent ID, customer email, failed amount, and failure reason code. The Salesforce opportunity associated with the customer must be flagged with payment_failed=true within 5 minutes of the failure event."},
    {"collection": "stripe-policies", "source": "payment-policies-v1",
     "content": "HubSpot contacts associated with failed payments must be tagged with payment_failed=true and moved to the Payment Recovery marketing list. An automated email recovery sequence triggers 2 hours after the failure event. If the payment is recovered within 24 hours, the tags are reversed automatically."},
]

def main():
    from openai import AzureOpenAI, OpenAI
    import psycopg2
    from pgvector.psycopg2 import register_vector

    provider = (os.getenv("MODEL_PROVIDER", "azure") or "azure").strip().lower()
    azure_endpoint = os.getenv("AZURE_OPENAI_ENDPOINT")
    azure_key      = os.getenv("AZURE_OPENAI_API_KEY")
    azure_embed_model = os.getenv("AZURE_OPENAI_EMBEDDING_DEPLOYMENT", "text-embedding-3-small")

    ollama_base_url = os.getenv("OLLAMA_BASE_URL", "http://localhost:11434/v1")
    ollama_api_key  = os.getenv("OLLAMA_API_KEY", "ollama")
    ollama_embed_model = os.getenv("OLLAMA_EMBED_MODEL", "")

    db_conn_str    = os.getenv("SUPABASE_DB_CONNECTION")

    if provider not in ("azure", "ollama"):
        print("ERROR: MODEL_PROVIDER must be 'azure' or 'ollama'")
        sys.exit(1)

    if not db_conn_str:
        print("ERROR: SUPABASE_DB_CONNECTION is required")
        sys.exit(1)

    if provider == "azure":
        if not all([azure_endpoint, azure_key]):
            print("ERROR: Azure mode requires AZURE_OPENAI_ENDPOINT and AZURE_OPENAI_API_KEY")
            sys.exit(1)
        embed_model = azure_embed_model
        client = AzureOpenAI(azure_endpoint=azure_endpoint,
                             api_key=azure_key, api_version="2024-08-01-preview")
    else:
        if not ollama_embed_model:
            print("ERROR: Ollama mode requires OLLAMA_EMBED_MODEL")
            sys.exit(1)
        embed_model = ollama_embed_model
        client = OpenAI(base_url=ollama_base_url, api_key=ollama_api_key)

    conn   = psycopg2.connect(db_conn_str)
    register_vector(conn)
    cur    = conn.cursor()

    print(f"Ingesting {len(KNOWLEDGE)} articles into Supabase pgvector with provider={provider} model={embed_model}...\n")
    for i, doc in enumerate(KNOWLEDGE, 1):
        print(f"  [{i}/{len(KNOWLEDGE)}] {doc['collection']} / {doc['source']}")
        embedding = client.embeddings.create(
            model=embed_model, input=doc["content"]).data[0].embedding
        doc_id = str(uuid.uuid5(uuid.NAMESPACE_DNS,
                                doc["collection"] + doc["content"][:60]))
        cur.execute("""
            INSERT INTO agent_memory (id, content, embedding, collection, source, scope, stored_at)
            VALUES (%s, %s, %s, %s, %s, 'LongTerm', NOW())
            ON CONFLICT(id) DO UPDATE
              SET content=EXCLUDED.content,
                  embedding=EXCLUDED.embedding,
                  stored_at=NOW()
        """, (doc_id, doc["content"], embedding, doc["collection"], doc["source"]))

    conn.commit(); cur.close(); conn.close()
    print(f"\n✅ Ingested {len(KNOWLEDGE)} articles. Supabase pgvector ready for RAG queries.")

if __name__ == "__main__":
    main()
