# syntax=docker/dockerfile:1
# EstrichONE Backup-Ziel (restic-REST-Server, nur anhängen) – Build-Kontext ist die Repository-Wurzel.
# Ein Befehl auf dem Zielrechner (siehe deploy/backup-target/README.md):
#   docker run -d --name estrichone-backup-target --restart unless-stopped -p 8444:8443 -v estrichone-backup-target:/data ghcr.io/florians25/estrichone-backup-target:latest
# rest-server aus den Quellen mit aktuellem Go bauen (das offizielle Image bringt einen veralteten Go-Compiler mit
# bekannten Schwachstellen in der Standardbibliothek mit). Version und Basis-Image fest (Digest).
FROM golang:1.27-alpine@sha256:d200d2dd76b03a99b52651fd1325aff7dcd35422c35c01bac56571b0507f3deb AS rest-server
ARG REST_SERVER_VERSION=v0.14.0
RUN CGO_ENABLED=0 GOBIN=/out go install -trimpath -ldflags "-s -w" github.com/restic/rest-server/cmd/rest-server@${REST_SERVER_VERSION} \
 && test -x /out/rest-server

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
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
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra@sha256:9b755ea799ae649141a073e30d8b62d8095a5465af2b7694d17866e8c05131d8
WORKDIR /app
COPY --from=build /app ./
COPY --from=rest-server /out/rest-server /usr/local/bin/rest-server
# Datenverzeichnis gehört dem App-Benutzer, damit ein benanntes Docker-Volume ohne weitere Schritte beschreibbar ist.
COPY --from=build --chown=1654:1654 /src/global.json /data/.keep
# Gemeinsames Verzeichnis mit dem Updater-Container (Fernaktualisierung, docker/installer/peer.sh).
COPY --from=build --chown=1654:1654 /src/global.json /update/.keep
ENV ESTRICHONE_ROLE=backup-target \
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
