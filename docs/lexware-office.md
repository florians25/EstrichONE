# Lexware Office (Public API): technische Notiz

Stand: November 2026, Modul `Lexware` (Backend `backend/src/EstrichOne.Api/Modules/Lexware`, Web `web/webapp/util/lexware*.ts`).

Rechnungen entstehen in Lexware Office (früher lexoffice), Bauunterlagen liegen in EstrichONE. EstrichONE verknüpft
Kunden mit Lexware-Kontakten, gleicht spätere Änderungen an verknüpften Kunden in beide Richtungen ab und zeigt Rechnungen
und Angebote am Kunden und an der Bauakte an (Nummer, Art, Datum, Betrag, Status, PDF). Dazu kommen drei Zusatzmodule,
alle untergeordnet zum Modul `lexware` und nach der Installation aus:

- `lexwareArticles` („Lexware Office: Artikel“): Abgleich von „Artikel und Leistungen“ mit `/v1/articles`.
- `quotations` („Angebote (Beta)“): Angebote in EstrichONE vorbereiten und als Entwurf in Lexware Office anlegen
  (Ausbaustufe 1: `POST /v1/quotations` ohne `finalize`; Nummer, Festschreiben und Versand bleiben in Lexware Office).
- `lexwareStatus` („Lexware Office: Status der Bauakte automatisch“): Statusregeln aus Angebots- und Rechnungsstatus.

**Grundsatz: Rechnungen sind rein lesend.** EstrichONE legt keine Rechnung, Gutschrift oder Auftragsbestätigung an,
ändert keinen Beleg, schreibt keinen fest und löscht nichts. Geschrieben wird in Lexware Office nur an genau benannten
Stellen:

| Aufruf | Wann |
|---|---|
| `POST /v1/contacts` | Kunde anlegen, immer nach Dublettenprüfung |
| `PUT /v1/contacts/{id}` | Änderungsabgleich verknüpfter Kunden mit eingeschaltetem Abgleich |
| `POST /v1/articles` | Artikelabgleich, nur Richtung „beide“, nur ohne möglichen Treffer bzw. nach ausdrücklicher Entscheidung |
| `PUT /v1/articles/{id}` | Artikelabgleich, nur Richtung „beide“, mit `version` des frisch gelesenen Artikels |
| `POST /v1/quotations` | „Als Entwurf an Lexware Office senden“, ohne Abfrage (kein `finalize`), höchstens einmal je Entwurf |

Kontakte und Artikel werden dort nie archiviert oder gelöscht. Abgesichert ist das an drei Stellen:

- `LexwareClient` hat für Belege außer `CreateQuotationDraftAsync` nur lesende Methoden.
- `LexwareClient.WriteAllowed` weist vor dem Senden jeden schreibenden Aufruf ab, der nicht in der Tabelle steht. Schreibende
  Aufrufe dürfen keine Abfrage tragen; `POST /v1/quotations?finalize=true` wird deshalb abgewiesen.
