#!/bin/bash
set -e
echo "==> Setting up Azentix dev environment..."
pip install --upgrade pip -q
pip install langgraph==0.2.28 langchain==0.3.7 langchain-openai==0.2.7 \
  openai==1.54.0 supabase==2.9.1 psycopg2-binary==2.9.10 \
  simple-salesforce==1.12.6 hubspot-api-client==9.0.0 stripe==11.1.0 \
  pika==1.3.2 python-dotenv==1.0.1 requests==2.32.3 \
  pytest==8.3.3 pytest-asyncio==0.24.0 -q

dotnet tool install --global dotnet-ef 2>/dev/null || true

curl -sL https://github.com/Kong/deck/releases/latest/download/deck_linux_amd64.tar.gz | tar -xz -C /tmp/
sudo mv /tmp/deck /usr/local/bin/ 2>/dev/null || true

curl -Ls https://cli.doppler.com/install.sh | sh 2>/dev/null || true

echo "==> Done! Run: python3 scripts/demo_offline.py"
