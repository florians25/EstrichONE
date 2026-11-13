# Microsoft Teams (Integration)

EstrichONE kann Unterhaltungen nach **Microsoft Teams** auslagern: je Bauakte ein **Teams-Chat** mit frei gewählten
Teilnehmern (Bauakten-Team, Kolonnen, Gruppen, Personen) bzw. ein **Kanal** im Team „Baustellen“, je **Kolonne** ein
Teams-Chat, **„In Teams öffnen“** in Web und Apps, Abgleich der Mitglieder und – optional –
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
  „Per Link verknüpfen …“ einfügen. „In Teams öffnen“ funktioniert dann sofort.
- **„Teams-Chat anlegen …“** (Bauakte) bzw. **„Teams-Chat“** (Kolonne) öffnet ohne App-Registrierung einen Gruppenchat per
  Deep Link direkt in Teams (Konto = E-Mail-Adresse). Er wird nicht gespeichert und lässt sich aus EstrichONE nicht löschen.
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
| `Chat.Create` | Anwendung | „Teams-Chat anlegen“ (Bauakte, Kolonne) | Gruppenchat mit den gewählten Teilnehmern anlegen |
| `ChatMember.ReadWrite.All` | Anwendung | Mitgliederabgleich der Chats | Mitglieder bei Änderungen an Bauakten-Team bzw. Kolonne ergänzen; entfernt nur von EstrichONE Hinzugefügte |
| `ChannelMessage.Send` | Delegiert | nur „Karten über Graph“ | Karten über das Dienstkonto in verknüpfte Kanäle senden |
| `ChatMessage.Send` | Delegiert | nur „Karten über Graph“ | Karten über das Dienstkonto in verknüpfte Gruppenchats senden |
| `offline_access` | Delegiert | nur „Karten über Graph“ | Dienstkonto angemeldet halten (Aktualisierungstoken, verschlüsselt) |
| `ChannelSettings.ReadWrite.All` | Anwendung | Archivieren | Kanal einer Bauakte archivieren bzw. wiederherstellen (von Hand oder „Beim Abschließen: archivieren“) |
| `Channel.Delete.All` | Anwendung | Löschen | Kanal einer Bauakte löschen |
| `Chat.ManageDeletion.All` | Anwendung | Löschen | Teams-Chat einer Bauakte bzw. Kolonne löschen (alternativ `Chat.ReadWrite.All`) |

`Chat.Create` deckt zusätzlich „Einzelchats über Microsoft Graph“ ab (Einstellung, Standard aus). Ohne die drei neuen
Berechtigungen funktioniert alles andere; Archivieren bzw. Löschen melden dann 424 `TEAMS_FORBIDDEN` mit dem Namen der
fehlenden Berechtigung. Der **Verbindungstest** liest die gewährten Anwendungsberechtigungen aus dem Zugriffstoken
(Anspruch `roles`) und zeigt im Schritt „Berechtigungen (Anwendung)“, welche optionalen fehlen und was dann nicht geht
(Hinweis, kein Fehler) – ohne etwas zu löschen oder anzulegen.

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

## Teams-Chats aus der Bauakte und je Kolonne (ab 1.0.24)

Bedienung (bewusst schlicht, SAP-Standard): Bauakte-Kopf **ein** Knopf (Split-MenuButton `akteTeamsButton`) – Standardaktion
„In Teams öffnen“ bzw. „Teams-Chat anlegen“; Menü: **In Teams öffnen**, **Teams-Chat anlegen …**, **Per Link
verknüpfen …**, **Teams-Chat löschen …** (`teams.delete`) bzw. **Verknüpfung lösen** (ohne dieses Recht). Mitgliederabgleich,
Archivieren und Karten laufen automatisch bzw. über die Einstellungen. Je Bauakte **ein** Chat (bzw. Kanal).
Kolonnen (Planung › Kolonnen): Knopf **Teams-Chat** (öffnen bzw. anlegen), **…** › **Teams-Chat löschen …**. Apps: nur
„In Teams öffnen“ (Bauakte, Mehr › Teams-Chat der eigenen Kolonne).

