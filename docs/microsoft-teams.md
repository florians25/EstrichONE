# Microsoft Teams (Integration)

EstrichONE kann Unterhaltungen nach **Microsoft Teams** auslagern: je Bauakte ein **Kanal** im Team „Baustellen“ oder ein
**Gruppenchat** mit dem Bauakten-Team, **„In Teams öffnen“** in Web und Apps, Abgleich der Mitglieder und – optional –
**Karten (Adaptive Cards)** bei neuen Mängeln, Abnahmen und Terminänderungen. Ohne Einrichtung bleibt alles wie bisher.

Einrichtung Schritt für Schritt (Entra Admin Center): Benutzerhandbuch Web, Kapitel 9 „Microsoft Teams“
(`docs/handbuch/kapitel/09-administration.md`, Abschnitt `#teams`).

## Chat-Modus

**Einstellungen › Integrationen › Microsoft Teams › Chat-Modus** (Recht `settings.integrations`):

| Modus | Eingebauter Chat | Teams |
|---|---|---|
| **EstrichONE-Chat** (Standard) | wie bisher | ausgeblendet |
| **Beides** | wie bisher | „Microsoft Teams“ in der Navigation, „In Teams öffnen“ an Bauakte, Startseite und in den Apps |
| **Microsoft Teams** | **Archiv**: lesen, gelesen markieren, stumm schalten, Anhang in Bauakte übernehmen, **exportieren** – nicht schreiben, keine neuen Chats | wie „Beides“; Navigation „Chat-Archiv“, Bauakte ohne Baustellen-Chat |

Im Modus „Microsoft Teams“ antworten schreibende Chat-Endpunkte mit **409 `CHAT_READ_ONLY`**. Es werden **keine Daten
gelöscht**; Löschfrist, Export (`GET /chat/export`, `GET /chat/admin/users/{id}/export`) und Löschung für
Betroffenenrechte arbeiten weiter. `GET /chat/status` liefert zusätzlich `mode` und `readOnly`.

## Ohne App-Registrierung

- **Verknüpfen per Link:** In Teams „Link zum Kanal abrufen“ bzw. beim Chat „Link kopieren“ und in der Bauakte unter
  „Mit Teams verknüpfen“ einfügen. „In Teams öffnen“ funktioniert dann sofort.
- **Karten per Teams-Workflows-Webhook:** In Teams beim Kanal „…“ › **Workflows** › Vorlage „Beim Empfang einer
  Webhookanforderung in einem Kanal posten“. Die erzeugte URL unter **Nachrichten › Webhook-URL** eintragen
  (verschlüsselt gespeichert, nur Hosts von Microsoft: `*.logic.azure.com`, `*.powerplatform.com`, `*.powerautomate.com`,
  `*.webhook.office.com`). Je Bauakte kann eine eigene URL hinterlegt werden (`webhookUrl` beim Verknüpfen).

## Mit App-Registrierung (Microsoft Graph)

Eigene App-Registrierung des Betriebs in Microsoft Entra ID (Einzelmandant). EstrichONE hat keinen eigenen Cloud-Dienst;
der Server spricht direkt mit `login.microsoftonline.com` und `graph.microsoft.com` (HTTPS, Port 443).

### Berechtigungen (minimal, mit Begründung)

