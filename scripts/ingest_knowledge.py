"""
ingest_knowledge.py — Seeds Supabase pgvector with SAP/Salesforce/ServiceNow knowledge.
Run once after Supabase setup: python scripts/ingest_knowledge.py
"""
import os, json, uuid
from dotenv import load_dotenv
load_dotenv()

KNOWLEDGE_BASE = [
    {"collection": "sap-salesforce-sync", "source": "sync-rules-v1", "content": "SAP to Salesforce price sync governance rules. Changes below 1% of current price are auto-approved and sync immediately. Changes between 1% and 10% require Finance Manager approval within 2 business hours. Changes above 10% require VP Sales approval and must be logged with business justification."},
    {"collection": "sap-salesforce-sync", "source": "sync-rules-v1", "content": "SAP is the single source of truth for pricing. Salesforce Standard Pricebook must be updated within 30 minutes of any SAP condition record change. The triggering event type sap_change_event pre-authorises auto-sync regardless of percentage change."},
    {"collection": "sap-salesforce-sync", "source": "sync-rules-v1", "content": "Price sync audit requirements: every sync must log SAP material number, old price, new price, percentage change, approval level, sync reference, and timestamp. Sync references follow format SYNC-YYYYMMDDHHMMSS-MATERIAL."},
    {"collection": "servicenow-kb", "source": "incident-routing-v2", "content": "P1 Critical incidents (business down, revenue impact) must be assigned to the On-Call Engineer group and escalated by phone within 5 minutes. P2 High incidents must be assigned within 15 minutes. P3 Medium and P4 Low incidents can be auto-classified and routed."},
    {"collection": "servicenow-kb", "source": "incident-routing-v2", "content": "SAP-related incidents with keywords: OData, RFC, BAPI, BW, authorization should be routed to SAP-Basis group. Salesforce integration incidents route to CRM-Integration group. Payment and billing incidents route to Finance-Operations group."},
    {"collection": "servicenow-kb", "source": "incident-routing-v2", "content": "Auto-resolution is permitted for: password reset requests, VPN connectivity with known workaround, standard software installation, and duplicate incidents. Auto-resolution confidence threshold is 0.8. Below 0.8, route to appropriate group."},
    {"collection": "stripe-policies", "source": "payment-policies-v1", "content": "Failed Stripe payments require immediate ServiceNow incident creation with urgency=2 impact=2. The incident must reference the Stripe payment intent ID and customer email. Salesforce opportunity must be flagged with payment_failed=true within 5 minutes."},
    {"collection": "stripe-policies", "source": "payment-policies-v1", "content": "HubSpot contacts associated with failed payments must be tagged payment_failed=true and moved to the Payment Recovery marketing list. Email sequence triggers automatically after 2 hours if payment not recovered."},
]

def ingest():
    from openai import AzureOpenAI
    import psycopg2
    from pgvector.psycopg2 import register_vector

    print(f"Ingesting {len(KNOWLEDGE_BASE)} knowledge articles into Supabase pgvector...")
    client = AzureOpenAI(azure_endpoint=os.getenv("AZURE_OPENAI_ENDPOINT"),
                          api_key=os.getenv("AZURE_OPENAI_API_KEY"), api_version="2024-08-01-preview")
    conn = psycopg2.connect(os.getenv("SUPABASE_DB_CONNECTION"))
    register_vector(conn)
    cur = conn.cursor()

    for i, doc in enumerate(KNOWLEDGE_BASE):
        print(f"  [{i+1}/{len(KNOWLEDGE_BASE)}] Embedding: {doc['source']} / {doc['collection']}")
        emb = client.embeddings.create(model="text-embedding-3-small", input=doc["content"]).data[0].embedding
        doc_id = str(uuid.uuid5(uuid.NAMESPACE_DNS, doc["collection"] + doc["content"][:50]))
        cur.execute(
            "INSERT INTO agent_memory (id,content,embedding,collection,source,scope,stored_at) "
            "VALUES (%s,%s,%s,%s,%s,'LongTerm',NOW()) ON CONFLICT(id) DO UPDATE "
            "SET content=EXCLUDED.content, embedding=EXCLUDED.embedding",
            (doc_id, doc["content"], emb, doc["collection"], doc["source"]))
    conn.commit()
    cur.close(); conn.close()
    print(f"\n✅ Ingested {len(KNOWLEDGE_BASE)} articles. Supabase pgvector ready.")

if __name__ == "__main__":
    ingest()
