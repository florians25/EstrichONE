# syntax=docker/dockerfile:1
# EstrichONE Backup-Dienst. Build-Kontext ist die Repository-Wurzel.
# Basis: offizielles PostgreSQL-17-Image (pg_dump passend zur Serverversion), dazu restic als einzelnes Binary.
FROM restic/restic:0.19.1 AS restic

FROM postgres:17-alpine
COPY --from=restic /usr/bin/restic /usr/local/bin/restic
# SSH-Client für SFTP-Sicherungsziele; curl und openssl für das Prüfen der CA eines Backup-Ziels (Wiederherstellung).
RUN apk add --no-cache openssh-client curl openssl \
 && adduser -D -u 1654 -h /home/backup backup
COPY deploy/backup/estrichone-backup /usr/local/bin/estrichone-backup
ENV TZ=Europe/Berlin
USER backup
ENTRYPOINT ["estrichone-backup"]
CMD ["schedule"]
