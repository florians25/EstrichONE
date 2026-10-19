# syntax=docker/dockerfile:1
# EstrichONE API – Build-Kontext ist die Repository-Wurzel.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
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
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0 \
    TZ=Europe/Berlin
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=3 \
  CMD ["dotnet", "EstrichOne.Api.dll", "healthcheck"]
ENTRYPOINT ["dotnet", "EstrichOne.Api.dll"]