- Tests prüfen die Liste der öffentlichen Methoden des Clients, die Sperre (auch für `/file`, `/document`, `/v1/files`,
  `DELETE` auf Kontakte und Artikel, `PUT /v1/quotations`, `POST /v1/invoices`) und dass über alle Abläufe hinweg nur die
  erlaubten Aufrufe schreiben; die nachgebildete API lehnt `POST /v1/quotations` mit Abfrage ab.

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
| `LexwareArticleModel.cs` | Tabelle `catalog_items` („Artikel und Leistungen“ mit Lexware-Verknüpfung und Abgleichstand) |
| `LexwareArticleSync.cs` | Artikelabgleich: Feldwerte, Dublettenprüfung, Dreiwege-Abgleich (reine Funktionen) und Lauf über alle Artikel |
| `LexwareArticleEndpoints.cs` | Endpunkte `lexware/articles` |
| `LexwareQuotationModel.cs` | Tabellen `quotation_drafts`, `quotation_draft_lines` |
| `LexwareQuotations.cs` | Summen, Prüfungen, Vorschläge aus der Bauakte, Senden mit Sperre gegen Doppelsenden |
| `LexwareQuotationEndpoints.cs` | Endpunkte `lexware/quotations` |
| `LexwareBauakteStatus.cs` | Statusregeln der Bauakte (reine Funktionen und Dienst) |

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
| `GET /v1/articles?page=&size=` | Artikelabgleich (alle Seiten, `size=250`) | Seite: `content`, `last`, `totalPages`, `totalElements`; je Artikel `id`, `title`, `description`, `type` (`PRODUCT`, `SERVICE`), `articleNumber`, `gtin`, `unitName`, `price.netPrice/grossPrice/leadingPrice/taxRate`, `archived`, `version`, `updatedDate` |
| `GET /v1/articles/{id}` | Artikel vor dem Verknüpfen, Übernehmen und vor jedem Ändern lesen | wie oben |
| `POST /v1/articles` | Eintrag in Lexware Office anlegen (nur Richtung „beide“) | `title`, `description`, `type`, `articleNumber`, `gtin`, `unitName`, `price: {netPrice, grossPrice, leadingPrice: "NET", taxRate}`, `version: 0`. Antwort: `id`, `version` |
| `PUT /v1/articles/{id}` | Abgeglichene Felder eines verknüpften Artikels ändern (nur Richtung „beide“) | Der frisch gelesene Artikel mit seiner `version`, geändert nur in den abgeglichenen Feldern; Preis führt danach netto (`leadingPrice: "NET"`, `grossPrice` aus Netto und Steuersatz). Nicht mitgesendet: `id`, `organizationId`, `createdDate`, `updatedDate`, `archived`. `409` = Versionskonflikt |
| `GET /v1/payment-conditions` | Auswahl der Zahlungsbedingung in der Angebotsmaske (30 Minuten zwischengespeichert) | Liste mit `id`, `paymentTermLabelTemplate`, `paymentTermDuration`, `paymentDiscountConditions.discountPercentage/discountRange`, `organizationDefault` |
| `POST /v1/quotations` | Angebot als Entwurf anlegen (ohne `finalize`) | `voucherDate`, `expirationDate`, `address.contactId`, `lineItems[]` (`type` `custom`, `material`, `service` mit `id` des Artikels oder `text`; `name`, `description`, `quantity`, `unitName`, `unitPrice.currency/netAmount/taxRatePercentage`, `discountPercentage`), `totalPrice.currency`, `taxConditions.taxType` (`net` oder `constructionService13b`), `paymentConditions.paymentTermLabel/paymentTermDuration/paymentDiscountConditions`, `title`, `introduction`, `remark`. Antwort: `id`, `version` |

Lexware liefert Zeitpunkte mit Zeitzone (`2023-02-22T00:00:00.000+01:00`); maßgeblich ist das Datum in dieser Zone.
„Überfällig“ ist kein Status der Rechnung: EstrichONE leitet ihn aus Status `open` und überschrittener Fälligkeit ab (in
der Belegliste liefert Lexware `overdue` selbst; gespeichert wird `open`).

Links in die Lexware-Oberfläche: `https://app.lexware.de/permalink/{invoices|quotations|credit-notes|order-confirmations}/view/{id}`
(Entwürfe `.../edit/{id}`), `.../permalink/contacts/view/{id}`. Bei der früheren Basis-URL `https://app.lexoffice.de`. Über
`EstrichOne:Lexware:AppBaseUrl` fest einstellbar.

## Artikel und Leistungen, Artikelabgleich

„Artikel und Leistungen“ (`catalog_items`) ist ein eigener Katalog: Verkaufsartikel und Leistungen mit Preis netto und
Steuersatz, wie sie in Angeboten stehen. Der Materialstamm führt Einkaufspreise, Lieferanten und Bestellungen und bleibt
unverändert; ein Eintrag kann über `material_id` (ohne Fremdschlüssel über die Modulgrenze, gelesen über
`Materials.Exports.IMaterialDirectory`) mit einem Artikel des Materialstamms verknüpft sein. Ein Lexware-Artikel gehört zu
höchstens einem Eintrag (eindeutiger Index auf `lexware_id`).

Einstellung `articleSync` (`FromLexware` Vorgabe, `Both`, `Off`) in `PUT settings/integrations/lexware`.