**Dialog „Teams-Chat anlegen“:** Name (vorbelegt „Bauaktennummer · Bauvorhaben“) und eine Teilnehmerauswahl
(MultiComboBox): „Bauakten-Team (Zuständige und Mitarbeiter)“ vorausgewählt, dazu Kolonnen, Gruppen (Benutzerrollen mit
Mitgliedern) und Personen. Die anlegende Person ist immer dabei. Hinweis nur bei Personen ohne Microsoft-Konto bzw. wenn es
für einen Gruppenchat zu wenige sind.

- **Mit App-Registrierung** (`Chat.Create`): `POST /chats` (`chatType: group`, `topic`, alle Mitglieder als `owner`),
  Verknüpfung mit der Bauakte. Die Teilnehmer werden als Quellen gespeichert (`memberSources`: `team`, `crew:{id}`,
  `role:{id}`, `user:{id}`); der Mitgliederabgleich folgt ihnen (Bauakten-Team geändert, Kolonne gespeichert → sofort im
  Hintergrund; sonst alle 10 Minuten bei geänderter Soll-Liste, täglich sicherheitshalber). Ältere Verknüpfungen ohne
  Quellen = Bauakten-Team.
- **Weniger als drei Personen mit Microsoft-Konto:** Microsoft legt Gruppenchats erst ab drei Mitgliedern an (zwei = Einzelchat).
  Der Server antwortet 422 `TEAMS_TOO_FEW_MEMBERS` mit verständlichem Text, die Oberfläche öffnet in diesem Fall gleich
  einen Einzel- bzw. Gruppenchat per Deep Link (nicht gespeichert).
- **Ohne App-Registrierung:** Deep Link `…/l/chat/0/0?users=…&topicName=…` (nicht gespeichert, nicht löschbar).
- **Erste Nachricht:** Ein per Graph angelegter Chat erscheint in der Teams-Chatliste der Mitglieder erst nach der ersten
  Nachricht. Mit verbundenem Dienstkonto („Karten über Graph“) schreibt EstrichONE „Chat „…“ wurde aus EstrichONE
  angelegt.“; sonst fordert der Erfolgsdialog auf, den Chat gleich zu öffnen (Knopf „In Teams öffnen“ – im Klick, damit
  der Browser das Fenster nicht blockiert).
- **Kein doppeltes Anlegen:** `POST /chats` und `POST /teams/{id}/channels` werden nur bei 429 wiederholt. Bei Zeitüberschreitung
  oder 503/504 antwortet die API 503 `TEAMS_TIMEOUT` („Möglicherweise wurde er trotzdem angelegt – bitte in Teams nachsehen
  und per Link verknüpfen“), statt einen zweiten Chat bzw. „Kanal existiert bereits“ zu erzeugen.
- **Kolonne:** `POST /teams/crews/{crewId}/chat` (Recht `planning.manage`): Thema „Kolonne Name“, Mitglieder = Kolonne mit
  Microsoft-Konto plus anlegende Person; Abgleich beim Speichern der Kolonne. Löschen `POST …/chat/delete` (`teams.delete`).

### Was vorher „nicht optimal“ war (1.0.23 ausgeblendet)

Gefunden im Code (ohne echten Tenant, nach Microsoft-Dokumentation): (1) Gruppenchat schon ab **zwei** Konten versucht –
Microsoft lehnt das ab, die Meldung war ein technischer 424-Fehler; (2) **Anlegen wurde bei Zeitüberschreitung bzw. 503/504
wiederholt** – doppelte Chats bzw. „Kanal existiert bereits“, obwohl der Kanal gerade angelegt worden war; (3) per Graph
angelegte Chats **erscheinen in Teams erst nach der ersten Nachricht** – es sah aus, als sei nichts passiert; (4) **ein
Dialog mit Auswahl Kanal/Gruppenchat/Link** und Schalter – unklar; Teilnehmer nur das Bauakten-Team; (5) Personen ohne
Microsoft-Konto fielen stillschweigend weg; (6) nach dem Anlegen öffnete sich Teams nicht (bzw. ein Fenster nach einer
Serveranfrage wird vom Browser blockiert); (7) gelöschte Kanäle blockieren ihren Namen 30 Tage – die Meldung sagte das nicht.

## Teams-Links in den Apps

Alle Teams-Links führen in die **Teams-App**, nicht in den Browser:

