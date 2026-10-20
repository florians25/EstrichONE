#!/bin/sh
# EstrichONE Hosting-Konsole: ohne Argument die Konsole starten, mit „install“ den Installer.
set -eu
case "${1:-}" in
  install) shift; exec estrichone-hosting-install "$@" ;;
  healthcheck) exec dotnet /app/EstrichOne.Hosting.dll healthcheck ;;
  *) exec dotnet /app/EstrichOne.Hosting.dll "$@" ;;
esac
