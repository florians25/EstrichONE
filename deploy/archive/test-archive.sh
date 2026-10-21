#!/usr/bin/env bash
# Ende-zu-Ende-Test: Archivserver, Backup-Ziel und E-Mail gegen den echten Stack (CI, Workflow infra.yml).
# Erwartet gebaute Images estrichone/{api,backup,web,archive,backup-target}:$ESTRICHONE_VERSION.
# Prüft: Koppeln per Code, automatische Archivierung, Unveränderbarkeit, Löschsperre, Prüfprotokoll, Export mit
# Prüfskript, Sicherung auf ein gekoppeltes Backup-Ziel (nur anhängen), Testmail an Mailpit.
# shellcheck disable=SC1111,SC1112
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ $EUID -ne 0 ]]; then
  echo "Bitte mit Root-Rechten ausführen: sudo -E $0" >&2
  exit 2
fi

export ESTRICHONE_VERSION="${ESTRICHONE_VERSION:-dev}"
PASSWORD="Estrich trocknet langsam 7"
CA="$(mktemp)"
WORK="$(mktemp -d)"
CURL=(curl -sS --fail-with-body --noproxy '*' --cacert "$CA")
CURL_NOFAIL=(curl -sS --noproxy '*' --cacert "$CA")
NET=estrichone_frontend

log() { printf '\n== %s\n' "$*"; }
cleanup() {
  local status=$?
  if [[ $status -ne 0 ]]; then
    echo "Fehlgeschlagen (Status $status). Letzte Protokollzeilen:" >&2
    docker compose logs --tail 60 api backup >&2 2>&1 || true
    docker logs --tail 40 eo-test-archive >&2 2>&1 || true
    docker logs --tail 40 eo-test-target >&2 2>&1 || true
  fi
  docker rm -f eo-test-archive eo-test-target eo-test-mailpit >/dev/null 2>&1 || true
  docker volume rm eo-test-archive eo-test-target >/dev/null 2>&1 || true
  docker compose --profile restore down -v >/dev/null 2>&1 || true
  rm -rf data backup-repository ssh archive .env "$CA" "$WORK"
}
trap cleanup EXIT

