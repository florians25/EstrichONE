# syntax=docker/dockerfile:1
# EstrichONE Backup-Dienst. Build-Kontext ist die Repository-Wurzel.
# Basis: offizielles PostgreSQL-17-Image (pg_dump passend zur Serverversion), dazu restic als einzelnes Binary.
# Basis-Images mit Digest gepinnt (Dependabot aktualisiert, Trivy prüft in .github/workflows/infra.yml).
FROM restic/restic:0.20.0@sha256:93999df80d4ac6cb148e82d2ef1fe6a81996f17ff535e772723538211b31555a AS restic

FROM postgres:17.11-alpine@sha256:e5d2edd28b1f8d40a0a6c0c9cdc545b19b56a0d5d88f94bc11a97d970d4700d0
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