| Berechtigung | Typ | Nötig | Wofür |
|---|---|---|---|
| `User.Read.All` | Anwendung | immer | Benutzer über E-Mail-Adresse bzw. UPN einem Microsoft-Konto zuordnen (liest nur ID, Name, E-Mail, UPN) |
| `Team.ReadBasic.All` | Anwendung | immer | Team „Baustellen“ prüfen, Name und Link anzeigen |
| `Channel.Create` | Anwendung | bei Art „Kanal“ | Kanal je Bauakte im Team „Baustellen“ anlegen |
| `TeamMember.ReadWrite.All` | Anwendung | bei „Kanal“ mit Mitgliederabgleich | Bauakten-Team ins Team „Baustellen“ aufnehmen (entfernt niemanden) |
| `Chat.Create` | Anwendung | bei Art „Gruppenchat“ | Gruppenchat je Bauakte mit dem Bauakten-Team anlegen |
| `ChatMember.ReadWrite.All` | Anwendung | bei „Gruppenchat“ mit Mitgliederabgleich | Mitglieder bei Teamänderungen ergänzen; entfernt nur von EstrichONE Hinzugefügte |
| `ChannelMessage.Send` | Delegiert | nur „Karten über Graph“ | Karten über das Dienstkonto in verknüpfte Kanäle senden |
| `ChatMessage.Send` | Delegiert | nur „Karten über Graph“ | Karten über das Dienstkonto in verknüpfte Gruppenchats senden |
| `offline_access` | Delegiert | nur „Karten über Graph“ | Dienstkonto angemeldet halten (Aktualisierungstoken, verschlüsselt) |

Warum delegiert für Nachrichten: Microsoft Graph erlaubt Anwendungen (ohne angemeldete Person) kein Senden in Kanäle
oder Chats (nur Datenimport). Deshalb sendet ein **Dienstkonto** (z. B. `estrichone@firma.de`, Teams-Lizenz nötig), das
einmalig per **Gerätecode** angemeldet wird. Wer das nicht möchte, nimmt den Workflows-Webhook. Keine Berechtigung zum
**Lesen** von Nachrichten: EstrichONE liest keine Teams-Inhalte.

Nicht gefragt werden u. a. `Group.ReadWrite.All`, `Directory.Read.All`, `ChannelMessage.Read.All`, `Chat.Read.All`.

### Anmeldung der App

- **Zertifikat (empfohlen):** PEM mit Zertifikat und privatem RSA-Schlüssel. Der öffentliche Teil wird in Entra ID
  hochgeladen; EstrichONE signiert damit eine Client-Assertion (RFC 7523). Fingerabdruck und Ablaufdatum werden angezeigt,
  30 Tage vorher erscheint ein Hinweis.
- **Geheimer Clientschlüssel:** Wert (nicht die Schlüssel-ID) eintragen; läuft nach höchstens 24 Monaten ab.

Schlüssel, Zertifikat, Webhook-URL, Aktualisierungstoken und der laufende Gerätecode liegen nur **verschlüsselt** in der
Datenbank (ASP.NET Data Protection, Zweck `EstrichOne.Teams.Secrets.v1`, Schlüsselring `data/files/.keys`, Teil der
Sicherung) und werden **nie** wieder ausgegeben (`hasClientSecret`, `certificateThumbprint`, `hasWebhook`, `webhookHost`).
Nach einer Wiederherstellung ohne Schlüsselring: Schlüssel neu eingeben (Meldung `TEAMS_SECRET_UNREADABLE`).

### Graph-Client

`Modules/Teams/GraphClient.cs` kapselt alle Aufrufe (kein Microsoft-SDK): Zugriffstoken je App-Registrierung
zwischengespeichert, **Wiederholung bei 429/503/504** mit `Retry-After` (höchstens `EstrichOne:Teams:MaxRetryDelay`,
Standard 30 s, `MaxAttempts` 4) bzw. exponentiellem Abstand, verständliche Fehler: fehlende Berechtigung (403 → Name
der Berechtigung), falscher Schlüssel (AADSTS-Codes), **Server ohne Internet** (DNS/Verbindung abgelehnt → 503
`TEAMS_OFFLINE` „Microsoft 365 ist vom Server aus nicht erreichbar … Ohne Internet bleibt der eingebaute Chat nutzbar“).
Tests laufen gegen einen nachgebildeten Graph-Server (`backend/tests/…/Support/FakeGraphServer.cs`), nie gegen einen
echten Tenant.

