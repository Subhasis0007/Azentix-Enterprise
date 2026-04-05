# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY Azentix.sln .
COPY Azentix.AgentHost/Azentix.AgentHost.csproj Azentix.AgentHost/
COPY Azentix.Agents/Azentix.Agents.csproj Azentix.Agents/
COPY Azentix.Models/Azentix.Models.csproj Azentix.Models/

RUN dotnet restore Azentix.sln

COPY . .

RUN dotnet publish Azentix.AgentHost/Azentix.AgentHost.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    --runtime linux-x64 \
    --self-contained false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV PORT=5000
ENV ASPNETCORE_URLS=http://+:${PORT}

HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
  CMD curl -f http://localhost:${PORT}/health || exit 1

EXPOSE ${PORT}
ENTRYPOINT ["dotnet", "Azentix.AgentHost.dll"]
