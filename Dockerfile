# Root Dockerfile for Render reliability.
# Uses repository root as build context and publishes Azentix.AgentHost.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files first for better restore caching.
COPY src/Azentix.sln ./src/Azentix.sln
COPY src/Azentix.AgentHost/Azentix.AgentHost.csproj ./src/Azentix.AgentHost/
COPY src/Azentix.Agents/Azentix.Agents.csproj ./src/Azentix.Agents/
COPY src/Azentix.Models/Azentix.Models.csproj ./src/Azentix.Models/
COPY src/Azentix.Tests.Unit/Azentix.Tests.Unit.csproj ./src/Azentix.Tests.Unit/

RUN dotnet restore ./src/Azentix.sln --runtime linux-x64

# Copy full repository after restore.
COPY . .

RUN dotnet publish ./src/Azentix.AgentHost/Azentix.AgentHost.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    --runtime linux-x64 \
    --self-contained false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y curl --no-install-recommends \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV PORT=10000
ENV ASPNETCORE_URLS=http://+:${PORT}

HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 CMD curl -f http://localhost:${PORT}/health || exit 1

EXPOSE ${PORT}
ENTRYPOINT ["dotnet", "Azentix.AgentHost.dll"]