**Fristen.** Anfragen aus der Oberfläche (Verbindungstest, Zuordnung, Verknüpfen, Abgleich, Dienstkonto) haben für alle
Microsoft-Aufrufe zusammen eine Frist von 25 s (`InteractiveDeadline`), je Versuch 10 s (`InteractiveAttemptTimeout`) und
höchstens 2 Versuche (`InteractiveMaxAttempts`); danach antwortet die API mit 503 `TEAMS_TIMEOUT` („Microsoft 365 antwortet
nicht rechtzeitig …“). So bekommt die Oberfläche immer eine Antwort, bevor ein vorgeschalteter Proxy (Cloudflare 100 s)
abbricht. Der Hintergrundabgleich nutzt die längeren Werte (`AttemptTimeout` 30 s, `MaxAttempts` 4). Die Kontenzuordnung
fragt gebündelt (Graph-`$batch`, 20 Benutzer je Aufruf).

**Statuscodes.** Ablehnung durch Microsoft → 424 (`TEAMS_FORBIDDEN`, `TEAMS_AUTH_FAILED`, `TEAMS_GRAPH_ERROR` …), keine
Verbindung, Frist, Drosselung → 503, eigene Prüfungen → 409/422, Unerwartetes → 500 mit Fehlerkennung. **Nie 502/504:**
Cloudflare ersetzt solche Antworten des Servers durch eine eigene englische Fehlerseite („The origin web server returned
an invalid or incomplete response …“), der Grund ginge verloren.

**Fehlersuche.** Jeder fehlgeschlagene Microsoft-Aufruf steht mit Schritt, HTTP-Status und Graph- bzw. AADSTS-Code im
Log (ohne Geheimnisse, ohne E-Mail-Adressen):
`cd /opt/estrichone && sudo docker compose logs api --since 30m | grep -iE "teams|graph|entra|microsoft 365"`.

### Mitgliederabgleich (best effort)

Soll: Zuständige Person und Mitglieder der Bauakte mit zugeordnetem Microsoft-Konto (plus Dienstkonto bei „Karten über
Graph“). Abgleich sofort nach Teamänderung in EstrichONE, beim Verknüpfen, über „Mitglieder jetzt abgleichen“ und im
Hintergrund (alle 10 Minuten, wenn sich das Soll geändert hat, sonst täglich).

- **Gruppenchat:** fehlende Mitglieder werden hinzugefügt (mit Verlauf); entfernt werden nur Personen, die EstrichONE
  selbst hinzugefügt hat und die nicht mehr zum Team gehören. Von Hand in Teams Hinzugefügte bleiben.
- **Kanal (Standardkanal):** fehlende Personen werden ins Team „Baustellen“ aufgenommen; es wird niemand entfernt
  (das Team hat weitere Kanäle).
- Fehler (z. B. fehlende Berechtigung, Person ohne Microsoft-Konto) stehen an der Verknüpfung (`lastSyncError`) und in der
  Bauakte (Knopf „In Teams öffnen“ hervorgehoben, Menü „Details“).

### Karten bei Ereignissen

Neuer Mangel (Web, App/Sync), Abnahme unterschrieben bzw. vom Kunden abgelehnt, Ausführungszeitraum geändert, Einsatzplan
freigegeben bzw. geändert. Inhalt wie bei Pushnachrichten: Ereignis, Mangelnummer und -titel bzw. Tag/Uhrzeit/Tätigkeit,
Nummer und Titel der Bauakte, Link „In EstrichONE öffnen“ (nur mit `EstrichOne:PublicUrl`). **Nicht** enthalten: Namen,
Kommentare, Kundendaten, Preise, Fotos. Versand im Hintergrund; Fehler unter „Nachrichten › Letzte Karte“ und an der
Verknüpfung.

## Rechte und Protokoll

- `settings.integrations` (neu, Verwaltungsrecht mit Zwei-Faktor-Pflicht, Standard nur Administrator): Einstellungen,
  Kontenzuordnung, Dienstkonto.
