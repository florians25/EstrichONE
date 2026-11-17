# syntax=docker/dockerfile:1
# EstrichONE API – Build-Kontext ist die Repository-Wurzel.

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb AS build
WORKDIR /src
COPY global.json ./
COPY backend/Directory.Build.props backend/Directory.Packages.props backend/
COPY backend/src/EstrichOne.Api/EstrichOne.Api.csproj backend/src/EstrichOne.Api/
RUN dotnet restore backend/src/EstrichOne.Api/EstrichOne.Api.csproj
COPY backend/src/ backend/src/
ARG ESTRICHONE_VERSION=0.1.0
RUN dotnet publish backend/src/EstrichOne.Api/EstrichOne.Api.csproj \
      --configuration Release --no-restore --output /app \
      -p:Version=${ESTRICHONE_VERSION} -p:UseAppHost=false

# „chiseled“: keine Shell, kein Paketmanager, läuft als Benutzer app (UID 1654).
# „extra“: enthält ICU und Zeitzonendaten (Europe/Berlin).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra@sha256:cae5b962bfb786606def03d250c6a645cc98839095c89e806e4bd8978a68115b
WORKDIR /app
COPY --from=build /app ./
# App-Updates (docs/app-updates.md): Versionshinweise für „Neu in dieser Version“ und – im Release – das signierte
# Android-Paket derselben Version mit android.json (Prüfsumme, Signatur); ohne Paket bleibt das Verzeichnis leer.
COPY docs/releases/ /app/releases/
COPY docker/app-packages/ /app/app-packages/
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0 \
    TZ=Europe/Berlin
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=3 \
  CMD ["dotnet", "EstrichOne.Api.dll", "healthcheck"]
ENTRYPOINT ["dotnet", "EstrichOne.Api.dll"]
