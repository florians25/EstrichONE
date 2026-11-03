#!/bin/sh
# EstrichONE Hosting-Konsole: ohne Argument die Konsole starten; install/reset-admin/uninstall = Installer;
# cli = Verwaltungsbefehl für den Server ausgeben (wird von hosting/install.sh nach /usr/local/bin installiert).
set -eu
case "${1:-}" in
  install|update|repair|reset-admin|uninstall) exec estrichone-hosting-install "$@" ;;
  cli) exec cat /opt/estrichone-hosting/estrichone-hosting ;;
  healthcheck) exec dotnet /app/EstrichOne.Hosting.dll healthcheck ;;
  *) exec dotnet /app/EstrichOne.Hosting.dll "$@" ;;
esac