| Feld | EstrichONE | Lexware Office |
|---|---|---|
| Bezeichnung | `title` | `title` |
| Beschreibung | `description` | `description` |
| Art | `kind` (`Product`, `Service`) | `type` (`PRODUCT`, `SERVICE`) |
| Artikelnummer, GTIN, Einheit | `article_number`, `gtin`, `unit` | `articleNumber`, `gtin`, `unitName` |
| Preis netto, Steuersatz | `net_price`, `tax_rate` | `price.netPrice`, `price.taxRate` |

Ablauf eines Laufs (`LexwareArticleSyncService.RunAsync`, Hintergrunddienst alle 30 Minuten über `ArticleSyncInterval`,
sofort mit „Jetzt abgleichen“; ein Lauf zur selben Zeit je Server):

1. Alle Artikel lesen (alle Seiten, archivierte eingeschlossen).
2. Verknüpfte Einträge: Dreiwege-Abgleich je Feld gegen `synced_fields` (gleiche Regeln wie beim Kundenabgleich; die
   Bezeichnung wird nie durch einen leeren Wert ersetzt). Aus Lexware Office wird übernommen, wenn nur dort geändert
   wurde. Nach Lexware Office geht ein Feld nur bei `Both`, und zwar nach frischem `GET` mit `version`; `409` liest neu.
   Im Erstabgleich (nach „Verknüpfen“) wird nichts überschrieben. Archivierte Artikel werden inaktiv, fehlende als
   `missing` gekennzeichnet. Ein Lauf ohne Unterschiede ändert den Eintrag nicht (keine neue Version).
3. Nur in Lexware Office: übernehmen, wenn kein nicht verknüpfter Eintrag dieselbe Artikelnummer oder dieselbe Bezeichnung
   (ohne Groß- und Kleinschreibung und Satzzeichen) hat. Sonst Treffer zur Entscheidung (Abgleichübersicht).
4. Nur bei `Both`, nur in EstrichONE (aktiv): in Lexware Office anlegen, wenn dort kein nicht verknüpfter Artikel mit
   gleicher Artikelnummer oder Bezeichnung steht. `POST` wird nicht wiederholt; hat Lexware den Artikel trotz Fehler
   angelegt, findet ihn der nächste Lauf als Treffer.
5. Fehler wie beim Kundenabgleich: Lexware nicht erreichbar oder Schlüssel abgelehnt bricht ab (`lastArticleSyncError`),
   ein abgelehnter einzelner Artikel steht am Eintrag (`sync_error`).

Protokoll: `lexware.articles_synced`, `lexware.article_created`, `lexware.article_synced`, `lexware.article_imported`,
`lexware.article_linked`.

## Angebote (Beta)

Recht `quotations.prepare` („Angebote vorbereiten“, Gruppe Rechnungen): Administratoren immer, die Rolle „Büro und
Bauleitung“ bei neuen Installationen. Module `lexware` und `quotations`.

- **Entwurf** (`quotation_drafts`, `quotation_draft_lines`): Kunde, optional Bauakte, Titel, Einleitung (vorbelegt mit
  Nummer, Bezeichnung und Anschrift der Bauakte, damit der Zuordnungsvorschlag über die Bauaktennummer greift), Gültig bis
  (Vorgabe heute plus 30 Tage), Zahlungsbedingung (Kopie der Werte aus Lexware Office oder eigener Text), Steuerart `net`
  oder `constructionService13b`, Bemerkung. Positionen `item` (aus dem Katalog oder frei) und `text`.
- **Summen** (`QuotationRules.Totals`, in der Weboberfläche gleich in `util/quotations.ts`): Positionsbetrag gleich Menge mal
  Einzelpreis abzüglich Rabatt, auf Cent gerundet; Steuer je Steuersatz auf die Summe der Positionen; bei § 13b keine
  Steuer. Nur Vorschau, maßgeblich rechnet Lexware Office.
- **Vorschläge** (`GET lexware/quotations/suggestions`): Summe der Flächen aus „Räume und Flächen“ (über
  `Bauakten.Exports.IBauakteRooms.AreasAsync`), sonst Kennwert `project.totalArea`, als Position in m² mit Estrichart,
  Nenndicke und Fußbodenheizung in der Beschreibung. Gibt es genau einen aktiven Katalogeintrag „Leistung je m²“, wird
  dessen Bezeichnung und Preis vorgeschlagen. Raumumfänge führt EstrichONE nicht; ein Vorschlag für Randdämmstreifen
  entfällt deshalb mit Hinweis. Nichts wird ohne Bestätigung übernommen.
