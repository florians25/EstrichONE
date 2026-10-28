# App-Datenschutz („Datenschutzangaben“) – Antworten für App Store Connect

Passend zu `ios/EstrichONE/Resources/PrivacyInfo.xcprivacy` (8 Datentypen, kein Tracking). App Store Connect › App-Datenschutz
› Datentypen bearbeiten. Datenschutzrichtlinie-URL: `privacy_url.txt` (Vorlage: `ios/AppStore/web/datenschutz.html`).

**Erfassen Sie oder Ihre Drittpartner Daten von dieser App?** → **Ja, wir erfassen Daten von dieser App**

Für **jeden** der folgenden Datentypen gleich beantworten:
- Verwendung: **App-Funktionalität** (sonst nichts anhaken: keine Analyse, keine Werbung, keine Produktpersonalisierung)
- Mit der Identität des Nutzers verknüpft: **Ja**
- Für Tracking verwendet: **Nein**

| Bereich in App Store Connect | Datentyp | Manifest-Schlüssel | Wofür |
|---|---|---|---|
| Kontaktinformationen | Name | `NSPrivacyCollectedDataTypeName` | Anzeigename des Benutzers (vom Betrieb angelegt) |
| Nutzerinhalte | Fotos oder Videos | `…PhotosorVideos` | Baustellenfotos und -videos in der Bauakte |
| Nutzerinhalte | Kundensupport | `…CustomerSupport` | Meldungen an die IT („Problem melden“) |
| Nutzerinhalte | Andere Nutzerinhalte | `…OtherUserContent` | Bautagebuch, Mängel, Protokolle, Unterschriften, Notizen, Chat |
| Kennungen | Nutzer-ID | `…UserID` | Benutzerkonto beim Server |
| Kennungen | Geräte-ID | `…DeviceID` | Geräteregistrierung (Gerätesperre, Poolgeräte), keine Werbe-ID |
| Standort | Genauer Standort | `…PreciseLocation` | nur auf Knopfdruck: Adresse einer Baustelle, Aufnahmeort eines Fotos |
| Diagnose | Andere Diagnosedaten | `…OtherDiagnosticData` | nur im freiwilligen Fehlerbericht (App-Version, Speicher, Abgleichstand) |

**Nicht** anhaken: E-Mail-Adresse, Telefonnummer, physische Adresse, Gesundheit, Fitness, Finanzinfos, Kontakte,
E-Mails oder Textnachrichten*, Audiodaten, Gameplay, Browser-/Suchverlauf, Käufe, Nutzungsdaten, Absturzdaten,
Leistungsdaten, sensible Daten, Werbedaten.

\* Der Chat ist unter „Andere Nutzerinhalte“ erfasst (Nachrichten zwischen Mitarbeitern des Betriebs auf dessen eigenem
Server). Wer es vorsichtiger möchte, hakt zusätzlich „Nutzerinhalte › E-Mails oder Textnachrichten“ mit denselben
Antworten an – dann aber auch `NSPrivacyCollectedDataTypeEmailsOrTextMessages` im Manifest ergänzen, damit beides übereinstimmt.

Ergebnis in der Produktseite: „Mit dir verknüpfte Daten“ (Kontaktinfos, Nutzerinhalte, Kennungen, Standort, Diagnose),
„Daten, die zum Tracking verwendet werden“: keine.
