# syntax=docker/dockerfile:1
# ─────────────────────────────────────────────────────────────────────────────
# Imagen del Hosted Agent de Upgrade Captain (step-7). Se construye DESDE LA RAÍZ del repo:
#
#   docker build -f deploy/Dockerfile -t upgrade-captain-hosted .
#   docker run --rm -p 8088:8088 --env-file .env upgrade-captain-hosted
#   curl -i localhost:8088/readiness
#
# Base SDK (no aspnet): el agente compila y ejecuta los tests de Canarias.Tours DENTRO de su sesión.
# Para Foundry la imagen debe ser linux/amd64 (azd la construye en remoto en ACR).
# ─────────────────────────────────────────────────────────────────────────────

# 1. Publicar el host
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props nuget.config UpgradeCaptain.slnx ./
COPY src/ src/
COPY vendor/ vendor/
COPY skills/ skills/
RUN dotnet publish src/UpgradeCaptain.Hosted -c Release -o /app --nologo

# 2. Datos de la demo: feed local de Teide.Geo, plantilla del repo y paquetes precargados (sin red en la sesión)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS demo
ENV NUGET_PACKAGES=/opt/nuget DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
WORKDIR /demo-src
COPY global.json ./
COPY demo/ demo/
COPY scripts/ scripts/
RUN bash scripts/pack-feed.sh \
 && mkdir -p /opt/demo/security \
 && cp -R demo/feed /opt/demo/feed \
 && cp demo/security/advisories.json /opt/demo/security/ \
 && cp demo/identities.json /opt/demo/ \
 && bash scripts/reset-demo.sh --workspace /opt/demo/canarias-tours --feed /opt/demo/feed --quiet
# Precarga los paquetes de 1.8.0 y de 2.0.0 (estado tras los arreglos mecánicos) en /opt/nuget.
RUN cd /opt/demo/canarias-tours && dotnet build --nologo -v q \
 && bash /demo-src/scripts/reset-demo.sh --state after-mechanical-fix --workspace /tmp/warm --feed /opt/demo/feed --quiet \
 && cd /tmp/warm && dotnet build --nologo -v q && (dotnet test --no-build --nologo -v q || true) \
 && rm -rf /tmp/warm \
 && find /opt/demo/canarias-tours -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +

# 3. Imagen final
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS final
# python3 SOLO para CodeAct (LocalCodeActProvider de Agent Framework ejecuta el Python que escribe el modelo).
# Upgrade Captain no tiene código Python: los scripts de las skills son apps de un solo fichero de .NET 10.
RUN apt-get update && apt-get install -y --no-install-recommends python3 && rm -rf /var/lib/apt/lists/*
COPY --from=build /app /app
COPY --from=demo /opt/demo /opt/demo
COPY --from=demo /opt/nuget /opt/nuget
# Precompila el script C# de la skill nuget-upgrade (dotnet run lo deja en caché para la primera llamada del agente).
RUN mkdir -p /tmp/warm && cp /opt/demo/canarias-tours/Directory.Packages.props /tmp/warm/ \
 && dotnet run /app/skills/nuget-upgrade/scripts/bump_package.cs -- /tmp/warm/Directory.Packages.props Teide.Geo 1.8.0 \
 && rm -rf /tmp/warm
ENV ASPNETCORE_URLS=http://+:8088 \
    NUGET_PACKAGES=/opt/nuget \
    UPGRADE_CAPTAIN_DEMO_ROOT=/opt/demo \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1
EXPOSE 8088
WORKDIR /app
ENTRYPOINT ["dotnet", "UpgradeCaptain.Hosted.dll"]
