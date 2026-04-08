# ── Stage 1: build ───────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files first (better layer caching)
COPY Azentix.sln .
COPY Azentix.AgentHost/Azentix.AgentHost.csproj   Azentix.AgentHost/
COPY Azentix.Agents/Azentix.Agents.csproj          Azentix.Agents/
COPY Azentix.Models/Azentix.Models.csproj           Azentix.Models/
COPY Azentix.Tests.Unit/Azentix.Tests.Unit.csproj   Azentix.Tests.Unit/

RUN dotnet restore Azentix.sln --runtime linux-x64

# Copy all source
COPY . .

RUN dotnet publish Azentix.AgentHost/Azentix.AgentHost.csproj \n    --configuration Release \n    --no-restore \n    --output /app/publish \n    --runtime linux-x64 \n    --self-contained false

# ── Stage 2: runtime ──────────────────────────────────────��──────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y curl --no-install-recommends \n    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV PORT=5000
ENV ASPNETCORE_URLS=http://+:${PORT}

HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \n  CMD curl -f http://localhost:${PORT}/health || exit 1

EXPOSE ${PORT}
ENTRYPOINT ["dotnet", "Azentix.AgentHost.dll"]