- **Senden** (`LexwareQuotationService.SendAsync`):
  1. Bereits gesendet: Antwort mit dem gesendeten Stand, kein Aufruf (idempotent).
  2. Prüfungen (`QuotationRules.Problems`): Titel, mindestens eine Position, Menge größer 0, Einheit, Einzelpreis,
     Steuersatz 19, 7 oder 0, Gültig bis; Kunde verknüpft.
  3. Sperre in der Datenbank: Nur wer den Entwurf mit unveränderter Version von `Draft` bzw. `Failed` auf `Sending` setzt,
     sendet. Ein hängendes `Sending` gilt nach zehn Minuten als abgebrochen.
  4. `POST /v1/quotations` ohne Abfrage, ohne Wiederholung (außer bei 429). Katalogeinträge mit Lexware-Verknüpfung gehen
     als `material` bzw. `service` mit `id`, alles andere als `custom`.
  5. Fehler: `Failed` mit Meldung. Bei Zeitüberschreitung, fehlender Verbindung oder 5xx ist offen, ob Lexware das Angebot
     angelegt hat (`send_uncertain`): Der nächste Versuch sucht zuerst in der Belegliste des Kontakts nach einem
     Angebotsentwurf seit dem ersten Versuch mit gleichem Titel und gleichem Nettobetrag und übernimmt ihn bei genau einem
     Treffer, statt erneut zu senden. Ein solcher Entwurf lässt sich nicht verwerfen, nur erneut senden.
  6. Erfolg: `Sent`, `lexware_id` (eindeutig), danach schreibgeschützt. Mit Bauakte entsteht eine Zuordnung in
     `lexware_voucher_links` (Art `quotation`, Status `draft`, Werte aus `GET /v1/quotations/{id}` bzw. aus dem Entwurf);
     der Beleg erscheint in der vorhandenen Belegliste mit „In Lexware öffnen“ (`permalink/quotations/edit/{id}`). PDF wie
     bei allen Belegen erst nach dem Festschreiben in Lexware Office. Protokoll `lexware.quotation_sent`.
- **Apps:** nur lesend über die vorhandene Belegliste; keine Maske.

## Status der Bauakte aus Lexware Office

Modul `lexwareStatus`; Regeln `statusOnQuotationAccepted` und `statusOnInvoicesPaid` (Vorgabe jeweils an) in
`PUT settings/integrations/lexware`. Ausgewertet nach jedem Statusabgleich der einer Bauakte zugeordneten Belege
(`GET bauakten/{id}/lexware`, auch mit `refresh=true`) und im Hintergrund alle 15 Minuten für bis zu 30 laufende Bauakten
mit Belegen (ältester Abgleich zuerst; Abruf je Beleg nur, wenn der kurze Cache abgelaufen ist).

| Regel | Bedingung | Wirkung |
|---|---|---|
| Angebot angenommen | ein zugeordnetes Angebot hat Status `accepted`, Bauakte steht auf `Inquiry` | Status `Planned` (nur aus `Inquiry`) |
| Rechnungen bezahlt | mindestens eine Rechnung, alle nicht stornierten Rechnungen `paid` oder `paidoff`, keine Gutschrift `open`; Bauakte weder `Completed` noch `Cancelled` | Kennzeichen „Zur Abschlussprüfung“ |

**Kein neuer Statuswert.** Die Apps lesen `status` als feste Werteliste und setzen unbekannte Werte still auf „Geplant“
(Android `bauakteStatus()` in `Domain.kt`, iOS `BauakteStatus(rawValue:) ?? .planned` in `Bauakte.swift`). Ein neuer Wert
würde in älteren Apps als „Geplant“ erscheinen und könnte beim Bearbeiten zurückgeschrieben werden. Das Kennzeichen ist
deshalb ein eigenes Merkmal der Bauakte: `review_requested_at`, `review_reason`, dazu `review_stamp` (Zahlstand, zu dem es
gesetzt bzw. zurückgestellt wurde). API: `reviewRequestedAt`, `reviewReason` an `BauakteDetail` (auch im Sync) und
`reviewRequestedAt` an `BauakteListItem`. Ältere Apps ignorieren die Felder.

