# syntax=docker/dockerfile:1
# EstrichONE Backup-Dienst. Build-Kontext ist die Repository-Wurzel.
# Basis: offizielles PostgreSQL-17-Image (pg_dump passend zur Serverversion), dazu restic als einzelnes Binary.
# Basis-Images mit Digest gepinnt (Dependabot aktualisiert, Trivy prüft in .github/workflows/infra.yml).
FROM restic/restic:0.19.1@sha256:136600b6ff6843d61d355f7f71f460a166429f35de6fd11b568fece3c9a4d510 AS restic

FROM postgres:17.11-alpine@sha256:3540c0362c809df084ffac0ae69fe794515f9de92692f02178bcdce0ef0302ac
COPY --from=restic /usr/bin/restic /usr/local/bin/restic
# SSH-Client für SFTP-Sicherungsziele; curl und openssl für das Prüfen der CA eines Backup-Ziels (Wiederherstellung).
# Sicherheitsupdates des Basissystems; gosu (Rechtewechsel des Postgres-Einstiegs, mit altem Go gebaut) wird hier nicht
# gebraucht – der Dienst läuft fest als Benutzer backup.
RUN apk upgrade --no-cache && apk add --no-cache openssh-client curl openssl \
 && rm -f /usr/local/bin/gosu \
 && adduser -D -u 1654 -h /home/backup backup
COPY deploy/backup/estrichone-backup /usr/local/bin/estrichone-backup
ENV TZ=Europe/Berlin
USER backup
ENTRYPOINT ["estrichone-backup"]
CMD ["schedule"]
