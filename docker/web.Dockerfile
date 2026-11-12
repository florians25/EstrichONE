# syntax=docker/dockerfile:1
# EstrichONE – Weboberfläche und Reverse Proxy (ADR 0007).
# Stufe 1 baut die OpenUI5-Anwendung (UI5 Tooling, TypeScript, alle Themes self-hosted) und verpackt sie mit
# versionierten Pfaden (/ui-<hash>/…), Stufe 2 baut Caddy mit dem Rate-Limit-Plugin, Stufe 3 liefert die App mit Caddy
# aus und leitet /api an die API weiter.
# Basis-Images mit Digest gepinnt (Aktualisierung über Dependabot, Scan mit Trivy in .github/workflows/infra.yml).
FROM node:25-bookworm-slim@sha256:81db02c4b671288a03915da9534dbd54f96d0e7c24d80ccc54f5b36b2e684370 AS build
WORKDIR /src
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

# Caddy mit github.com/mholt/caddy-ratelimit (Rate-Limits je Client-Adresse, deploy/Caddyfile); Version fest.
FROM caddy:2.11.8-builder@sha256:d4eb070c36154bd2d4c94b40359a03c6168346d5af5404963c74691ab77fb63f AS caddy
RUN xcaddy build v2.11.7 --output /usr/bin/caddy \
      --with github.com/mholt/caddy-ratelimit@v0.1.1-0.20260612195517-5625512f24f6 \
 && /usr/bin/caddy list-modules | grep -q '^http.handlers.rate_limit$'

FROM caddy:2.11.8@sha256:4fcafa98afd1c2e642e353e3c5cf9ddbe892fcee14bc990e7ba2c5b78e51f9cf
# Sicherheitsupdates des Basissystems beim Bau einspielen (Alpine-Pakete).
RUN apk upgrade --no-cache
COPY --from=caddy /usr/bin/caddy /usr/bin/caddy
COPY --from=build /src/dist /srv
# Störungs-, Wartungs- und Update-Seite: /srv/stoerung.html (OpenUI5, web/webapp/stoerung.html) – Caddy bei 502/503/504,
# Service Worker ohne Verbindung.
COPY deploy/Caddyfile /etc/caddy/Caddyfile
# Caddyfile beim Bau prüfen (Syntax, Plugin vorhanden)
RUN ESTRICHONE_DOMAIN=localhost caddy adapt --config /etc/caddy/Caddyfile >/dev/null
