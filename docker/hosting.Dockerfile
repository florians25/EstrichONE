# syntax=docker/dockerfile:1
# EstrichONE Hosting-Konsole – Build-Kontext ist die Repository-Wurzel.
#   docker build -f docker/hosting.Dockerfile -t ghcr.io/florians25/estrichone-hosting:<version> .
# Enthält: Konsole (ASP.NET Core), Weboberfläche (OpenUI5, self-hosted), Docker-CLI mit Compose-Plugin,
# die Compose-Vorlagen der Suite (eine Quelle: deploy/compose*.yml) und den Installer (… install).

FROM node:26-bookworm@sha256:fa52d1669505e29b58c042875b1d65c229e0787a3610fd52869b22b68e480853 AS ui
WORKDIR /src/hosting/ui
COPY hosting/ui/package.json hosting/ui/package-lock.json ./
RUN npm ci --no-audit --no-fund
# Logo unverändert aus branding/ (aktueller Stand des Logo-Pakets)
COPY branding/ /src/branding/
COPY hosting/ui/ ./
RUN npm run lint && npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
WORKDIR /src
COPY global.json ./
COPY hosting/Directory.Build.props hosting/Directory.Packages.props hosting/
COPY hosting/src/EstrichOne.Hosting/EstrichOne.Hosting.csproj hosting/src/EstrichOne.Hosting/
RUN dotnet restore hosting/src/EstrichOne.Hosting/EstrichOne.Hosting.csproj
COPY hosting/src/ hosting/src/
ARG ESTRICHONE_VERSION=0.1.0
RUN rm -rf hosting/src/EstrichOne.Hosting/wwwroot && dotnet publish hosting/src/EstrichOne.Hosting/EstrichOne.Hosting.csproj \
      --configuration Release --no-restore --output /app -p:Version=${ESTRICHONE_VERSION} -p:UseAppHost=false

FROM docker:29.9.2-cli@sha256:5e6baf67325a79f64ed3bed6863954d5e05d0f3d4e57cbdb14809346814bbfdb AS dockercli

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee
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
