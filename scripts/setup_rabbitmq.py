"""
setup_rabbitmq.py — Creates all CloudAMQP queues programmatically.
Run once after CloudAMQP account created: python scripts/setup_rabbitmq.py
"""
import os, pika
from dotenv import load_dotenv
load_dotenv()

QUEUES = [
    "sap-price-changes",
    "servicenow-incidents",
    "stripe-events",
    "hubspot-sync",
    "approval-queue",
    "notifications",
    "dead-letter-archive",
]

def setup():
    url = os.getenv("CLOUDAMQP_URL")
    if not url:
        print("ERROR: CLOUDAMQP_URL not set in .env")
        return
    print(f"Connecting to CloudAMQP...")
    conn = pika.BlockingConnection(pika.URLParameters(url))
    ch = conn.channel()
    for q in QUEUES:
        ch.queue_declare(queue=q, durable=True, arguments={"x-max-priority": 9})
        print(f"  ✅ Created queue: {q}")
    conn.close()
    print(f"\n✅ All {len(QUEUES)} queues created in CloudAMQP.")

if __name__ == "__main__":
    setup()
