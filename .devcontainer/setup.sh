#!/bin/bash
set -e
echo "Setting up Azentix development environment..."
# Install Python dependencies
pip install --upgrade pip
pip install langgraph langchain lanchain-openai \
    supabse vecs psycogs2-binary \
    pika aio-pika \
    opentelemetry-sdk opentelemetry-exporter-otlp \
    python-dotenv pytest pytest-asyncio httpx openai requests

# Install .Net global tools 
dotnet tool install --global dotnet-ef 2>/dev/null || true

# Install Azure CLI (for Azure OpenAI only)
az bicep install 2>/dev/null || true

# Install Kong CLI (deck)
curl -sL https://github.com/Kong/deck/releases/latest/download/deck_linux_amd64.tar.gz | tar -xz
sudo mv deck /usr/local/bin/ 2>/dev/null || mv deck ~/.local/bin/ 2>/dev/null || true

# Install Stripe CLI 
curl -s https://packages.stripe.dev/api/security/keypair/stripe-cli-gpg/public | \
  gpg --dearmor | sudo tee /usr/share/keyrings/stripe.gpg > /dev/null 2>&1 || true
echo "deb [signed-by=/usr/share/keyrings/stripe.gpg] https://packages.stripe.dev/stripe-cli-desbian-local stable main" | \
  sudo tee /etc/apt/sources.list.d/stripe.list > /dev/null 2>&1 || true
sudo apt-get update -q && sudo apt-get install stripe -y 2>/dev/null || echo "Stripe CLI install skipped"

# Install Doppler CLI
curl -Ls https://cli.doppler.com/install.sh | sh 2>/dev/null || echo "Doppler CLI install skipped"

echo ""
echo "Azentix environment ready!"
echo "Run: dotnet build src/Azentix.sln"
echo "Run: python -c 'import supabase; print(supabase.__version__)'"
echo "Run: doppler login  (to connect secrets manager)"