- **iOS:** 1. https-Link mit `UIApplication.open(…, options: [.universalLinksOnly: true])` (Universal Link → Teams-App, nie
  Safari), 2. `msteams:/l/…` (gleicher Pfad wie `teams.microsoft.com/l/…`, `LSApplicationQueriesSchemes` enthält `msteams`),
  3. Hinweis „Microsoft Teams installieren“ (App Store), Browser nur auf ausdrücklichen Wunsch. Vorher wurde der https-Link
  ohne `universalLinksOnly` geöffnet, wenn `msteams:` nicht griff – hatte jemand Teams-Links einmal in Safari geöffnet,
  merkt sich iOS das und öffnet Safari.
- **Android:** 1. `ACTION_VIEW` mit dem https-Link und `setPackage("com.microsoft.teams")`, 2. `msteams:`-Intent, 3. Google
  Play „Microsoft Teams installieren“. Vorher hing alles an einer Paketabfrage vorab: Schlug sie fehl (z. B. Teams nur im
  Arbeitsprofil) bzw. war Teams nicht Standard für teams.microsoft.com, öffnete der https-Link den Browser.
- Fehlt `appUrl` (ältere Server), bilden die Apps `msteams:` aus dem https-Link.

## Chats aus EstrichONE starten (Einzel- und Gruppenchats)

„Chat in Teams“ startet einen Einzel- bzw. Gruppenchat mit Kolleginnen und Kollegen. Standard ist ein **Teams-Deep-Link**
(keine Graph-Berechtigung nötig, funktioniert auch ohne App-Registrierung):

`https://teams.microsoft.com/l/chat/0/0?users=<upn1>,<upn2>&topicName=<Thema>&message=<Text>` – ein Empfänger: Einzelchat
(ein vorhandener wird geöffnet), mehrere: Gruppenchat (nur dann mit Thema). In den Apps zuerst `msteams:/l/chat/0/0?…`
(Teams-App), sonst der https-Link (Universal Link bzw. App-Link; ohne Teams-App im Browser). iOS fragt `msteams` über
`LSApplicationQueriesSchemes`, Android `com.microsoft.teams` über `<queries>` ab; fehlt die App, bieten die Apps
„Microsoft Teams installieren“ (App Store / Google Play) an.

- **Konto je Person:** zugeordneter Anmeldename (UPN). Ohne App-Registrierung bzw. solange noch nicht abgeglichen: die
  E-Mail-Adresse in EstrichONE (meist gleich dem UPN – nicht geprüft). Ist eine Person bei Microsoft **nicht gefunden**,
  erscheint bei ihr keine Teams-Aktion.
- **Optional über Microsoft Graph** (Einstellung „Einzelchats über Microsoft Graph anlegen“, `Chat.Create`): Einzelchats
  werden mit `POST /chats` (`chatType: oneOnOne`) angelegt – Microsoft gibt einen vorhandenen 1:1-Chat zurück, der Link ist
  stabil. Gruppenchats bleiben Deep Links, weil Graph bei jedem Aufruf einen **neuen** Gruppenchat anlegen würde.
- Wo: Bauakte › Kontakte › Team im Betrieb (Symbol je Person), Bauakte-Menü „Teams“ › „Chat mit dem ganzen
  Bauakten-Team“, Personal › Mitarbeiter (Symbol im Kopf), Kopfzeile › Teams-Menü › „Chat in Teams starten …“, Chat-Archiv
  „Neuer Chat in Teams“; Apps: Chat-Archiv „Neuer Chat in Teams“, Bauakte-Menü „Chat mit dem Team in Teams“.
- **Nicht** im Helpdesk/IT-Service: Tickets und Kommentare bleiben bewusst in EstrichONE.

## Archivieren, Löschen, Abschluss der Bauakte

Recht **`teams.delete`** (neu, Verwaltungsrecht mit Zwei-Faktor-Pflicht wie `settings.integrations`, ab Werk nur
Administrator). Im Bauakte-Menü „Teams“:

| Aktion | Kanal | Gruppenchat | Graph |
|---|---|---|---|
| **Archivieren** / **Wiederherstellen** | ja – nur lesen, in Teams wiederherstellbar | **nicht möglich** (Teams kennt kein Archiv für Chats) → 409 `TEAMS_NOT_SUPPORTED` | `POST /teams/{id}/channels/{id}/archive` bzw. `/unarchive` |
| **In Teams löschen** | ja – Teambesitzer können gelöschte Kanäle 30 Tage wiederherstellen | ja – vorläufig gelöscht, Wiederherstellung nur durch Microsoft-365-Admin | `DELETE /teams/{id}/channels/{id}` bzw. `DELETE /chats/{id}` |
| **Verknüpfung lösen** | ja (in Teams bleibt alles) | ja | – |

