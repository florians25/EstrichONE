# 0007 – Caddy als Reverse Proxy

- **Status:** angenommen · **Datum:** 2026-10-19

## Kontext
Die Apps verlangen gültiges HTTPS. Betreiber sind oft keine IT-Fachleute.

## Entscheidung
Caddy 2 terminiert TLS und holt Zertifikate automatisch (Let's Encrypt). Für reine Testinstallationen
(`localhost`) verwendet Caddy seine interne CA. Nur Caddy öffnet Ports nach außen.

## Folgen
Keine manuelle Zertifikatsverwaltung. Für LAN-Server ohne öffentliche Domain ist eine DNS-Challenge oder eine
interne CA nötig (dokumentiert in `deploy/README.md`).
