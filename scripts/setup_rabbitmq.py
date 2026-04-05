#!/usr/bin/env python3
"""
setup_rabbitmq.py — Create all CloudAMQP queues.
Run once after signing up: python3 scripts/setup_rabbitmq.py
"""
import os, sys
from dotenv import load_dotenv
load_dotenv()

QUEUES = [
    ("sap-price-changes",    {"x-max-priority": 9}),
    ("servicenow-incidents", {"x-max-priority": 9}),
    ("stripe-events",        {"x-max-priority": 9}),
    ("hubspot-sync",         {}),
    ("approval-queue",       {"x-max-priority": 9}),
    ("notifications",        {}),
    ("dead-letter-archive",  {}),
]

def main():
    import pika
    url = os.getenv("CLOUDAMQP_URL")
    if not url:
        print("ERROR: CLOUDAMQP_URL not set in .env"); sys.exit(1)

    print(f"Connecting to CloudAMQP...")
    conn    = pika.BlockingConnection(pika.URLParameters(url))
    channel = conn.channel()
    for queue_name, args in QUEUES:
        channel.queue_declare(queue=queue_name, durable=True, arguments=args or None)
        print(f"  ✅ {queue_name}")
    conn.close()
    print(f"\n✅ All {len(QUEUES)} queues created.")

if __name__ == "__main__":
    main()
