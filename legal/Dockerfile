# Mini-Webserver für Impressum, Datenschutz und Support (App Store: Support- und Datenschutz-URL).
# Nur HTTP auf Port 8080 – TLS übernimmt der Reverse Proxy (z. B. Nginx Proxy Manager).
FROM caddy:2
COPY Caddyfile /etc/caddy/Caddyfile
COPY site/ /srv/
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s CMD wget -qO- http://127.0.0.1:8080/ >/dev/null || exit 1
