# syntax=docker/dockerfile:1
# EstrichONE Hosting-Konsole – Build-Kontext ist die Repository-Wurzel.
#   docker build -f docker/hosting.Dockerfile -t ghcr.io/florians25/estrichone-hosting:<version> .
# Enthält: Konsole (ASP.NET Core), Weboberfläche (OpenUI5, self-hosted), Docker-CLI mit Compose-Plugin,
# die Compose-Vorlagen der Suite (eine Quelle: deploy/compose*.yml) und den Installer (… install).

FROM node:24-bookworm@sha256:8fa96264f302f58bbb13aa7339c541547b56975766ce956c875da149452a3e40 AS ui
WORKDIR /src/hosting/ui
COPY hosting/ui/package.json hosting/ui/package-lock.json ./
RUN npm ci --no-audit --no-fund
# Logo unverändert aus branding/ (aktueller Stand des Logo-Pakets)
COPY branding/ /src/branding/
COPY hosting/ui/ ./
RUN npm run lint && npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb AS build
WORKDIR /src
COPY global.json ./
COPY hosting/Directory.Build.props hosting/Directory.Packages.props hosting/
COPY hosting/src/EstrichOne.Hosting/EstrichOne.Hosting.csproj hosting/src/EstrichOne.Hosting/
RUN dotnet restore hosting/src/EstrichOne.Hosting/EstrichOne.Hosting.csproj
COPY hosting/src/ hosting/src/
ARG ESTRICHONE_VERSION=0.1.0
RUN rm -rf hosting/src/EstrichOne.Hosting/wwwroot && dotnet publish hosting/src/EstrichOne.Hosting/EstrichOne.Hosting.csproj \
      --configuration Release --no-restore --output /app -p:Version=${ESTRICHONE_VERSION} -p:UseAppHost=false

FROM docker:29.9.3-cli@sha256:2357639ad1ea7d0194134bf3292622fd4e562483966ed674e5d7c945b5f3b28d AS dockercli

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:bfd912c83295ce66d4b706ebb8541b9f14259b0e76fbb88b2751f58ce2399d2e
# Docker-CLI und Compose-Plugin (statische Binärdateien) – gesteuert wird der Docker-Daemon des Hosts über den Socket.
COPY --from=dockercli /usr/local/bin/docker /usr/local/bin/docker
COPY --from=dockercli /usr/local/libexec/docker/cli-plugins/docker-compose /usr/local/libexec/docker/cli-plugins/docker-compose
WORKDIR /app
COPY --from=build /app ./
COPY --from=ui /src/hosting/ui/dist ./wwwroot/
# Vorlagen je Instanz: identisch mit dem Docker-Installer der Suite
COPY deploy/compose.yml deploy/compose.proxy.yml docker/installer/compose.override.yml ./templates/
# Installer: Compose-Datei der Konsole + Skript
COPY hosting/deploy/compose.yml hosting/deploy/env.example /opt/estrichone-hosting/
COPY hosting/deploy/install.sh /usr/local/bin/estrichone-hosting-install
# Verwaltungsbefehl für den Server (Ein-Befehl-Installation: curl … hosting/install.sh | sudo bash)
COPY hosting/install.sh /opt/estrichone-hosting/estrichone-hosting
COPY hosting/deploy/entrypoint.sh /usr/local/bin/estrichone-hosting
ARG ESTRICHONE_VERSION=0.1.0
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0 \
    DOCKER_CONFIG=/tmp/docker \
    HOSTING_IMAGE_VERSION=${ESTRICHONE_VERSION} \
    TZ=Europe/Berlin
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=3 \
  CMD ["dotnet", "/app/EstrichOne.Hosting.dll", "healthcheck"]
ENTRYPOINT ["estrichone-hosting"]
