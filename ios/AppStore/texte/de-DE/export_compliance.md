# Exportkonformität (Verschlüsselung)

In der App gesetzt: `ITSAppUsesNonExemptEncryption = NO` (`ios/EstrichONE/Config/Info.plist`). App Store Connect fragt
deshalb beim Build nicht mehr nach. Falls doch (z. B. ältere Builds), so beantworten:

| Frage | Antwort |
|---|---|
| Verwendet Ihre App Verschlüsselung? | Ja – nur die im Betriebssystem enthaltene (HTTPS/TLS, CryptoKit, Schlüsselbund) |
| Fällt die Verschlüsselung unter die Ausnahmen (Kategorie 5 Teil 2, Note 3 / Authentifizierung, Datenschutz)? | Ja, ausgenommen |
| Proprietäre oder nicht standardisierte Verschlüsselung? | Nein |
| Exportdokumente (CCATS) nötig? | Nein |
| Verfügbar in Frankreich – Erklärung nötig? | Nein (nur Standard-Krypto zum Datenschutz) |

Details und Krypto-Inventar: `ios/APPSTORE.md`, Abschnitt 5.
