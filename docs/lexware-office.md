# Lexware Office (Public API): technische Notiz

Stand: November 2026, Modul `Lexware` (Backend `backend/src/EstrichOne.Api/Modules/Lexware`, Web `web/webapp/util/lexware*.ts`).

Rechnungen und Angebote entstehen ausschließlich in Lexware Office (früher lexoffice), Bauunterlagen liegen in EstrichONE.
EstrichONE verknüpft Kunden mit Lexware-Kontakten, gleicht spätere Änderungen an verknüpften Kunden in beide Richtungen
ab und zeigt Rechnungen und Angebote am Kunden und an der Bauakte an (Nummer, Art, Datum, Betrag, Status, PDF).

**Grundsatz: Belege sind rein lesend.** EstrichONE legt in Lexware Office keinen Beleg an, ändert keinen, schließt keinen
ab und löscht nichts. Geschrieben wird in Lexware Office ausschließlich an Kontakten: beim Anlegen (`POST /v1/contacts`,
immer nach Dublettenprüfung: von Hand mit ausdrücklicher Entscheidung, automatisch nur ohne jeden Treffer) und beim
Abgleich geänderter Kundendaten
(`PUT /v1/contacts/{id}`, nur für verknüpfte Kunden mit eingeschaltetem Abgleich). Kontakte werden dort nie archiviert
oder gelöscht. Abgesichert ist das an drei Stellen:

- `LexwareClient` hat für Belege nur lesende Methoden (`GetVoucherAsync`, `ListVouchersAsync`, `GetVoucherDocumentAsync`).
- `LexwareClient.WriteAllowed` weist vor dem Senden jeden Aufruf ab, der nicht `GET`, `POST /v1/contacts` oder
  `PUT /v1/contacts/{id}` ist.
- Tests prüfen die Liste der öffentlichen Methoden des Clients, die Sperre (auch für `/file`, `/document`, `/v1/files`
  und `DELETE` auf Kontakte) und dass über alle Abläufe hinweg kein schreibender Belegaufruf abgesetzt wird; die
  nachgebildete API kennt keine schreibenden Belegendpunkte.

Die Zuordnung eines Belegs zu einer Bauakte liegt nur in EstrichONE (Tabelle `lexware_voucher_links`).

