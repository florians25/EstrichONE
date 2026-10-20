# EstrichONE-Installer: richtet die Suite mit einem `docker run` ein bzw. aktualisiert sie (siehe install.sh).
# Build-Kontext ist die Repository-Wurzel.
FROM docker:28-cli
ARG ESTRICHONE_VERSION=dev
ENV INSTALLER_VERSION=${ESTRICHONE_VERSION}
COPY deploy/compose.yml deploy/compose.proxy.yml docker/installer/compose.override.yml /installer/
COPY deploy/.env.example /installer/env.example
COPY docker/installer/install.sh /usr/local/bin/estrichone-install
ENTRYPOINT ["estrichone-install"]