json() { python3 -c "import sys,json; d=json.load(sys.stdin); print(eval('d'+sys.argv[1]))" "$1"; }
api() { "${CURL[@]}" "${AUTH[@]}" "$@"; }
ip_of() { docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' "$1"; }
code_of() {
  local code=""
  for _ in $(seq 1 60); do
    code="$(docker logs "$1" 2>&1 | sed -n 's/.*KOPPLUNGSCODE: *\([A-Z0-9-]*\).*/\1/p' | tail -n 1)"
    [[ -n "$code" ]] && break
    sleep 1
  done
  [[ -n "$code" ]] || { echo "Kein Kopplungscode von $1"; exit 1; }
  echo "$code"
}

log "Server einrichten"
cleanup
CA="$(mktemp)"
WORK="$(mktemp -d)"
./setup.sh --domain localhost --name "Archiv-Test" --yes >/dev/null
docker compose up -d --no-build --wait --wait-timeout 180 >/dev/null
docker compose exec -T caddy cat /data/caddy/pki/authorities/local/root.crt > "$CA"
AUTH=()
SETUP="$(docker compose logs api | grep -o 'Einrichtungscode: [A-Z0-9-]*' | tail -n 1 | awk '{print $2}')"
api -H 'Content-Type: application/json' -o /dev/null https://localhost/api/v1/setup -d "{\"setupCode\":\"$SETUP\",\"companyName\":\"Archiv-Test\",\"administrator\":{\"displayName\":\"Admin\",\"userName\":\"admin\",\"email\":null,\"password\":\"$PASSWORD\"}}"
TOKEN="$(api -H 'Content-Type: application/json' https://localhost/api/v1/auth/login -d "{\"login\":\"admin\",\"password\":\"$PASSWORD\",\"client\":\"web\",\"clientName\":\"test\"}" | json "['accessToken']")"
AUTH=(-H "Authorization: Bearer $TOKEN")

log "Archivserver, Backup-Ziel und Mailpit starten (jeweils eigener Container, wie auf eigener Hardware)"
docker run -d --name eo-test-archive --network "$NET" -v eo-test-archive:/data --read-only --tmpfs /tmp --cap-drop ALL \
  "estrichone/archive:$ESTRICHONE_VERSION" >/dev/null
docker run -d --name eo-test-target --network "$NET" -v eo-test-target:/data --read-only --tmpfs /tmp --cap-drop ALL \
  "estrichone/backup-target:$ESTRICHONE_VERSION" >/dev/null
docker run -d --name eo-test-mailpit --network "$NET" -p 18025:8025 axllent/mailpit:v1.27 >/dev/null
ARCHIVE_CODE="$(code_of eo-test-archive)"
TARGET_CODE="$(code_of eo-test-target)"

log "Archivserver koppeln (Adresse = Rechnername, falscher Code wird abgelehnt)"
WRONG="$("${CURL_NOFAIL[@]}" "${AUTH[@]}" -o /dev/null -w '%{http_code}' -H 'Content-Type: application/json' \
  https://localhost/api/v1/archive/pair -d '{"address":"eo-test-archive","code":"AAAA-BBBB-CCCC"}')"
[[ "$WRONG" == "422" ]] || { echo "Falscher Code nicht abgelehnt ($WRONG)"; exit 1; }
PAIRED="$(api -H 'Content-Type: application/json' https://localhost/api/v1/archive/pair -d "{\"address\":\"eo-test-archive\",\"code\":\"$ARCHIVE_CODE\"}")"
FP_SUITE="$(json "['fingerprintDisplay']" <<<"$PAIRED")"
FP_ARCHIVE="$(docker logs eo-test-archive 2>&1 | sed -n 's/.*Fingerabdruck: *//p' | tail -n 1)"
[[ "$FP_SUITE" == "$FP_ARCHIVE" ]] || { echo "Fingerabdruck weicht ab: $FP_SUITE / $FP_ARCHIVE"; exit 1; }

log "Rechnung hochladen → automatische Archivierung (Regel: Rechnungen sofort, 10 Jahre)"
BAUAKTE=01a15700-0000-7000-8000-00000000a0c1
api -H 'Content-Type: application/json' -o /dev/null https://localhost/api/v1/bauakten -d "{\"id\":\"$BAUAKTE\",\"title\":\"Archiv EFH\"}"
DOC="$WORK/rechnung.pdf"
printf '%%PDF-1.4\n%% Rechnung 4711\n1 0 obj<<>>endobj\ntrailer<<>>\n%%%%EOF\n' > "$DOC"
SHA="$(sha256sum "$DOC" | cut -d' ' -f1)"
FILE=01a15700-0000-7000-8000-0000000af11e
api -H 'Content-Type: application/json' -o /dev/null https://localhost/api/v1/files -d "{\"id\":\"$FILE\",\"bauakteId\":\"$BAUAKTE\",\"kind\":\"document\",\"category\":\"invoice\",\"originalName\":\"Rechnung-4711.pdf\",\"contentType\":\"application/pdf\",\"sizeBytes\":$(stat -c %s "$DOC"),\"sha256\":\"$SHA\"}"
api -X PUT -H 'Content-Type: application/octet-stream' --data-binary @"$DOC" -o /dev/null "https://localhost/api/v1/files/$FILE/content"
STATUS=""
for _ in $(seq 1 100); do
  STATUS="$(api "https://localhost/api/v1/archive/files/$FILE" | python3 -c "import sys,json; t=sys.stdin.read(); print(json.loads(t)['status'] if t.strip() else 'none')")"
  [[ "$STATUS" == "archived" ]] && break
  sleep 2
done
[[ "$STATUS" == "archived" ]] || { echo "Nicht archiviert (Status $STATUS)"; exit 1; }
RECORD="$(api "https://localhost/api/v1/archive/files/$FILE")"
echo "Archiviert: bis $(json "['retainUntil']" <<<"$RECORD"), Journal-Nr. $(json "['ledgerSeq']" <<<"$RECORD")"

log "Unveränderbarkeit und Löschsperre direkt am Archivserver (eigener Zugang per neuem Code)"
NEW_CODE="$(docker exec eo-test-archive estrichone-archive pair | sed -n 's/Kopplungscode: *\([A-Z0-9-]*\).*/\1/p')"
python3 - "$(ip_of eo-test-archive)" "$NEW_CODE" <<'PY'
import base64, hashlib, hmac, json, ssl, sys, urllib.request, uuid
host, code = sys.argv[1], sys.argv[2]
ctx = ssl.create_default_context(); ctx.check_hostname = False; ctx.verify_mode = ssl.CERT_NONE
base = f"https://{host}:8443"
def call(method, path, body=None, headers=None):
    req = urllib.request.Request(base + path, data=body, method=method, headers=headers or {})
    try:
        with urllib.request.urlopen(req, context=ctx) as r: return r.status, r.read()
    except urllib.error.HTTPError as e: return e.code, e.read()
_, hello = call("GET", "/pair/hello"); hello = json.loads(hello)
der = ssl.PEM_cert_to_DER_cert(hello["caCertificatePem"]); fp = hashlib.sha256(der).hexdigest()
alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"
key = hashlib.sha256("".join(c for c in code.upper() if c in alphabet).encode()).digest()
proof = lambda purpose, nonce: hmac.new(key, f"estrichone-pair-v1|{purpose}|{fp}|{nonce}".encode(), hashlib.sha256).hexdigest()
nonce = uuid.uuid4().hex
status, body = call("POST", "/pair/complete", json.dumps({"clientName": "CI", "nonce": nonce, "proof": proof("client", nonce), "host": host}).encode(), {"Content-Type": "application/json"})
assert status == 200, (status, body)
pair = json.loads(body); assert pair["serverProof"] == proof("server", nonce), "Server-Beweis falsch"
auth = {"Authorization": "Bearer " + pair["token"]}
oid = str(uuid.uuid4()); content = b"%PDF-1.4 Lieferschein"; other = b"%PDF-1.4 manipuliert"
meta = lambda: base64.b64encode(json.dumps({"name": "ls.pdf", "retentionYears": 6, "documentType": "delivery"}).encode()).decode()
def store(data): return call("POST", f"/api/v1/objects/{oid}", data, {**auth, "X-Archive-Sha256": hashlib.sha256(data).hexdigest(), "X-Archive-Metadata": meta()})
assert store(content)[0] == 201
assert store(content)[0] == 200, "Wiederholung nicht idempotent"
s, b = store(other); assert s == 409 and b"ARCHIVE_IMMUTABLE" in b, (s, b)
s, b = call("PUT", f"/api/v1/objects/{oid}", other, auth); assert s == 409, s
s, b = call("DELETE", f"/api/v1/objects/{oid}", None, auth); assert s == 409 and b"ARCHIVE_RETENTION_ACTIVE" in b, (s, b)
bad = hashlib.sha256(b"x").hexdigest()
s, b = call("POST", f"/api/v1/objects/{uuid.uuid4()}", content, {**auth, "X-Archive-Sha256": bad, "X-Archive-Metadata": meta()}); assert s == 422, s
s, b = call("GET", f"/api/v1/objects/{oid}", None, auth); assert s == 200 and b == content
s, b = call("GET", "/api/v1/status", None, {"Authorization": "Bearer falsch"}); assert s == 401, s
print("Unveränderbar, idempotent, Löschsperre aktiv, Prüfsumme erzwungen, fremder Zugang abgelehnt")
PY

log "Prüfprotokoll und Export für Betriebsprüfer"
REPORT="$(api -X POST https://localhost/api/v1/archive/verify)"
[[ "$(json "['ok']" <<<"$REPORT")" == "True" ]] || { echo "Prüfung nicht in Ordnung: $REPORT"; exit 1; }
api -o "$WORK/export.zip" https://localhost/api/v1/archive/export
(cd "$WORK" && mkdir export && cd export && python3 -m zipfile -e ../export.zip . && sha256sum -c sha256sums.txt && python3 journal_pruefen.py journal.jsonl)
grep -q "$SHA" "$WORK/export/sha256sums.txt" || { echo "Rechnung fehlt im Export"; exit 1; }
docker exec eo-test-archive estrichone-archive verify >/dev/null

log "Backup-Ziel koppeln (Adresse = IP) und dorthin sichern"
TARGET_IP="$(ip_of eo-test-target)"
TARGET="$(api -H 'Content-Type: application/json' https://localhost/api/v1/backup/targets -d "{\"address\":\"$TARGET_IP\",\"code\":\"$TARGET_CODE\"}")"
TARGET_ID="$(json "['id']" <<<"$TARGET")"
docker compose exec -T backup estrichone-backup run
TARGETS="$(api https://localhost/api/v1/backup/targets)"
[[ "$(json "[0]['lastRun']['status']" <<<"$TARGETS")" == "succeeded" ]] || { echo "Sicherung auf Backup-Ziel fehlt: $TARGETS"; exit 1; }
[[ "$(json "[0]['remote']['snapshots']" <<<"$TARGETS")" -ge 1 ]] || { echo "Backup-Ziel meldet keinen Snapshot: $TARGETS"; exit 1; }
docker compose exec -T backup estrichone-backup check 100%

log "Backup-Ziel ist nur-anhängen: Löschen eines Snapshots wird abgelehnt"
CONF="$(ls data/backup-status/targets/*.conf)"
REPOURL="$(sed -n "s/^TARGET_REPOSITORY='rest:\(.*\)'$/\1/p" "$CONF")"
SNAP="$(curl -sS --noproxy '*' --cacert "${CONF%.conf}.ca.pem" -H 'Accept: application/vnd.x.restic.rest.v2' "${REPOURL}snapshots/" | python3 -c "import sys,json; print(json.load(sys.stdin)[0]['name'])")"
DEL="$(curl -sS --noproxy '*' --cacert "${CONF%.conf}.ca.pem" -o /dev/null -w '%{http_code}' -X DELETE "${REPOURL}snapshots/$SNAP")"
[[ "$DEL" == "403" ]] || { echo "Snapshot ließ sich löschen ($DEL) – append-only wirkt nicht"; exit 1; }
api -o /dev/null "https://localhost/api/v1/backup/targets/$TARGET_ID/recovery"

log "E-Mail: Konto einrichten, Testmail an Mailpit"
api -X PUT -H 'Content-Type: application/json' -o /dev/null https://localhost/api/v1/mail/settings -d '{"enabled":true,"host":"eo-test-mailpit","port":1025,"security":"none","userName":null,"password":null,"clearPassword":false,"fromAddress":"buero@archiv-test.de","fromName":"Archiv-Test","replyTo":null}'
RESULT="$(api -H 'Content-Type: application/json' https://localhost/api/v1/mail/test -d '{"to":"chef@archiv-test.de"}')"
[[ "$(json "['ok']" <<<"$RESULT")" == "True" ]] || { echo "Testmail fehlgeschlagen: $RESULT"; exit 1; }
curl -sS --noproxy '*' http://localhost:18025/api/v1/messages | python3 -c "import sys,json; m=json.load(sys.stdin)['messages']; assert any(x['Subject']=='Testmail von EstrichONE' for x in m), m"

log "Archiv, Backup-Ziel und E-Mail erfolgreich geprüft"
