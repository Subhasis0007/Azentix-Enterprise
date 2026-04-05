#!/bin/bash
set -e
echo "Setting up Azentix development environment..."

# Install Python dependencies
pip install --upgrade pip
pip install langgraph==0.2.28 langchain==0.3.7 langchain-openai==0.2.7 \
  openai==1.54.0 supabase==2.9.1 vecs==0.4.3 psycopg2-binary==2.9.10 \
  simple-salesforce==1.12.6 hubspot-api-client==9.0.0 stripe==11.1.0 \
  pika==1.3.2 aio-pika python-dotenv==1.0.1 \
  pytest==8.3.3 pytest-asyncio==0.24.0 httpx requests==2.32.3 \
  opentelemetry-sdk opentelemetry-exporter-otlp

# Install .NET global tools
dotnet tool install --global dotnet-ef 2>/dev/null || true

# Install deck (Kong config tool)
curl -sL https://github.com/Kong/deck/releases/latest/download/deck_linux_amd64.tar.gz | tar -xz
sudo mv deck /usr/local/bin/ 2>/dev/null || mv deck ~/.local/bin/ 2>/dev/null || true

# Install Doppler CLI
curl -Ls https://cli.doppler.com/install.sh | sh 2>/dev/null || echo "Doppler CLI install skipped"

echo ""
echo "Azentix environment ready!"
echo "Run: dotnet build src/Azentix.sln"
echo "Run: python -c 'import langgraph; print(langgraph.__version__)'"
echo "Run: doppler login"