Die öffentliche Dokumentation (https://developers.lexware.io/docs/) war beim Bau aus der Entwicklungsumgebung nicht
erreichbar, auch nicht bei der Erweiterung um Änderungsabgleich und Beleg-PDF. Endpunkte und Feldnamen sind nach bestem
Wissen umgesetzt und stehen gebündelt in `LexwareClient.cs`; der Abschnitt „Gegen ein echtes Konto zu prüfen“ nennt, was
vor dem Einsatz zu bestätigen ist.

## Aufbau

| Datei | Inhalt |
|---|---|
| `LexwareClient.cs` | HTTP-Zugang: Bearer-Schlüssel, Drosselung, Wiederholung, Zeitlimit, Fehlertexte, alle Feldnamen der API |
| `LexwareMatching.cs` | Dublettenprüfung als reine Funktionen (Normalisierung, Gründe, Bewertung) |
| `LexwareService.cs` | Einstellungen, Schlüssel (Data Protection), Kontaktliste und Belege mit Cache, Verknüpfung, Statusabgleich, Beleg-PDF |
| `LexwareContactSync.cs` | Änderungsabgleich als reine Funktionen: Felder beider Seiten, Vergleich, Dreiwege-Abgleich, Übernahme in den Kunden |
| `LexwareChangeSync.cs` | Änderungsabgleich je Verknüpfung, Lauf über alle Verknüpfungen, Hintergrunddienst |
| `LexwareAutoCreate.cs` | Neue Kunden ohne ähnlichen Kontakt automatisch anlegen, Vermerke in `lexware_contact_creations` |
| `LexwareEndpoints.cs` | Endpunkte, Rechte, Fehlerabbildung auf Problem-Antworten |
| `LexwareModel.cs` | Tabellen `lexware_settings`, `lexware_contact_links` (mit Abgleichstand), `lexware_voucher_links` |

Einstellungen in `appsettings` unter `EstrichOne:Lexware` (`LexwareOptions`): erlaubte Hosts der Basis-URL,
Mindestabstand der Aufrufe, Versuche, Wartezeiten, Zeitlimit, Cache-Dauern, Seitengröße der Kontaktliste, Adresse der
Lexware-Oberfläche (`AppBaseUrl`), Abstände des Änderungsabgleichs (`ChangePollInterval` 30 s, `ChangeSyncInterval`
15 min, `ChangeSyncPause` 5 min), Versuche bei Versionskonflikt (`UpdateAttempts` 3) und der Weg zum Beleg-PDF
(`VoucherDocumentRoute`: `Auto`, `File`, `Document`).

## Verwendete Endpunkte von Lexware Office

Basis-URL `https://api.lexware.io` (früher `https://api.lexoffice.io`, weiterhin einstellbar). Kopfzeilen:
`Authorization: Bearer <Schlüssel>`, `Accept: application/json`, bei schreibenden Aufrufen `Content-Type: application/json`.

| Aufruf | Zweck | Gelesene bzw. gesendete Felder |
|---|---|---|
| `GET /v1/profile` | Verbindung testen | `organizationId`, `companyName` |
| `GET /v1/contacts?customer=true&page=&size=` | Kontaktliste für Dublettenprüfung, Übernahme und Abgleichübersicht | Seite: `content`, `last`, `totalPages`, `totalElements` |
| `GET /v1/contacts?customer=true&name=` bzw. `email=`, `number=` | gezielte Suche im Dialog „Kontakt suchen und verknüpfen“ | wie oben |
| `GET /v1/contacts/{id}` | Kontakt vor dem Verknüpfen, nach dem Anlegen und vor jedem Ändern lesen | `id`, `version`, `updatedDate`, `roles.customer.number`, `company.name`, `company.vatRegistrationId`, `company.taxNumber`, `person.salutation`, `person.firstName`, `person.lastName`, `addresses.billing[0].street/zip/city` (ersatzweise `shipping`), `emailAddresses.*[]`, `phoneNumbers.*[]` (ohne `fax`), `archived` |
| `PUT /v1/contacts/{id}` | Abgeglichene Felder eines verknüpften Kontakts ändern | Der zuvor mit `GET /v1/contacts/{id}` frisch gelesene Kontakt mit seiner `version`, geändert nur in `company.name` bzw. `person.firstName/lastName`, `person.salutation`, `company.vatRegistrationId`, `addresses.billing[0].street/zip/city`, dem passenden Eintrag unter `emailAddresses.*[]` bzw. `phoneNumbers.*[]` (ohne `fax`). Nicht mitgesendet: `id`, `organizationId`, `createdDate`, `updatedDate`, `archived`. Antwort: `version`, `updatedDate`. `409` = Versionskonflikt |
| `POST /v1/contacts` | Kunde in Lexware anlegen | `version: 0`, `roles: {customer: {}}`, `company: {name, vatRegistrationId, taxNumber}` oder `person: {salutation, firstName, lastName}`, `addresses.billing[{street, zip, city, countryCode: "DE"}]`, `emailAddresses.business[]`, `phoneNumbers.business[]` und `mobile[]`, `note`. Antwort: `id` |
| `GET /v1/voucherlist?voucherType=invoice,quotation,creditnote,orderconfirmation&voucherStatus=any&sort=voucherDate,DESC&size=100&contactId=` | Belege eines Kunden | `content[].id`, `voucherType`, `voucherStatus`, `voucherNumber`, `voucherDate`, `dueDate`, `totalAmount`, `currency`, `contactId`, `contactName` |
| dieselbe Abfrage mit `voucherNumber=` statt `contactId=` | Beleg über die Nummer finden | wie oben |
| `GET /v1/invoices/{id}`, `GET /v1/quotations/{id}`, `GET /v1/credit-notes/{id}`, `GET /v1/order-confirmations/{id}` | Status und Beträge abgleichen, Texte für den Zuordnungsvorschlag | `voucherStatus` (draft, open, paid, paidoff, voided; Angebote auch accepted, rejected), `voucherNumber`, `voucherDate`, `dueDate` bzw. `expirationDate` (ersatzweise `voucherDate` plus `paymentConditions.paymentTermDuration`), `totalPrice.totalNetAmount/totalGrossAmount/currency`, `title`, `introduction`, `remark`, `address.contactId/name` |

| `GET /v1/{invoices\|quotations\|credit-notes\|order-confirmations}/{id}/file` mit `Accept: application/pdf` | Beleg als PDF (bevorzugter Weg) | PDF-Daten; `406` bei Entwürfen |
| `GET /v1/{belegart}/{id}/document`, danach `GET /v1/files/{documentFileId}` mit `Accept: application/pdf` | Beleg als PDF, älterer Weg (als veraltet gekennzeichnet) | `documentFileId`, dann PDF-Daten |

Lexware liefert Zeitpunkte mit Zeitzone (`2023-02-22T00:00:00.000+01:00`); maßgeblich ist das Datum in dieser Zone.
„Überfällig“ ist kein Status der Rechnung: EstrichONE leitet ihn aus Status `open` und überschrittener Fälligkeit ab (in
der Belegliste liefert Lexware `overdue` selbst; gespeichert wird `open`).

Links in die Lexware-Oberfläche: `https://app.lexware.de/permalink/{invoices|quotations|credit-notes|order-confirmations}/view/{id}`
(Entwürfe `.../edit/{id}`), `.../permalink/contacts/view/{id}`. Bei der früheren Basis-URL `https://app.lexoffice.de`. Über
`EstrichOne:Lexware:AppBaseUrl` fest einstellbar.

## Drosselung, Zeitlimit, Fehler

- Höchstens zwei Anfragen je Sekunde: ein gemeinsamer Takt für den ganzen Server (`MinRequestInterval`, Vorgabe 550 ms).
- `429`: Wiederholung mit wachsender Wartezeit (1 s, 2 s, 4 s, höchstens 8 s; `Retry-After` wird beachtet), insgesamt
  vier Versuche. Das gilt auch für schreibende Aufrufe, weil Lexware die Anfrage dann nicht verarbeitet hat.
- `502`, `503`, `504`, Netzwerkfehler und Zeitüberschreitung: Wiederholung nur bei lesenden Aufrufen. Das Anlegen eines
  Kontakts wird nicht wiederholt, damit kein Kontakt doppelt entsteht. Das Ändern eines Kontakts wiederholt der
  Hintergrunddienst im nächsten Lauf (mit frisch gelesener Version).
- `409` beim Ändern eines Kontakts: neu lesen, neu entscheiden, erneut schreiben, insgesamt drei Versuche.
- Zeitlimit je Versuch 15 Sekunden.
- Der Schlüssel erscheint in keinem Log und keiner Meldung. Die Standardprotokollierung des HttpClient ist abgeschaltet,
  protokolliert wird nur der Pfad ohne Abfrage.

| Lexware antwortet | Meldung an die Oberfläche | Code, HTTP-Status der EstrichONE-API |
|---|---|---|
| 401 | API-Schlüssel abgelehnt | `LEXWARE_UNAUTHORIZED`, 424 |
| 402, 403 | Public API im Tarif nicht enthalten oder keine Berechtigung | `LEXWARE_NO_API_ACCESS`, 424 |
| 404 | Eintrag in Lexware nicht gefunden | `LEXWARE_NOT_FOUND`, 424 |
| 400, 406, 422 | Angaben nicht angenommen, mit Einzelheiten aus `details[]` bzw. `IssueList[]` | `LEXWARE_INVALID`, 424 |
| 409 | Konflikt | `LEXWARE_CONFLICT`, 424 |
| 406 beim Beleg-PDF, kein PDF in der Antwort | Für Entwürfe stellt Lexware Office kein PDF bereit | `LEXWARE_NO_DOCUMENT`, 409 |
| 429 nach allen Versuchen | im Moment keine weiteren Anfragen | `LEXWARE_RATE_LIMITED`, 503 |
| 5xx, kein Netz, Zeitüberschreitung | nicht verfügbar bzw. nicht erreichbar | `LEXWARE_UNAVAILABLE`, `LEXWARE_OFFLINE`, `LEXWARE_TIMEOUT`, 503 |
| kein Schlüssel hinterlegt | noch nicht eingerichtet | `LEXWARE_NOT_CONFIGURED`, 409 |
| Modul ausgeschaltet | Modul deaktiviert | `MODULE_DISABLED`, 409 |

## Endpunkte von EstrichONE (`/api/v1`)

| Aufruf | Recht | Zweck |
|---|---|---|
| `GET lexware/status` | angemeldet | Modul an, Schlüssel hinterlegt, eigene Rechte |
| `GET`, `PUT settings/integrations/lexware` | `settings.integrations` | Einstellungen; der Schlüssel wird nie zurückgegeben (`hasApiKey`) |
| `POST settings/integrations/lexware/test` | `settings.integrations` | Verbindung testen, Firmenname der Organisation |
| `GET settings/integrations/lexware/reconciliation` | `settings.integrations` | Abgleichübersicht mit `deviationItems` (`refresh=true` lädt die Kontaktliste neu und gleicht die Änderungen aller Verknüpfungen ab) |
| `POST settings/integrations/lexware/reconciliation/link-unique` | `settings.integrations` | eindeutige Treffer aus der Vorschau verknüpfen |
| `GET lexware/customers/{id}` | `invoices.read` | Verknüpfung eines Kunden mit Stand des Änderungsabgleichs (`sync`: Zustand, letzter Abgleich, Abweichungen) |
| `GET lexware/customers/{id}/candidates` | `invoices.assign` | Dublettenprüfung in Lexware, mit `q` gezielte Suche |
| `POST lexware/customers/{id}/link` | `invoices.assign` | mit vorhandenem Lexware-Kontakt verknüpfen |
| `POST lexware/customers/{id}/create` | `invoices.assign` | in Lexware anlegen; bei Treffern nur mit `createAnyway` |
| `DELETE lexware/customers/{id}/link` | `invoices.assign` | Verknüpfung lösen |
| `PUT lexware/customers/{id}/sync` | `invoices.assign` | „Mit Lexware Office abgleichen“ ein- oder ausschalten |
| `POST lexware/customers/{id}/create/retry` | `invoices.assign` | automatische Anlage erneut versuchen (prüft vorher erneut auf Dubletten) |
| `POST lexware/contacts/check` | `contacts.edit` | beim Anlegen eines Kunden prüfen, ob es ihn in Lexware Office schon gibt; blockiert nie |
| `POST lexware/customers/{id}/sync/run` | `invoices.assign` | Änderungsabgleich für diesen Kunden jetzt ausführen |
| `POST lexware/customers/{id}/sync/resolve` | `invoices.assign`, für „Lexware übernehmen“ zusätzlich `contacts.edit` | Abweichungen entscheiden: `source` für alle Felder oder `fields` je Feld, jeweils `estrichOne` oder `lexware` |
| `GET lexware/customers/{id}/vouchers/{lexwareId}/document` | `invoices.read` | Beleg des verknüpften Kontakts als PDF (`download=true` als Anhang) |
| `GET bauakten/{id}/lexware/vouchers/{linkId}/document` | `invoices.read` | der Bauakte zugeordneter Beleg als PDF (`download=true` als Anhang) |
| `POST settings/integrations/lexware/reconciliation/enable-sync` | `settings.integrations` | Abgleich für ausgewählte Kunden einschalten; mit `preview=true` nur die Zahlen (`wouldCreate`, `withMatches`) |
| `GET lexware/contacts` | `invoices.assign` | Lexware-Kunden seitenweise mit Suche (`q`, `page`, `size`, `unlinked`) |
| `GET lexware/contacts/{lexwareId}/candidates` | `invoices.assign` | Dublettenprüfung in EstrichONE |
| `POST lexware/contacts/{lexwareId}/import` | `invoices.assign` und `contacts.edit` | als Kunde übernehmen; bei Treffern nur mit `createAnyway` |
| `GET lexware/customers/{id}/vouchers` | `invoices.read` | Rechnungen, Angebote, Gutschriften und Auftragsbestätigungen des verknüpften Kontakts (`refresh=true` lädt neu) |
| `GET bauakten/{id}/lexware` | `invoices.read` | der Bauakte zugeordnete Belege mit Statusabgleich (`refresh=true` sofort), dazu `contact` (Lexware-Seite des verknüpften Kunden aus dem gemerkten Abgleichstand, Abgleichstand, abweichende Felder) und `summary` (Summenzeile der Belege) |
| `GET bauakten/{id}/lexware/candidates` | `invoices.assign` | Belege zum Zuordnen: Belege des Kunden der Bauakte mit Vorschlag, mit `number` Suche über die Belegnummer |
| `POST bauakten/{id}/lexware/vouchers/assign` | `invoices.assign` | Beleg zuordnen (`lexwareId`, `type`); schreibt nur in EstrichONE |
| `DELETE bauakten/{id}/lexware/vouchers/{linkId}` | `invoices.assign` | Zuordnung lösen |

Einen Endpunkt, der in Lexware Office einen Beleg anlegt oder ändert, gibt es nicht.

Bis auf Status und Einstellungen verlangen alle Endpunkte das eingeschaltete Modul `lexware` (Systemeinstellungen,
Module; nach der Installation aus). Die Rechte `invoices.read` (Belege sehen) und `invoices.assign` (Belege zuordnen,
Kunden verknüpfen) erhält die Rolle „Büro und
Bauleitung“ nur bei neuen Installationen; in bestehenden Installationen vergibt sie die Administration.

## Schalter „Mit Lexware Office abgleichen“ (einseitige Kunden)

- Feld `lexwareSync` am Kunden (`contacts.lexware_sync`, `ContactResponse.lexwareSync`). Aus = der Kunde bleibt nur in
  EstrichONE: `link` und `create` antworten mit 409 `LEXWARE_SYNC_DISABLED`. In der Abgleichübersicht steht er unter `excludedItems`, nicht unter `onlyEstrichOneItems`, und
  wird nicht als Dublette vorgeschlagen.
- Schreiben: `POST contacts` und `PUT contacts/{id}` mit `lexwareSync` (fehlt das Feld: beim Anlegen die Vorgabe, beim
  Ändern unverändert), Sync-Operationen `contact.create` und `contact.update` mit `lexwareSync`, dazu
  `PUT lexware/customers/{id}/sync` (`invoices.assign`) und die Massenaktion
  `POST settings/integrations/lexware/reconciliation/enable-sync`.
- Vorgabe für neue Kunden: Einstellung `newCustomersSync` (Vorgabe ja), wirksam nur bei eingeschaltetem Modul. Clients
  lesen die wirksame Vorgabe aus `GET settings/modules` (`lexwareSyncDefault`, laden die Apps ohnehin) bzw.
  `GET lexware/status` (`syncDefault`).
- Migration: bestehende Kunden erhalten `false`. Solange das Modul aus ist, erhalten auch neue Kunden `false`. So wird
  kein Kunde ungefragt für die Übertragung vorgemerkt.
- Eine bestehende Verknüpfung bleibt beim Ausschalten erhalten und wird nur bewusst gelöst: Der Kontakt und seine
  Belege liegen bereits in Lexware Office, ein stilles Lösen würde die Belegliste des Kunden leeren. Die Belege eines
  verknüpften Kunden werden deshalb weiter angezeigt; die Oberfläche zeigt den Zustand an.
- Aus Lexware übernommene Kunden erhalten `true`.

## Belege zuordnen und Status abgleichen

- Am Kunden: Belegliste des verknüpften Kontakts, zwei Minuten zwischengespeichert, „Aktualisieren“ lädt neu.
- An der Bauakte: nur zugeordnete Belege. Zuordnen aus den Belegen des Kunden der Bauakte oder über die Belegnummer. Ein
  Beleg gehört zu höchstens einer Bauakte (eindeutiger Index auf `lexware_id`).
- Vorschlag: Kommt die Bauaktennummer in Titel, Einleitung oder Schlusstext eines noch nicht zugeordneten Belegs vor,
  steht er im Dialog oben mit dem Hinweis „Nennt diese Bauakte“. Dafür werden höchstens zwölf Belege einzeln gelesen
  (Rate-Limit) und ihre Texte fünf Minuten zwischengespeichert.
- Bewusst nur ein Vorschlag, keine automatische Zuordnung: Die Nummer kann auch als Verweis in einem Beleg zu einer
  anderen Bauakte stehen (Nachtrag, Sammelrechnung), und eine falsche stille Zuordnung fiele erst spät auf. Die
  Bestätigung ist ein Klick.
- Statusabgleich: beim Öffnen des Abschnitts für Belege, deren letzter Abgleich älter als zwei Minuten ist (stornierte
  nicht mehr), höchstens zwanzig je Aufruf; „Aktualisieren“ gleicht alle sofort ab. Ist Lexware nicht erreichbar, bleibt
  der gespeicherte Stand mit Hinweis sichtbar. In Lexware gelöschte Belege erhalten den Hinweis „nicht mehr vorhanden“.

## Prüfung beim Anlegen und automatische Anlage

- **Warnung im Dialog „Neuer Kunde“:** `POST lexware/contacts/check` vergleicht die Eingaben mit der Kontaktliste
  (`LexwareMatching`, wie die übrige Dublettenprüfung) und liefert Treffer mit Gründen, unabhängig vom Schalter „Mit
  Lexware Office abgleichen“. Ohne Schlüssel oder ohne Verbindung antwortet der Endpunkt mit `checked=false` und einem
  Hinweis statt mit einem Fehler. „Aus Lexware Office übernehmen“ ist `POST lexware/contacts/{id}/import` (prüft seinerseits
  gegen die EstrichONE-Kunden); ein schon verknüpfter Kontakt wird mit seinem Kunden genannt.
- **Automatische Anlage** (`LexwareAutoCreateService`, Einstellung `autoCreate`, Vorgabe an): Der Hintergrunddienst nimmt
  bei jedem Lauf Kunden mit eingeschaltetem Abgleich, ohne Verknüpfung und ohne Vermerk. Dublettenprüfung gegen die
  Kontaktliste; ohne Treffer `POST /v1/contacts`, verknüpfen (`CreatedInLexware`), Protokoll `lexware.contact_created` ohne
  Benutzer. Mit Treffern Vermerk `duplicates`, kein Aufruf; der Kunde steht in der Abgleichübersicht unter den möglichen
  Dubletten. Das erfasst Web, Apps und offline angelegte Kunden gleichermaßen, weil nur der gespeicherte Kunde zählt.
- **Bestand:** Die Migration vermerkt alle vorhandenen Kunden mit eingeschaltetem Abgleich und ohne Verknüpfung als
  `skipped`. Dasselbe geschieht mit den offenen Kunden, wenn die Einstellung von aus auf an wechselt, und beim bewussten
  Lösen einer Verknüpfung. Schaltet jemand den Abgleich für einen Kunden später ein (am Kunden oder als Massenaktion),
  entfällt der Vermerk und es gilt die Regel für neue Kunden.
- **Keine Doppelanlage:** Ein Anlageversuch läuft immer unter derselben Sperre wie das Anlegen von Hand und der
  Änderungsabgleich. Schlägt er fehl, steht der Grund am Kunden (`failed`), der nächste Versuch folgt nach 2, 4, 8 bis
  höchstens 60 Minuten oder über „Erneut versuchen“. Vor jedem weiteren Versuch wird die Kontaktliste neu gelesen und
  erneut geprüft: Hat Lexware den Kontakt trotz Fehlermeldung angelegt, wird er als Treffer gefunden und nicht noch
  einmal angelegt.

## Beleg als PDF

- `LexwareClient.GetVoucherDocumentAsync` holt das PDF. Der Weg ist an einer Stelle umstellbar
  (`EstrichOne:Lexware:VoucherDocumentRoute`): `File` nutzt `GET /v1/{belegart}/{id}/file`, `Document` den älteren Weg
  über `/document` und `/v1/files/{documentFileId}`, `Auto` (Vorgabe) versucht `/file` und nimmt bei 404, 405 oder 501
  den älteren Weg. Beides sind `GET`-Aufrufe; die Schreibsperre bleibt unberührt.
- Für Entwürfe gibt es kein Dokument: Der Server fragt Lexware dann gar nicht erst (Status aus Belegliste bzw.
  Zuordnung, bei „Entwurf“ einmal frisch geprüft) und antwortet mit 409 `LEXWARE_NO_DOCUMENT` „Für Entwürfe stellt
  Lexware Office kein PDF bereit.“ Dieselbe Meldung entsteht, wenn Lexware mit 406 antwortet.
- Die EstrichONE-Endpunkte reichen das PDF durch (`Content-Type: application/pdf`, `Cache-Control: private, no-store`,
  `Content-Disposition: inline` bzw. mit `download=true` als Anhang, Dateiname `Rechnung-RE1042.pdf`). Geprüft wird das
  Recht `invoices.read` und die Zugehörigkeit: Am Kunden muss der Beleg in der Belegliste des verknüpften Kontakts stehen,
  an der Bauakte muss er ihr zugeordnet sein (mit Lesezugriff auf die Bauakte).
- Gespeichert wird nichts. Nur im Arbeitsspeicher bleiben höchstens acht PDFs zwei Minuten liegen, damit „Anzeigen“ und
  „Herunterladen“ nacheinander Lexware nicht zweimal belasten. Größte Datei 30 MB (`MaxDocumentBytes`).
- Web: „PDF anzeigen“ (große Vorschau wie bei Dokumenten, mit Herunterladen, neuem Tab, „In Lexware öffnen“) und
  „Herunterladen“ in der Belegliste am Kunden und im Abschnitt der Bauakte. Schlägt der Abruf fehl, erscheint die
  Meldung des Servers und „In Lexware öffnen“ bleibt.

## Änderungsabgleich verknüpfter Kunden

Abgeglichen werden nur Kunden mit Verknüpfung und eingeschaltetem „Mit Lexware Office abgleichen“, die weder archiviert
noch anonymisiert sind.

| Feld | EstrichONE | Lexware Office |
|---|---|---|
| Name | `Name`; bei Personen als „Vorname Nachname“ („Müller, Hans“ wird wie „Hans Müller“ gelesen) | `company.name` bzw. `person.firstName` und `person.lastName` |
| Anrede | `Salutation` | `person.salutation` (nur Personen) |
| Straße, PLZ, Ort | abweichende Rechnungsanschrift, sonst die Anschrift des Kunden | `addresses.billing[0].street/zip/city` |
| E-Mail-Adresse | erste E-Mail-Adresse | der Eintrag unter `emailAddresses`, der dem Kunden bzw. dem letzten Stand entspricht, sonst der erste |
| Telefon | erste Telefonnummer (ohne Fax) | entsprechend unter `phoneNumbers` (ohne `fax`) |
| USt-ID | `VatId` | `company.vatRegistrationId` (nur Firmen) |

Nicht abgeglichen: Kundennummern (nie überschrieben), Land (EstrichONE führt keines; neu angelegte Rechnungsanschriften
erhalten `DE`), Steuernummer, Lieferanschrift, Ansprechpartner, Notiz, weitere E-Mail-Adressen und Telefonnummern. Alle
diese Angaben gehen beim `PUT` unverändert zurück.

Vergleich (`LexwareContactSync.Same`): ohne Rand- und Mehrfachleerzeichen; E-Mail ohne Groß- und Kleinschreibung; Telefon
nur nach Ziffern (`+49`, `0049` wie `0`); USt-ID ohne Leerzeichen und ohne Groß- und Kleinschreibung. Formatunterschiede
lösen also keinen Abgleich aus.

Regeln je Feld (`LexwareContactSync.Merge`, Dreiwege-Abgleich gegen den zuletzt abgeglichenen Stand):

1. Beide Seiten gleich: nichts zu tun, der Wert wird der abgeglichene Stand.
2. Nur EstrichONE hat das Feld seit dem letzten Abgleich geändert: nach Lexware Office, wenn die Richtung es zulässt.
3. Nur Lexware Office hat es geändert: nach EstrichONE, wenn die Richtung es zulässt.
4. Beide Seiten haben es unterschiedlich geändert: Abweichung, nichts wird überschrieben.
5. Kein abgeglichener Stand (Erstabgleich): jeder Unterschied ist eine Abweichung, nichts wird überschrieben.
6. Lässt die Richtung eine einseitige Änderung nicht zu, bleibt sie als Abweichung sichtbar.
7. Ein leerer Name überschreibt nie einen vorhandenen.
8. Die Entscheidung des Nutzers (`sync/resolve`) gilt je Feld und unabhängig von der Richtung.

Abgeglichener Stand je Verknüpfung (`lexware_contact_links`): `synced_fields` (Feldwerte als JSON), `lexware_version`,
`lexware_updated_at`, `local_version` (Version des Kunden), `synced_at`, `deviations` mit `deviation_count`,
`remote_state` (`missing`, `archived`), `sync_error`, `sync_attempts`, `next_sync_at`.

Ablauf:

- **EstrichONE nach Lexware:** Der Hintergrunddienst (`LexwareChangeSyncJob`) sieht alle 30 Sekunden nach Verknüpfungen,
  deren Kunde eine andere Version hat als beim letzten Abgleich. Das erfasst jede Änderung, gleich woher sie kommt (Web,
  Apps über den Sync, Import), ohne dass das Speichern des Kunden von Lexware abhängt. Vor dem Schreiben wird der Kontakt
  frisch gelesen (`LexwareClient.UpdateContactAsync`), bei `409` neu gelesen und neu entschieden.
- **Lexware nach EstrichONE:** Alle 15 Minuten liest der Dienst die Kontaktliste neu und vergleicht `version` jedes
  verknüpften Kontakts mit dem gemerkten Stand. Sofort geht es mit „Jetzt abgleichen“ am Kunden (`sync/run`) und mit
  „Aktualisieren“ in der Abgleichübersicht. Beim Verknüpfen wird mit dem dabei gelesenen Kontakt sofort verglichen.
- **Fehler:** Ist Lexware nicht erreichbar oder lehnt es den Schlüssel ab, bricht der Lauf ab, merkt die Meldung in den
  Einstellungen (`lastChangeSyncError`) und pausiert fünf Minuten. Lehnt Lexware einen einzelnen Kontakt ab, steht die
  Meldung an der Verknüpfung; der nächste Versuch folgt nach 2, 4, 8 bis höchstens 60 Minuten.
- **Kontakt fehlt oder ist archiviert:** `remote_state`; angezeigt am Kunden und in der Abgleichübersicht, geschrieben
  wird nichts. Wird der Kontakt wiederhergestellt, läuft der Abgleich weiter.
- **Kunde gelöscht oder anonymisiert:** Die Verknüpfung endet, der Lexware-Kontakt bleibt unberührt. **Kunde
  archiviert:** Die Verknüpfung bleibt (sonst verlöre der Kunde seine Belegliste), der Abgleich ruht.
- **Abgleich ruht** (Schalter am Kunden aus, Kunde archiviert, Einstellung „Aus“): Der abgeglichene Stand wird verworfen.
  Beim Wiedereinschalten ist der erste Lauf wieder ein Erstabgleich, Unterschiede aus der Zwischenzeit erscheinen als
  Abweichungen.
- **Protokoll:** `lexware.contact_synced` je Richtung mit den Feldnamen; ohne Benutzer bei automatischer Übernahme.

Einstellung `changeSync` (`Both`, `ToLexware`, `FromLexware`, `Off`; Vorgabe `Both`) in
`PUT settings/integrations/lexware`.

Webhooks (`/v1/event-subscriptions`, Ereignis `contact.changed`) sind nicht umgesetzt: Lexware ruft dafür eine öffentlich
erreichbare Adresse des Servers auf, und EstrichONE läuft häufig ohne öffentliche Erreichbarkeit im Betriebsnetz. Der
regelmäßige Abruf kommt ohne aus.

## Dublettenprüfung

Vor jedem Anlegen, in beide Richtungen, prüft der Server (nicht nur die Oberfläche):

- EstrichONE nach Lexware: `POST lexware/customers/{id}/create` vergleicht den Kunden mit allen Lexware-Kunden. Gibt es
  Treffer und fehlt `createAnyway`, wird nichts angelegt; die Antwort enthält die Treffer mit Gründen.
- Lexware nach EstrichONE: `POST lexware/contacts/{id}/import` vergleicht mit allen EstrichONE-Kunden, gleiche Regel.

Verglichen werden (`LexwareMatching`): Name ohne Groß- und Kleinschreibung, Umlaute, Satzzeichen, Rechtsformzusätze
(GmbH, GmbH & Co. KG, e.K., UG, AG und weitere), Anreden und Titel, unabhängig von der Reihenfolge der Namensteile;
E-Mail-Adressen; Telefonnummern (nur Ziffern, `+49` und `0049` wie `0`, die letzten acht Ziffern); PLZ mit Straße
(„Str.“ wie „Straße“); Kundennummer; USt-ID.

| Grund | Wertung |
|---|---|
| gleiche E-Mail-Adresse, gleiche USt-ID | eindeutiges Merkmal |
| gleiche Kundennummer | eindeutig nur zusammen mit gleichem oder ähnlichem Namen (die Nummernkreise sind unabhängig) |
| gleicher Name und PLZ, gleicher Name | wahrscheinlich |
| gleiche Telefonnummer | Hinweis, zählt mit anderen Gründen zusammen |
| ähnlicher Name | Hinweis (Tippfehler oder Namenszusatz); nur ein gemeinsames Wort zählt erst mit gleicher PLZ |
| gleiche Anschrift | allein kein Treffer (Nachbarn, Bauträger und Bauherr) |
| Privatperson und Firma | senkt die Wertung, wenn kein eindeutiges Merkmal übereinstimmt |

Ein Lexware-Kontakt ist mit höchstens einem EstrichONE-Kunden verknüpft und umgekehrt (Primärschlüssel und eindeutiger
Index in `lexware_contact_links`, Meldung `LEXWARE_ALREADY_LINKED`). Die Massenaktion der Abgleichübersicht verknüpft nur
Paare, bei denen es auf beiden Seiten genau ein Gegenstück mit gleicher E-Mail-Adresse oder gleicher Kundennummer gibt,
und prüft das beim Ausführen erneut.

Die Kontaktliste wird seitenweise geladen (`size=250`) und fünf Minuten im Speicher gehalten; „Aktualisieren“ lädt neu.
Nur Kontakte mit der Rolle Kunde werden betrachtet, archivierte nicht.

## Gegen ein echtes Konto zu prüfen

1. Schlüssel aus `https://app.lexware.de/addons/public-api`: Verbindungstest und Firmenname.
2. Seitengröße 250 bei `GET /v1/contacts` (falls Lexware weniger zulässt: `EstrichOne:Lexware:ContactPageSize`).
3. `POST /v1/contacts`: Pflichtfelder, zulässige Werte für `person.salutation`, je Art nur eine E-Mail-Adresse bzw.
   Telefonnummer, Verhalten bei fehlender Anschrift.
4. `GET /v1/voucherlist`: Filter `contactId` und `voucherNumber`, `voucherStatus=any`, mehrere Belegarten in
   `voucherType`, Sortierung `voucherDate,DESC`, Seitengröße 100, Feld `totalAmount`.
5. Einzelabruf je Belegart: Pfade `quotations`, `credit-notes`, `order-confirmations`; Statuswerte der Angebote
   (`accepted`, `rejected`), Feld `expirationDate`; Entwurf ohne `voucherNumber`; Feld `dueDate` der Rechnung.
6. Felder `title`, `introduction`, `remark` für den Zuordnungsvorschlag.
7. Links `permalink/{invoices|quotations|credit-notes|order-confirmations}/{view|edit}` und `permalink/contacts/view` in der
   Lexware-Oberfläche.
8. Fehlerformat bei 406 und 422 (Felder `details[]` bzw. `IssueList[]`) und Kopfzeile `Retry-After` bei 429.
9. `GET /v1/contacts` und `GET /v1/contacts/{id}`: Felder `version` und `updatedDate` sind in Liste und Einzelabruf
   enthalten; `archived` am Einzelabruf; archivierte Kontakte in der Liste.
10. `PUT /v1/contacts/{id}`: nimmt den vollständig zurückgesendeten Kontakt an (insbesondere `roles.customer.number`,
    `company.contactPersons`, `xRechnung`, weitere Anschriften); erwartet `version`; antwortet bei veralteter Version mit
    `409`; liefert `version` und `updatedDate`. Prüfen, ob die weggelassenen Felder `id`, `organizationId`,
    `createdDate`, `updatedDate`, `archived` so richtig sind.
11. `PUT`: Entfernen eines Werts (Schlüssel fehlt im gesendeten Kontakt) leert das Feld; leere Listen unter
    `emailAddresses.business` bzw. `phoneNumbers.business` werden angenommen; je Art nur ein Eintrag.
12. `PUT`: Name einer Person als `firstName` und `lastName` (Aufteilung am letzten Leerzeichen bzw. am Komma) und zulässige
    Werte für `salutation`.
13. Wechsel Firma zu Person an einem Kontakt ist nicht vorgesehen; EstrichONE schreibt je nach Art des Lexware-Kontakts.
14. Beleg-PDF: `GET /v1/{belegart}/{id}/file` mit `Accept: application/pdf` für alle vier Belegarten; Antwort bei Entwürfen
    (erwartet `406`); falls der Pfad fehlt, der ältere Weg über `/document` und `/v1/files/{documentFileId}`. Bei
    E-Rechnungen (XRechnung, ZUGFeRD) prüfen, dass mit `Accept: application/pdf` ein PDF und kein XML kommt.
15. Automatische Anlage: Pflichtfelder bei `POST /v1/contacts` für Personen ohne Vornamen und Firmen ohne Anschrift;
    Verhalten bei Zeitüberschreitung (Kontakt trotzdem angelegt?) und ob er danach in `GET /v1/contacts` sofort erscheint.
16. Lastverhalten: Erstabgleich mit vielen Verknüpfungen (liest nur die Kontaktliste) und viele Änderungen kurz
    hintereinander (ein `GET` und ein `PUT` je Kontakt, zwei Anfragen je Sekunde).
## Nicht Teil dieser Runde

Webhooks (Änderungen ohne Abruf, siehe oben), Lexware-Daten und Belege in der Bauakte der Apps sowie die Warnung beim
Anlegen eines Kunden in den Apps (dort gibt es weiter nur den Schalter „Mit Lexware Office abgleichen“ am Kunden;
Änderungen und neue Kunden aus den Apps werden auf dem Server abgeglichen bzw. angelegt; die Endpunkte dafür stehen bereit), Abgleich von Land,
Steuernummer, Lieferanschrift und Ansprechpartnern, Zahlungseingänge im Einzelnen. Das Anlegen von Rechnungen oder
Angeboten aus EstrichONE ist ausdrücklich nicht vorgesehen.