Löschen verlangt das **Eintippen der Bauaktennummer** (`confirmation`, sonst 422 `TEAMS_CONFIRMATION_MISMATCH`), löst danach
die Verknüpfung und wird protokolliert. Bei Microsoft bereits gelöschte Unterhaltungen (404) gelten als erledigt. Der
Standardkanal „Allgemein“ ist nicht löschbar. Per Link verknüpfte Kanäle ohne `groupId` lassen sich nur in Teams selbst
archivieren bzw. löschen.

**Beim Abschließen der Bauakte** (Einstellung, Standard **archivieren**, sonst „nichts“ oder „löschen“): Der
Hintergrundabgleich (alle 10 Minuten) archiviert bzw. löscht Unterhaltungen **abgeschlossener oder stornierter** Bauakten –
nur solche, die EstrichONE selbst angelegt hat. Wird die Bauakte wieder geöffnet, hebt er die Archivierung auf. Fehler
(z. B. fehlende Berechtigung) stehen an der Verknüpfung (`closeError`, Bauakte › Teams › Details); neuer Versuch nach einem
Tag. Bestehende Verknüpfungen werden beim Update nicht rückwirkend archiviert.

## Umstellung („alle zum Chatten nach Teams bringen“)

**Einstellungen › Integrationen › Microsoft Teams › Umstellung** (`settings.integrations`):

1. **Konten:** zugeordnet / bei Microsoft nicht gefunden / noch nicht geprüft / ohne E-Mail. Ob jemand die Teams-App
   tatsächlich nutzt, zeigt EstrichONE nicht (dafür wäre `Reports.Read.All` nötig) – siehe Teams Admin Center › Berichte.
2. **Alle offenen Bauakten ohne Teams-Verknüpfung verknüpfen:** startet einen **Hintergrundauftrag** (`202 Accepted`); die
   Seite fragt den Stand ab (`GET …/rollout/job`, alle 2 s). Kein langer HTTP-Aufruf hinter Cloudflare. Je Bauakte
   gelten die üblichen Fristen und Wiederholungen des Hintergrunds; grundsätzliche Fehler (keine Verbindung, Anmeldung,
   Berechtigung) brechen ab, Einzelfehler (z. B. Gruppenchat mit weniger als zwei Konten) werden aufgelistet. Höchstens ein
   Auftrag gleichzeitig; der Stand liegt im Speicher (nach Neustart weg – erneut starten macht beim Rest weiter).
3. **Hinweis „Ab sofort chatten wir in Teams“** (Text anpassbar): einmal auf der Startseite und in den Apps (Heute),
   wegklickbar und gemerkt, mit Link zum Installieren von Teams.

Im Chat-Modus **Microsoft Teams** führen alle Chat-Einstiege nach Teams: Kopfzeile (Teams-Menü: Teams öffnen, Chat in Teams
starten, Chat-Archiv), Navigation „Microsoft Teams“, Bauakte „In Teams öffnen“ statt Baustellen-Chat, Apps ohne
eingebaute Eingabe. Das Chat-Archiv bleibt lesbar.

## Rechte und Protokoll

- `settings.integrations` (neu, Verwaltungsrecht mit Zwei-Faktor-Pflicht, Standard nur Administrator): Einstellungen,
  Kontenzuordnung, Dienstkonto.
- `teams.delete` (neu, Verwaltungsrecht mit Zwei-Faktor-Pflicht, Standard nur Administrator): Kanäle archivieren,
  wiederherstellen, Kanäle und Gruppenchats in Teams löschen.
- Bauakte: sehen mit `bauakten.read` und Sichtbarkeit der Bauakte; verknüpfen, abgleichen, lösen und „Nachricht in Teams
  posten“ mit `bauakten.edit`.
