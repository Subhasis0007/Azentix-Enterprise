"""
LangGraph queue worker
Consumes CloudAMQP queues and runs the three core use cases:
1) SAP -> Salesforce price sync
2) ServiceNow incident triage
3) Stripe billing alert handling
"""

import json
import logging
import os
import threading
from typing import Any, Dict

import pika
from dotenv import load_dotenv

from flows.incident_triage_flow import compile_incident_triage
from flows.price_sync_flow import compile_price_sync
from flows.stripe_billing_flow import compile_stripe_billing


logging.basicConfig(
    level=os.getenv("LOG_LEVEL", "INFO"),
    format="%(asctime)s %(levelname)s [%(threadName)s] %(message)s",
)
log = logging.getLogger("langgraph-worker")


def _load_config() -> Dict[str, Any]:
    load_dotenv()
    return {
        "azure_endpoint": os.getenv("AZURE_OPENAI_ENDPOINT", ""),
        "azure_key": os.getenv("AZURE_OPENAI_API_KEY", ""),
        "supabase_db": os.getenv("SUPABASE_DB_CONNECTION", ""),
        "sap_base_url": os.getenv("SAP_BASE_URL", ""),
        "sap_api_key": os.getenv("SAP_API_KEY", ""),
        "sf_client_id": os.getenv("SALESFORCE_CLIENT_ID", ""),
        "sf_client_secret": os.getenv("SALESFORCE_CLIENT_SECRET", ""),
        "sf_username": os.getenv("SALESFORCE_USERNAME", ""),
        "sf_password": os.getenv("SALESFORCE_PASSWORD", ""),
        "snow_url": os.getenv("SERVICENOW_INSTANCE_URL", ""),
        "snow_user": os.getenv("SERVICENOW_USERNAME", ""),
        "snow_pass": os.getenv("SERVICENOW_PASSWORD", ""),
        "stripe_key": os.getenv("STRIPE_SECRET_KEY", ""),
        "hs_token": os.getenv("HUBSPOT_ACCESS_TOKEN", ""),
        "hs_api": os.getenv("HUBSPOT_API_BASE", "https://api.hubapi.com"),
        "rabbitmq_url": os.getenv("CLOUDAMQP_URL", ""),
    }


def _initial_price_state(payload: Dict[str, Any]) -> Dict[str, Any]:
    return {
        "sap_material": payload.get("sap_material") or payload.get("material") or "MAT-001234",
        "sf_product_id": payload.get("sf_product_id") or payload.get("product_id") or "01t5e000003K9XAAA0",
        "triggered_by": payload.get("triggered_by") or "queue_event",
        "tenant_id": payload.get("tenant_id") or "default-tenant",
        "sap_price": None,
        "sap_currency": None,
        "sf_price": None,
        "sf_pricebook_id": None,
        "discrepancy": None,
        "discrepancy_pct": None,
        "sync_required": False,
        "approval_needed": False,
        "approval_level": None,
        "rag_context": None,
        "sync_ref": None,
        "audit_trail": [],
        "final_status": None,
        "error": None,
    }


def _initial_incident_state(payload: Dict[str, Any]) -> Dict[str, Any]:
    return {
        "incident_number": payload.get("incident_number") or "INC0001234",
        "incident_sys_id": payload.get("incident_sys_id"),
        "short_description": payload.get("short_description"),
        "description": payload.get("description"),
        "current_priority": payload.get("current_priority"),
        "category": payload.get("category"),
        "kb_articles": None,
        "ai_classification": None,
        "action_taken": None,
        "audit_trail": [],
        "final_status": None,
        "error": None,
    }


def _initial_stripe_state(payload: Dict[str, Any]) -> Dict[str, Any]:
    return {
        "stripe_event_type": payload.get("stripe_event_type") or payload.get("event_type") or "payment_intent.payment_failed",
        "stripe_payment_id": payload.get("stripe_payment_id") or payload.get("payment_id"),
        "stripe_customer_id": payload.get("stripe_customer_id") or payload.get("customer_id"),
        "customer_email": payload.get("customer_email"),
        "payment_amount": payload.get("payment_amount"),
        "payment_currency": payload.get("payment_currency"),
        "failure_reason": payload.get("failure_reason"),
        "snow_incident_id": None,
        "sf_opportunity_id": None,
        "hubspot_contact_id": None,
        "audit_trail": [],
        "final_status": None,
        "error": None,
    }


def _consume_loop(queue_name: str, graph, state_builder, rabbitmq_url: str) -> None:
    while True:
        conn = None
        try:
            conn = pika.BlockingConnection(pika.URLParameters(rabbitmq_url))
            channel = conn.channel()
            channel.queue_declare(queue=queue_name, durable=True)
            channel.basic_qos(prefetch_count=1)

            def _callback(ch, _method, _props, body: bytes) -> None:
                try:
                    payload = json.loads(body.decode("utf-8")) if body else {}
                    state = state_builder(payload)
                    result = graph.invoke(
                        state,
                        config={"configurable": {"thread_id": f"{queue_name}-{payload.get('id', 'evt')}"}},
                    )
                    log.info("Queue=%s status=%s", queue_name, result.get("final_status"))
                    ch.basic_ack(delivery_tag=_method.delivery_tag)
                except Exception as ex:
                    log.exception("Queue=%s processing failed: %s", queue_name, ex)
                    # Requeue false to avoid poison-loop storms.
                    ch.basic_nack(delivery_tag=_method.delivery_tag, requeue=False)

            channel.basic_consume(queue=queue_name, on_message_callback=_callback)
            log.info("Consuming queue=%s", queue_name)
            channel.start_consuming()
        except Exception as ex:
            log.exception("Queue=%s loop crashed: %s. Reconnecting...", queue_name, ex)
        finally:
            try:
                if conn and conn.is_open:
                    conn.close()
            except Exception:
                pass


def main() -> None:
    config = _load_config()
    rabbitmq_url = config.get("rabbitmq_url")
    if not rabbitmq_url:
        raise RuntimeError("CLOUDAMQP_URL is required for langgraph worker")

    price_graph = compile_price_sync(config)
    triage_graph = compile_incident_triage(config)
    stripe_graph = compile_stripe_billing(config)

    queue_price = os.getenv("RABBITMQ_QUEUE_SAP_PRICES", "sap-price-changes")
    queue_inc = os.getenv("RABBITMQ_QUEUE_INCIDENTS", "servicenow-incidents")
    queue_stripe = os.getenv("RABBITMQ_QUEUE_STRIPE", "stripe-events")

    workers = [
        threading.Thread(
            target=_consume_loop,
            name="price-sync-worker",
            args=(queue_price, price_graph, _initial_price_state, rabbitmq_url),
            daemon=True,
        ),
        threading.Thread(
            target=_consume_loop,
            name="incident-triage-worker",
            args=(queue_inc, triage_graph, _initial_incident_state, rabbitmq_url),
            daemon=True,
        ),
        threading.Thread(
            target=_consume_loop,
            name="stripe-billing-worker",
            args=(queue_stripe, stripe_graph, _initial_stripe_state, rabbitmq_url),
            daemon=True,
        ),
    ]

    for t in workers:
        t.start()

    for t in workers:
        t.join()


if __name__ == "__main__":
    main()
