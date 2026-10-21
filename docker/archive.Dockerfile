# syntax=docker/dockerfile:1
# EstrichONE Archivserver (GoBD-orientiert, WORM) – Build-Kontext ist die Repository-Wurzel.
# Ein Befehl auf dem Zielrechner (siehe deploy/archive/README.md):
#   docker run -d --name estrichone-archive --restart unless-stopped -p 8443:8443 -v estrichone-archive:/data ghcr.io/florians25/estrichone-archive:latest

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY backend/Directory.Build.props backend/Directory.Packages.props backend/
COPY backend/src/EstrichOne.Archive/EstrichOne.Archive.csproj backend/src/EstrichOne.Archive/
RUN dotnet restore backend/src/EstrichOne.Archive/EstrichOne.Archive.csproj
COPY backend/src/EstrichOne.Archive/ backend/src/EstrichOne.Archive/
ARG ESTRICHONE_VERSION=0.1.0
RUN dotnet publish backend/src/EstrichOne.Archive/EstrichOne.Archive.csproj --configuration Release --no-restore --output /app \
      -p:Version=${ESTRICHONE_VERSION}

# Ohne Shell und Paketmanager, Benutzer app (UID 1654); „extra“ enthält die Zeitzonendaten (Europe/Berlin).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app
COPY --from=build /app ./

# Datenverzeichnis gehört dem App-Benutzer, damit ein benanntes Docker-Volume ohne weitere Schritte beschreibbar ist.
COPY --from=build --chown=1654:1654 /src/global.json /data/.keep
ENV ESTRICHONE_ROLE=archive \
    ESTRICHONE_DATA=/data \
    PATH=/app:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin \
    DOTNET_gcServer=0 \
    ASPNETCORE_HTTP_PORTS= \
    TZ=Europe/Berlin
VOLUME /data
EXPOSE 8443
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 CMD ["estrichone-archive", "healthcheck"]
ENTRYPOINT ["estrichone-archive"]
CMD ["serve"]
