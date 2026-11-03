# syntax=docker/dockerfile:1
# EstrichONE – Weboberfläche und Reverse Proxy (ADR 0007).
# Stufe 1 baut die OpenUI5-Anwendung (UI5 Tooling, TypeScript, alle Themes self-hosted) und verpackt sie mit
# versionierten Pfaden (/ui-<hash>/…), Stufe 2 baut Caddy mit dem Rate-Limit-Plugin, Stufe 3 liefert die App mit Caddy
# aus und leitet /api an die API weiter.
# Basis-Images mit Digest gepinnt (Aktualisierung über Dependabot, Scan mit Trivy in .github/workflows/infra.yml).
FROM node:24-bookworm-slim@sha256:c86ecead9e55a5210ac1727de38a4ca840bf03dcaf34dc9749addbf383b1920c AS build
WORKDIR /src
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

# Caddy mit github.com/mholt/caddy-ratelimit (Rate-Limits je Client-Adresse, deploy/Caddyfile); Version fest.
FROM caddy:2.11.7-builder@sha256:c4cd20eaa0b69112fd5d4f721ffc9671687e1d5d0b155c8620cf7546dbef94c0 AS caddy
RUN xcaddy build v2.11.7 --output /usr/bin/caddy \
      --with github.com/mholt/caddy-ratelimit@v0.1.1-0.20260612195517-5625512f24f6 \
 && /usr/bin/caddy list-modules | grep -q '^http.handlers.rate_limit$'

FROM caddy:2.11.7@sha256:36c24ad91a7ff7bc4c87fe22bfa8df3c4b18da0773d73fc12ee2c52b98c0bb7e
# Sicherheitsupdates des Basissystems beim Bau einspielen (Alpine-Pakete).
RUN apk upgrade --no-cache
COPY --from=caddy /usr/bin/caddy /usr/bin/caddy
COPY --from=build /src/dist /srv
# Störungs- und Wartungsseite (Caddy bei 502/503/504, Service Worker ohne Verbindung)
COPY deploy/fallback/ /srv/
COPY deploy/Caddyfile /etc/caddy/Caddyfile
# Caddyfile beim Bau prüfen (Syntax, Plugin vorhanden)
RUN ESTRICHONE_DOMAIN=localhost caddy adapt --config /etc/caddy/Caddyfile >/dev/null