- Bauakte: sehen mit `bauakten.read` und Sichtbarkeit der Bauakte; verknüpfen, abgleichen, lösen mit `bauakten.edit`.
- Protokoll: `teams.settings_saved`, `teams.chat_mode_changed`, `teams.connection_tested`, `teams.webhook_tested`,
  `teams.users_matched`, `teams.user_mapped`, `teams.sender_connected`, `teams.sender_disconnected`,
  `teams.bauakte_linked`, `teams.bauakte_unlinked`, `teams.members_synced` – ohne Geheimnisse und ohne Nachrichteninhalte.

## API (alle unter `/api/v1`)

| Methode und Pfad | Recht | Zweck |
|---|---|---|
| `GET /teams/status` | angemeldet | Chat-Modus, `chatReadOnly`, `available`, Team „Baustellen“ (Link), `canManage` |
| `GET/PUT /settings/integrations/teams` | `settings.integrations` | Einstellungen (Geheimnisse: `null` = unverändert, `clear…` = entfernen) |
| `POST /settings/integrations/teams/test` | `settings.integrations` | Verbindungstest mit Einzelschritten |
| `POST /settings/integrations/teams/webhook/test` | `settings.integrations` | Testkarte an den Webhook |
| `GET /settings/integrations/teams/users`, `POST …/users/match`, `PUT …/users/{userId}` | `settings.integrations` | Kontenzuordnung |
| `POST …/sender/start`, `POST …/sender/poll`, `DELETE …/sender` | `settings.integrations` | Dienstkonto (Gerätecode) |
| `GET /bauakten/{id}/teams` | `bauakten.read` | Verknüpfung mit `webUrl` (https) und `appUrl` (`msteams:`) |
| `POST /bauakten/{id}/teams` | `bauakten.edit` | `{ kind: "channel" \| "groupChat" }` anlegen oder `{ url }` verknüpfen |
| `POST /bauakten/{id}/teams/sync`, `DELETE /bauakten/{id}/teams` | `bauakten.edit` | abgleichen, lösen (in Teams wird nichts gelöscht) |

Apps öffnen `appUrl` (`msteams:/l/…`), wenn die Teams-App installiert ist, sonst `webUrl` im Browser.

## Datenschutzhinweis (Vorlage für den Betrieb)

> Für die Abstimmung zu Baustellen nutzen wir Microsoft Teams (Microsoft Ireland Operations Ltd., Auftragsverarbeitung
> nach Art. 28 DSGVO über die Microsoft-365-Verträge des Betriebs). EstrichONE übermittelt an Microsoft: die
> Zuordnung Ihres EstrichONE-Kontos zu Ihrem Microsoft-Konto (über Ihre dienstliche E-Mail-Adresse), Ihre Mitgliedschaft
> in Kanälen bzw. Gruppenchats der Bauakten, denen Sie zugeordnet sind, sowie – falls eingeschaltet – kurze Hinweise zu
> Ereignissen (z. B. „Neuer Mangel“ mit Mangeltitel, Bauaktennummer und -titel). Was Sie in Teams schreiben, verarbeitet
> Microsoft nach den Einstellungen Ihres Microsoft-365-Mandanten (Aufbewahrung, Speicherort). EstrichONE liest keine
> Nachrichten aus Teams. Der bisherige EstrichONE-Chat bleibt als Archiv lesbar und exportierbar und wird nach der
> eingestellten Löschfrist gelöscht. Rechtsgrundlage: § 26 BDSG bzw. Art. 6 Abs. 1 lit. b/f DSGVO; Betriebsvereinbarung
> zur Nutzung von Microsoft Teams (§ 87 Abs. 1 Nr. 6 BetrVG).

Hinweise für den Betrieb: Teams vor der Umstellung mit dem Betriebsrat regeln; Eintrag im Verzeichnis der
Verarbeitungstätigkeiten ergänzen (Empfänger Microsoft, Drittlandbezug nach EU-US Data Privacy Framework bzw.
EU Data Boundary); Beschäftigte informieren (obiger Text).
