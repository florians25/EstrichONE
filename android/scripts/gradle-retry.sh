#!/usr/bin/env bash
# Gradle mit Wiederholung: Downloads (Gradle-Distribution, Abhängigkeiten) scheitern gelegentlich mit 502/504.
# Nur Netzwerkfehler werden wiederholt; Kompilier- und Testfehler brechen sofort ab.
set -uo pipefail
cd "$(dirname "$0")/.."
for attempt in 1 2 3; do
  log="$(mktemp)"
  ./gradlew --no-daemon "$@" 2>&1 | tee "$log"
  status=${PIPESTATUS[0]}
  if [[ $status -eq 0 ]]; then exit 0; fi
  if ! grep -qiE 'Could not (resolve|download|GET|HEAD)|Read timed out|Connection reset|status code 50[234]|response code: 50[234]|Downloading https://services.gradle.org.*(failed|Exception)|java.net.(Socket|Connect)' "$log"; then
    exit $status
  fi
  echo "::warning::Netzwerkfehler beim Gradle-Lauf (Versuch $attempt) – neuer Versuch in $((attempt * 30)) s"
  sleep $((attempt * 30))
done
exit 1
