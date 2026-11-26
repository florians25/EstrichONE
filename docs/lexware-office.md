# Lexware Office (Public API): technische Notiz

Stand: November 2026, Modul `Lexware` (Backend `backend/src/EstrichOne.Api/Modules/Lexware`, Web `web/webapp/util/lexware*.ts`).

Rechnungen entstehen und liegen in Lexware Office (früher lexoffice), Bauunterlagen in EstrichONE. EstrichONE verknüpft
Kunden mit Lexware-Kontakten, legt Rechnungen aus der Bauakte als Entwurf an und zeigt Nummer, Betrag und Status der
Belege an der Bauakte. Es schließt keine Rechnung ab, versendet nichts und löscht nichts in Lexware Office.

Die öffentliche Dokumentation (https://developers.lexware.io/docs/) war beim Bau aus der Entwicklungsumgebung nicht
erreichbar. Endpunkte und Feldnamen sind nach bestem Wissen umgesetzt und stehen gebündelt in `LexwareClient.cs`; der
Abschnitt „Gegen ein echtes Konto zu prüfen“ nennt, was vor dem Einsatz zu bestätigen ist.

## Aufbau

| Datei | Inhalt |
|---|---|
| `LexwareClient.cs` | HTTP-Zugang: Bearer-Schlüssel, Drosselung, Wiederholung, Zeitlimit, Fehlertexte, alle Feldnamen der API |
| `LexwareMatching.cs` | Dublettenprüfung als reine Funktionen (Normalisierung, Gründe, Bewertung) |
| `LexwareService.cs` | Einstellungen, Schlüssel (Data Protection), Kontaktliste mit Cache, Verknüpfung, Statusabgleich, Vorschlag |
| `LexwareEndpoints.cs` | Endpunkte, Rechte, Fehlerabbildung auf Problem-Antworten |
| `LexwareModel.cs` | Tabellen `lexware_settings`, `lexware_contact_links`, `lexware_voucher_links` |

Einstellungen in `appsettings` unter `EstrichOne:Lexware` (`LexwareOptions`): erlaubte Hosts der Basis-URL,
Mindestabstand der Aufrufe, Versuche, Wartezeiten, Zeitlimit, Cache-Dauern, Seitengröße der Kontaktliste, Adresse der
Lexware-Oberfläche (`AppBaseUrl`).

## Verwendete Endpunkte von Lexware Office

Basis-URL `https://api.lexware.io` (früher `https://api.lexoffice.io`, weiterhin einstellbar). Kopfzeilen:
`Authorization: Bearer <Schlüssel>`, `Accept: application/json`, bei schreibenden Aufrufen `Content-Type: application/json`.

| Aufruf | Zweck | Gelesene bzw. gesendete Felder |
|---|---|---|
| `GET /v1/profile` | Verbindung testen | `organizationId`, `companyName` |
| `GET /v1/contacts?customer=true&page=&size=` | Kontaktliste für Dublettenprüfung, Übernahme und Abgleichübersicht | Seite: `content`, `last`, `totalPages`, `totalElements` |
| `GET /v1/contacts?customer=true&name=` bzw. `email=`, `number=` | gezielte Suche im Dialog „Kontakt suchen und verknüpfen“ | wie oben |
| `GET /v1/contacts/{id}` | Kontakt vor dem Verknüpfen und nach dem Anlegen lesen | `id`, `roles.customer.number`, `company.name`, `company.vatRegistrationId`, `company.taxNumber`, `person.salutation`, `person.firstName`, `person.lastName`, `addresses.billing[0].street/zip/city` (ersatzweise `shipping`), `emailAddresses.*[]`, `phoneNumbers.*[]` (ohne `fax`), `archived` |
| `POST /v1/contacts` | Kunde in Lexware anlegen | `version: 0`, `roles: {customer: {}}`, `company: {name, vatRegistrationId, taxNumber}` oder `person: {salutation, firstName, lastName}`, `addresses.billing[{street, zip, city, countryCode: "DE"}]`, `emailAddresses.business[]`, `phoneNumbers.business[]` und `mobile[]`, `note`. Antwort: `id` |
| `POST /v1/invoices` (ohne `finalize`) | Rechnung als Entwurf | `archived: false`, `voucherDate`, `address: {contactId}`, `lineItems[{type: custom oder text, name, description, quantity, unitName, unitPrice: {currency: "EUR", netAmount, taxRatePercentage}, discountPercentage: 0}]`, `totalPrice: {currency: "EUR"}`, `taxConditions: {taxType: "net"}`, `paymentConditions: {paymentTermLabel, paymentTermDuration}`, `shippingConditions: {shippingDate, shippingEndDate, shippingType: service, serviceperiod oder none}`, `title`, `introduction`, `remark`. Antwort: `id` |
| `GET /v1/invoices/{id}` | Status und Beträge abgleichen | `voucherStatus` (draft, open, paid, paidoff, voided), `voucherNumber`, `voucherDate`, `dueDate` (ersatzweise `voucherDate` plus `paymentConditions.paymentTermDuration`), `totalPrice.totalNetAmount/totalGrossAmount/currency`, `title`, `address.contactId/name` |
| `GET /v1/voucherlist?voucherType=invoice&voucherStatus=open,paid,paidoff,voided,overdue&voucherNumber=` | vorhandene Rechnung über die Nummer finden | `content[].id`, `voucherStatus`, `voucherNumber`, `voucherDate`, `dueDate`, `totalAmount`, `currency`, `contactId`, `contactName` |

Zeitpunkte sendet EstrichONE als `yyyy-MM-ddT00:00:00.000+01:00` bzw. `+02:00` (Zeitzone Europe/Berlin). „Überfällig“
ist kein Status der Rechnung: EstrichONE leitet ihn aus Status `open` und überschrittener Fälligkeit ab.

Links in die Lexware-Oberfläche: `https://app.lexware.de/permalink/invoices/edit/{id}` (Entwurf),
`.../permalink/invoices/view/{id}` (alle anderen), `.../permalink/contacts/view/{id}`. Bei der früheren Basis-URL
`https://app.lexoffice.de`. Über `EstrichOne:Lexware:AppBaseUrl` fest einstellbar.

## Drosselung, Zeitlimit, Fehler

- Höchstens zwei Anfragen je Sekunde: ein gemeinsamer Takt für den ganzen Server (`MinRequestInterval`, Vorgabe 550 ms).
- `429`: Wiederholung mit wachsender Wartezeit (1 s, 2 s, 4 s, höchstens 8 s; `Retry-After` wird beachtet), insgesamt
  vier Versuche. Das gilt auch für schreibende Aufrufe, weil Lexware die Anfrage dann nicht verarbeitet hat.
- `502`, `503`, `504`, Netzwerkfehler und Zeitüberschreitung: Wiederholung nur bei lesenden Aufrufen. Schreibende Aufrufe
  werden nicht wiederholt, damit kein Kontakt und keine Rechnung doppelt entstehen.
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
| `GET settings/integrations/lexware/reconciliation` | `settings.integrations` | Abgleichübersicht (`refresh=true` lädt die Kontaktliste neu) |
| `POST settings/integrations/lexware/reconciliation/link-unique` | `settings.integrations` | eindeutige Treffer aus der Vorschau verknüpfen |
| `GET lexware/customers/{id}` | `invoices.read` | Verknüpfung eines Kunden |
| `GET lexware/customers/{id}/candidates` | `invoices.create` | Dublettenprüfung in Lexware, mit `q` gezielte Suche |
| `POST lexware/customers/{id}/link` | `invoices.create` | mit vorhandenem Lexware-Kontakt verknüpfen |
| `POST lexware/customers/{id}/create` | `invoices.create` | in Lexware anlegen; bei Treffern nur mit `createAnyway` |
| `DELETE lexware/customers/{id}/link` | `invoices.create` | Verknüpfung lösen |
| `PUT lexware/customers/{id}/sync` | `invoices.create` | „Mit Lexware Office abgleichen“ ein- oder ausschalten |
| `POST settings/integrations/lexware/reconciliation/enable-sync` | `settings.integrations` | Abgleich für ausgewählte Kunden einschalten |
| `GET lexware/contacts` | `invoices.create` | Lexware-Kunden seitenweise mit Suche (`q`, `page`, `size`, `unlinked`) |
| `GET lexware/contacts/{lexwareId}/candidates` | `invoices.create` | Dublettenprüfung in EstrichONE |
| `POST lexware/contacts/{lexwareId}/import` | `invoices.create` und `contacts.edit` | als Kunde übernehmen; bei Treffern nur mit `createAnyway` |
| `GET bauakten/{id}/lexware` | `invoices.read` | Rechnungen der Bauakte mit Statusabgleich (`refresh=true` sofort) |
| `GET bauakten/{id}/lexware/invoice-proposal` | `invoices.create` | Vorschlag für den Entwurf |
| `POST bauakten/{id}/lexware/invoices` | `invoices.create` | Entwurf anlegen und zuordnen |
| `GET bauakten/{id}/lexware/invoices/search?number=` | `invoices.create` | vorhandene Rechnung suchen |
| `POST bauakten/{id}/lexware/invoices/assign` | `invoices.create` | vorhandene Rechnung zuordnen |
| `DELETE bauakten/{id}/lexware/invoices/{linkId}` | `invoices.create` | Zuordnung lösen |

Bis auf Status und Einstellungen verlangen alle Endpunkte das eingeschaltete Modul `lexware` (Systemeinstellungen,
Module; nach der Installation aus). Die Rechte `invoices.read` und `invoices.create` erhält die Rolle „Büro und
Bauleitung“ nur bei neuen Installationen; in bestehenden Installationen vergibt sie die Administration.

## Schalter „Mit Lexware Office abgleichen“ (einseitige Kunden)

- Feld `lexwareSync` am Kunden (`contacts.lexware_sync`, `ContactResponse.lexwareSync`). Aus = der Kunde bleibt nur in
  EstrichONE: `link` und `create` antworten mit 409 `LEXWARE_SYNC_DISABLED`, ebenso der Rechnungsentwurf für einen
  unverknüpften Kunden. In der Abgleichübersicht steht er unter `excludedItems`, nicht unter `onlyEstrichOneItems`, und
  wird nicht als Dublette vorgeschlagen.
- Schreiben: `POST contacts` und `PUT contacts/{id}` mit `lexwareSync` (fehlt das Feld: beim Anlegen die Vorgabe, beim
  Ändern unverändert), Sync-Operationen `contact.create` und `contact.update` mit `lexwareSync`, dazu
  `PUT lexware/customers/{id}/sync` (`invoices.create`) und die Massenaktion
  `POST settings/integrations/lexware/reconciliation/enable-sync`.
- Vorgabe für neue Kunden: Einstellung `newCustomersSync` (Vorgabe ja), wirksam nur bei eingeschaltetem Modul. Clients
  lesen die wirksame Vorgabe aus `GET settings/modules` (`lexwareSyncDefault`, laden die Apps ohnehin) bzw.
  `GET lexware/status` (`syncDefault`).
- Migration: bestehende Kunden erhalten `false`. Solange das Modul aus ist, erhalten auch neue Kunden `false`. So wird
  kein Kunde ungefragt für die Übertragung vorgemerkt.
- Eine bestehende Verknüpfung bleibt beim Ausschalten erhalten und wird nur bewusst gelöst: Der Kontakt und seine
  Rechnungen liegen bereits in Lexware Office, ein stilles Lösen würde die Belege der Bauakten vom Kunden trennen.
  Rechnungen für einen verknüpften Kunden bleiben deshalb möglich; die Oberfläche zeigt den Zustand an.
- Aus Lexware übernommene Kunden erhalten `true`.

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
4. `POST /v1/invoices` mit `address: {contactId}` ohne weitere Adressfelder: übernimmt Lexware Name und Anschrift des
   Kontakts? Werden `unitName` „m²“ und „Stück“ sowie `taxRatePercentage` 0, 7, 19 angenommen? Textzeilen (`type: text`).
5. `GET /v1/invoices/{id}`: Entwurf ohne `voucherNumber`, Feld `dueDate`, Statuswerte.
6. `GET /v1/voucherlist`: Filter `voucherNumber`, Werteliste für `voucherStatus`, Feld `totalAmount`.
7. Links `permalink/invoices/edit`, `permalink/invoices/view`, `permalink/contacts/view` in der Lexware-Oberfläche.
8. Fehlerformat bei 406 und 422 (Felder `details[]` bzw. `IssueList[]`) und Kopfzeile `Retry-After` bei 429.
9. Kleinunternehmer und abweichende Steuerarten (`taxConditions.taxType`): derzeit immer `net`.

## Nicht Teil dieser Runde

Angebote und Auftragsbestätigungen, Webhooks (Statusänderungen ohne Abruf), Abruf des Rechnungs-PDF in die Bauakte,
Abschlagsrechnungen, Rechnungen und Verknüpfung in den Apps (dort gibt es nur den Schalter „Mit Lexware Office
abgleichen“ am Kunden), Abgleich geänderter Kundendaten.