- „Prüfung zurückstellen“ (`POST bauakten/{id}/review/dismiss`) entfernt das Kennzeichen; der Zahlstand bleibt gemerkt.
  Erneut gesetzt wird es erst bei anderem Zahlstand (andere Rechnungen oder Status). Entfällt die Bedingung (neue offene
  Rechnung), wird der gemerkte Stand verworfen.
- Abschließen und Stornieren entfernen das Kennzeichen. Abgeschlossen wird nie automatisch.
- Nur vorwärts: Keine Regel setzt einen Status zurück, keine ändert eine Bauakte, die nicht mehr auf „Anfrage“ steht.
- Verlauf und Protokoll: `bauakte.status_changed` mit `auto=true`, `trigger=LexwareQuotationAccepted`,
  `source=Lexware Office` („Automatisch durch Lexware Office: …“) bzw. `bauakte.review_requested`; „Prüfung zurückstellen“
  als `bauakte.review_dismissed` mit Benutzer.
- Benachrichtigung `bauakte.lexware_status` („Status aus Lexware Office“) an die zuständige Person der Bauakte.

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
| `POST lexware/contacts/check` | `contacts.edit` | prüfen, ob es einen Kunden in Lexware Office schon gibt (während der Eingabe im Web, verbindlich vor dem Anlegen in den Apps); antwortet ohne Verbindung mit `checked=false` |
| `POST contacts` | `contacts.edit` | Kunde anlegen; prüft vorher verbindlich in Lexware Office (`LEXWARE_CUSTOMER_EXISTS`, `LEXWARE_CHECK_UNAVAILABLE`, Felder `lexwareCreateAnyway`, `lexwareSkipCheck`) |
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

| `GET lexware/articles` | `materials.read` | Artikel und Leistungen mit Abgleichstand (ohne Aufruf bei Lexware Office); Modul `lexwareArticles` oder `quotations` |
| `PUT lexware/articles/{id}` | `materials.manage` | Eintrag anlegen oder ändern (nur in EstrichONE, mit `version`) |
| `POST lexware/articles/sync` | `materials.manage` | „Jetzt abgleichen“; Modul `lexwareArticles` |
| `GET lexware/articles/reconciliation` | `materials.manage` | nicht verknüpfte Artikel beider Seiten mit möglichen Treffern |
| `POST lexware/articles/import` | `materials.manage` | Artikel übernehmen; bei Treffern nur mit `createAnyway` (`LEXWARE_ARTICLE_DUPLICATE`) |
| `POST lexware/articles/{id}/link` | `materials.manage` | mit vorhandenem Artikel verknüpfen; Erstabgleich überschreibt nichts |
| `POST lexware/articles/{id}/create` | `materials.manage` | in Lexware Office anlegen, nur Richtung „beide“ (`LEXWARE_ARTICLE_SYNC_DIRECTION`) |
| `POST lexware/articles/{id}/resolve` | `materials.manage` | Abweichungen je Feld entscheiden; „EstrichONE“ nur bei Richtung „beide“ |
| `GET lexware/quotations` | `quotations.prepare` | Angebotsentwürfe einer Bauakte (`bauakteId`) oder eines Kunden (`contactId`) |
| `GET lexware/quotations/options` | `quotations.prepare` | Zahlungsbedingungen (bei Fehler `paymentConditionsError`, die Maske bleibt nutzbar), Katalog, Steuersätze |
| `GET lexware/quotations/suggestions?bauakteId=` | `quotations.prepare` | Positionsvorschläge aus der Bauakte |
| `POST lexware/quotations` | `quotations.prepare` | Entwurf anlegen (idempotent über die ID); ohne Verknüpfung des Kunden 409 `LEXWARE_CUSTOMER_NOT_LINKED` mit `contactId` |
| `GET`, `PUT`, `DELETE lexware/quotations/{id}` | `quotations.prepare` | Entwurf lesen, speichern (mit `version`), verwerfen; nach dem Senden 409 `LEXWARE_QUOTATION_SENT` |
| `POST lexware/quotations/{id}/send` | `quotations.prepare` | als Entwurf an Lexware Office senden; erneut aufgerufen liefert er den gesendeten Stand ohne zweiten Aufruf |
| `POST bauakten/{id}/review/dismiss` | `bauakten.edit` | „Prüfung zurückstellen“ |
| `GET bauakten?review=true` | `bauakten.read` | nur Bauakten mit Kennzeichen „Zur Abschlussprüfung“ |

