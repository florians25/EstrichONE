# EstrichONE – Weboberfläche und Reverse Proxy (ADR 0007).
# Stufe 1 baut die OpenUI5-Anwendung (UI5 Tooling, TypeScript, alle Themes self-hosted) und verpackt sie mit
# versionierten Pfaden (/ui-<hash>/…), Stufe 2 liefert sie mit Caddy aus und leitet /api an die API weiter.
FROM node:24-bookworm-slim AS build
WORKDIR /src
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

FROM caddy:2
COPY --from=build /src/dist /srv
COPY deploy/Caddyfile /etc/caddy/Caddyfile