- Chats starten (`GET /teams/people`, `POST /teams/chats`): `chat.use`.
- Protokoll: `teams.settings_saved`, `teams.chat_mode_changed`, `teams.connection_tested`, `teams.webhook_tested`,
  `teams.users_matched`, `teams.user_mapped`, `teams.sender_connected`, `teams.sender_disconnected`,
  `teams.bauakte_linked`, `teams.bauakte_unlinked`, `teams.members_synced`, `teams.bauakte_archived_in_teams`,
  `teams.bauakte_unarchived_in_teams`, `teams.bauakte_deleted_in_teams` (mit `trigger` manual/closed), `teams.crew_chat_created`,
  `teams.crew_chat_deleted`, `teams.message_posted`
  (nur Länge), `teams.link_all_started` – ohne Geheimnisse und ohne Nachrichteninhalte.

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
| `POST /bauakten/{id}/teams` | `bauakten.edit` | `{ kind: "groupChat", topic?, participants? }` bzw. `{ kind: "channel" }` anlegen oder `{ url }` verknüpfen |
| `GET /bauakten/{id}/teams/participants` | `bauakten.edit` | Auswahl für „Teams-Chat anlegen“: Bauakten-Team, Kolonnen, Gruppen, Personen (mit `upn`), `viaGraph`, `minMembers`, `topic` |
| `GET /teams/crews` | `planning.read` | Teams-Chats der Kolonnen, `viaGraph`, `canCreate`, `canDelete` |
| `POST /teams/crews/{crewId}/chat` | `planning.manage` | Teams-Chat der Kolonne anlegen (Graph) |
| `POST /teams/crews/{crewId}/chat/delete` | `teams.delete` | Teams-Chat der Kolonne in Teams löschen |
| `GET /teams/crews/mine` | angemeldet | Teams-Chats der eigenen Kolonnen (Apps) |
| `POST /bauakten/{id}/teams/sync`, `DELETE /bauakten/{id}/teams` | `bauakten.edit` | abgleichen, lösen (in Teams wird nichts gelöscht) |
| `POST /bauakten/{id}/teams/message` | `bauakten.edit` | `{ text }` als Karte in den Kanal/Chat (Webhook oder Dienstkonto) |
| `POST /bauakten/{id}/teams/archive`, `…/unarchive` | `teams.delete` | Kanal archivieren bzw. wiederherstellen |
| `POST /bauakten/{id}/teams/delete` | `teams.delete` | `{ confirmation: "<Bauaktennummer>" }` – in Teams löschen und Verknüpfung lösen |
| `GET /teams/people` | `chat.use` | Kollegen mit `upn` (bzw. `reason`, warum kein Chat möglich ist) |
| `POST /teams/chats` | `chat.use` | `{ userIds, topic?, message? }` → `{ webUrl, appUrl, viaGraph, included, missing }` |
| `GET /settings/integrations/teams/rollout` | `settings.integrations` | Stand der Umstellung |
| `POST …/rollout/link-all`, `GET/DELETE …/rollout/job` | `settings.integrations` | Massenverknüpfung starten (202), Stand, abbrechen |

Apps öffnen `appUrl` (`msteams:/l/…`), wenn die Teams-App installiert ist, sonst `webUrl` im Browser.
`GET /bauakten/{id}/teams` liefert zusätzlich `teamChat` (Deep Link für das ganze Bauakten-Team ohne die eigene Person),
`canDelete`, `canPost`, `confirmation` (Bauaktennummer) und `closeAction`; `GET /teams/status` liefert `rolloutNotice`,
`canStartChats`, `canDelete` und `teamsDownloadUrl`.

## Nicht geprüft (ohne echten Tenant)

Alles ist gegen den nachgebildeten Graph-Server getestet. Annahmen nach der Microsoft-Graph-Dokumentation, die mit einem
echten Mandanten zu bestätigen sind: Kanal-Archivierung über v1.0 (`…/channels/{id}/archive`, `ChannelSettings.ReadWrite.All`),
Löschen von Chats mit Anwendungsrecht `Chat.ManageDeletion.All` (bzw. `Chat.ReadWrite.All`), `roles` im App-Token, Verhalten
des Deep Links `…/l/chat/0/0?users=…` bei Gast- bzw. externen Konten, Mindestzahl drei Mitglieder für `POST /chats`
(`group`) mit Anwendungsrecht, Sichtbarkeit neuer Chats erst nach der ersten Nachricht, Öffnen von `msteams:/l/…` und
Universal/App Links `teams.microsoft.com/l/…` durch die Teams-Apps.

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