Die Angebotsendpunkte verlangen die Module `lexware` und `quotations`. Einen Endpunkt, der eine Rechnung anlegt oder einen
Beleg ändert, gibt es nicht.

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

## Verbindliche Prüfung vor der Kundenanlage

- `POST contacts` prüft bei eingeschaltetem Modul mit hinterlegtem Schlüssel selbst in Lexware Office, mit den
  endgültigen Angaben und bevor gespeichert wird (`ICustomerCreationCheck` in `Contacts/Exports`, umgesetzt von
  `LexwareCustomerCreationCheck`; unabhängig vom Schalter „Mit Lexware Office abgleichen“).
- Treffer: 409 `LEXWARE_CUSTOMER_EXISTS` mit `candidates` (wie die Prüfung, mit Gründen) und `canImport`; nichts wird
  gespeichert. Anlage trotz Treffern nur mit `lexwareCreateAnyway: true`.
- Prüfung nicht möglich (Lexware nicht erreichbar, Schlüssel abgelehnt, Zeitüberschreitung): 409
  `LEXWARE_CHECK_UNAVAILABLE`; nichts wird gespeichert. Anlage nur mit `lexwareSkipCheck: true`. Dieses Feld übergeht nur
  die fehlende Antwort: Ist Lexware erreichbar und gibt es Treffer, bleibt es bei `LEXWARE_CUSTOMER_EXISTS`.
- Präzision (`LexwareService.PreciseContactsAsync`): die vollständige Kontaktliste über alle Seiten (kurz
  zwischengespeichert) und dazu bei jeder Prüfung frische, gezielte Abfragen `GET /v1/contacts?customer=true&name=` (voller
  Name und längstes Wort) und `&email=`, jeweils über alle Seiten der Treffer. Die Drosselung auf zwei Anfragen je
  Sekunde gilt unverändert. `POST lexware/contacts/check` nutzt dieselbe Prüfung.
- Web: Jede Stelle, die einen Kunden anlegt, geht über `util/lexwareCreate.ts` (`createCustomerChecked`): Zustand
  „Prüfung in Lexware Office läuft“, bei Treffern die Kandidaten (im Dialog „Neuer Kunde“ als Liste, sonst als Rückfrage),
  bei fehlender Antwort die Rückfrage „Ohne Prüfung anlegen“.
- Apps: Kunden entstehen dort über den Sync (auch ohne Verbindung). Die Apps prüfen deshalb beim Klick auf Anlegen bzw.
  Sichern über `POST lexware/contacts/check` mit den endgültigen Angaben und legen erst danach an; bei Treffern nur nach
  „Aus Lexware Office übernehmen“ oder „Trotzdem neu anlegen“, ohne Antwort nur nach „Ohne Prüfung anlegen“.
- Entscheidung zu älteren App-Versionen und zum Sync: Die Sync-Operation `contact.create` wird nicht abgelehnt. Eine
  Ablehnung ließe offline erfasste Kunden und die daran hängenden Bauakten auf dem Gerät hängen, und ältere Apps können
  keine Rückfrage zeigen. Solche Kunden werden angenommen und danach auf dem Server geprüft: ohne Treffer automatisch in
  Lexware Office angelegt (wenn eingeschaltet), mit Treffern nie automatisch, sondern unter „Mögliche Dubletten“
  geführt. Ältere Clients, die `POST contacts` direkt aufrufen, erhalten die Ablehnung im Standard-Problemformat; `detail`
  nennt den Grund verständlich („Achtung: Dieser Kunde ist bereits in Lexware Office vorhanden. Der Kunde wurde nicht
  angelegt. …“) und wird von ihnen als Fehlermeldung angezeigt.
- Nicht erfasst: der Kunden-Import aus CSV (eigene Vorschau mit Dublettenprüfung in EstrichONE; die importierten Kunden
  erscheinen im Kundenabgleich) und die Übernahme aus Lexware Office selbst.

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
16. Verbindliche Prüfung: Filter `name` (Teilwort, Mindestlänge, Groß- und Kleinschreibung) und `email` bei
    `GET /v1/contacts`, Seitengröße 100 und Blättern in gefilterten Ergebnissen; Dauer der Prüfung beim Klick mit echter
    Drosselung.
