# EstrichONE <version>

<!-- Vorlage für Versionshinweise: als docs/releases/v<version>.md kopieren, <version> ersetzen, Neuerungen eintragen. -->

## Neu

- …

## Server aktualisieren

Bestehende Installation (ab 0.1.0 legt der Installer das Skript an):

```bash
sudo /opt/estrichone/update.sh            # neueste Version
sudo /opt/estrichone/update.sh <version>  # genau diese Version
```

Das Skript sichert vorher, aktualisiert, wartet auf alle Dienste und meldet Erfolg oder Fehler. Ältere Installationen
einmalig mit dem Installer-Befehl unten aktualisieren.

## Server neu installieren

```bash
sudo docker run --rm -it \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -v /opt/estrichone:/opt/estrichone \
  ghcr.io/florians25/estrichone-installer:<version>
```

## Archivserver und Backup-Ziel (weitere Rechner)

```bash
sudo docker run --rm -v /var/run/docker.sock:/var/run/docker.sock ghcr.io/florians25/estrichone-installer:<version> archiv
sudo docker run --rm -v /var/run/docker.sock:/var/run/docker.sock ghcr.io/florians25/estrichone-installer:<version> backup-ziel
```

Aktualisieren: denselben Befehl mit der neuen Version erneut ausführen. Koppeln in EstrichONE unter System → Archiv
bzw. System → Backup-Ziele.