17. Lastverhalten: Erstabgleich mit vielen Verknüpfungen (liest nur die Kontaktliste) und viele Änderungen kurz
    hintereinander (ein `GET` und ein `PUT` je Kontakt, zwei Anfragen je Sekunde).
18. `GET /v1/articles`: Paging mit `page` und `size`, größte zulässige Seitengröße (angenommen 250,
    `EstrichOne:Lexware:ArticlePageSize`), Feldnamen `title`, `description`, `type`, `articleNumber`, `gtin`, `unitName`,
    `price.netPrice/grossPrice/leadingPrice/taxRate`, `archived`, `version`; ob archivierte Artikel in der Liste stehen.
19. `POST /v1/articles` und `PUT /v1/articles/{id}`: Pflichtfelder, ob `grossPrice` mitgesendet werden darf bzw. muss, wenn
    `leadingPrice` `NET` ist (Rundung), Verhalten bei veralteter `version` (erwartet `409`), ob weggelassene Felder
    (`id`, `organizationId`, `createdDate`, `updatedDate`, `archived`) richtig sind, Feld `note` bleibt erhalten.
20. `GET /v1/payment-conditions`: Antwort als Liste, Felder `paymentTermLabelTemplate`, `paymentTermDuration`,
    `paymentDiscountConditions`, `organizationDefault`; ob die Vorlage Platzhalter enthält, die beim Senden ersetzt werden
    müssen.
21. `POST /v1/quotations` ohne `finalize`: entsteht ein Entwurf ohne Nummer; Pflichtfelder (`voucherDate`,
    `expirationDate`, `address.contactId`, `totalPrice.currency`, `taxConditions.taxType`); Format der Zeitpunkte;
    `lineItems` mit `type` `material`/`service` und `id` eines Artikels (müssen `name` und `unitPrice` trotzdem mitgehen?);
    Textpositionen mit `name` und `description`; `discountPercentage`; Text der Zahlungsbedingung.
22. `taxConditions.taxType` `constructionService13b`: ob die Positionen `taxRatePercentage` 0 tragen müssen (EstrichONE
    sendet 0) und welche Pflichtangaben am Kontakt nötig sind; ob Lexware den Hinweis auf die Steuerschuldnerschaft selbst
    druckt.
23. Antwort von `POST /v1/quotations` (`id`, `version`) und ob das Angebot danach sofort in `GET /v1/voucherlist` mit
    Status `draft` und `title` bzw. per `GET /v1/quotations/{id}` mit `totalPrice.totalNetAmount` erscheint (Grundlage der
    Suche nach einem trotz Fehlermeldung angelegten Angebot).
24. Permalink `permalink/quotations/edit/{id}` für Angebotsentwürfe und `permalink/articles/view/{id}` für Artikel.
25. Statusregeln: Statuswert `accepted` bei angenommenen Angeboten, `paidoff` bei durch Gutschrift ausgeglichenen
    Rechnungen, Status `open` bei noch nicht erstatteten Gutschriften.
## Nicht Teil dieser Runde

Webhooks (Änderungen ohne Abruf, siehe oben), Lexware-Daten und Belege in der Bauakte der Apps sowie die Warnung beim
Anlegen eines Kunden in den Apps (dort gibt es weiter nur den Schalter „Mit Lexware Office abgleichen“ am Kunden;
Änderungen und neue Kunden aus den Apps werden auf dem Server abgeglichen bzw. angelegt; die Endpunkte dafür stehen bereit), Abgleich von Land,
Steuernummer, Lieferanschrift und Ansprechpartnern, Zahlungseingänge im Einzelnen. Rechnungen aus EstrichONE anzulegen
ist ausdrücklich nicht vorgesehen. Bei Angeboten nicht Teil von Ausbaustufe 1: Nummer vergeben, festschreiben
(`finalize`), versenden, ändern eines gesendeten Angebots aus EstrichONE und eine Angebotsmaske in den Apps. Löschen von
Artikeln in Lexware Office, Abgleich von `note` und Mengeneinheiten-Katalog, Raumumfänge für Randdämmstreifen.
