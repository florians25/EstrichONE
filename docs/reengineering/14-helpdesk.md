# 14 – EstrichONE Helpdesk

**Status heute:** nicht vorhanden. **Ziel:** ein kleines internes Ticketsystem für IT-Probleme. Es nutzt
dieselbe Benutzer-, Geräte- und Dateiverwaltung wie der Rest von EstrichONE.

## 1. Datenmodell

```
tickets
  id (uuid, Client-generiert → offline anlegbar), nummer (T-JJJJ-NNNN), titel, beschreibung,
  kategorie, prioritaet (niedrig|normal|hoch|dringend), status, erstellt_von, betroffener_benutzer,
  betroffenes_geraet (devices.id, optional), bauakte_id (optional), zugewiesen_an, erstellt_am,
  geloest_am, loesung (Text), quelle (web|desktop|ios|android)
ticket_comments   id, ticket_id, autor, text, intern (bool), erstellt_am
ticket_files      → files (gleiche Ablage, gleiche Upload-Warteschlange wie Fotos)
ticket_diagnostics id, ticket_id, payload_json (nur freigegebene Felder), vom_benutzer_bestaetigt_am
ticket_events     Statuswechsel, Zuweisung, Priorität (Verlauf)
```

**Kategorien (Vorgabe, im Web anpassbar):** Anmeldung · Synchronisation · App-Fehler · Gerät defekt ·
Drucker/Büro-IT · Zugang/Rechte · Sonstiges.

**Status:** Neu → In Bearbeitung → Rückfrage (wartet auf Melder) → Gelöst → Geschlossen
(automatisch 7 Tage nach „Gelöst“ ohne Widerspruch). „Wieder öffnen“ ist aus „Gelöst“ möglich.

## 2. Ticketanlage – einfach

**Mobil (*Mehr › Problem melden*, außerdem aus jeder Fehlermeldung heraus über „Problem melden“):**

```
Problem melden
Was ist passiert? *  [ Fotos werden nicht übertragen                    ]
Details              [ seit heute morgen, Fahrzeug 3 …                  ]
Kategorie            [Synchronisation ▾]   (aus dem Kontext vorbelegt)
[📷 Bildschirmfoto/Foto anhängen]
[✓] Technische Informationen mitsenden   ▸ Anzeigen, was gesendet wird
                                             [Senden]
```

- **Pflicht ist nur der Titel.** Gerät und Benutzer füllt die App automatisch aus. Priorität setzt der
  Bearbeiter, nicht der Melder (Melder können „dringend – ich kann nicht arbeiten“ ankreuzen).
- **Offline:** Das Ticket wird als Entwurf bzw. Outbox-Operation gespeichert und automatisch gesendet.
  Ein Ticket über „Anmeldung funktioniert nicht“ muss **ohne Anmeldung** möglich sein → siehe Abschnitt 5.
- **Web/Desktop:** gleiches Formular als Seitenpanel; ein Bildschirmfoto lässt sich aus der Zwischenablage
  einfügen (Strg+V).

## 3. Diagnosedaten

**„Problem mit Diagnosedaten melden“** – die Daten werden **vor dem Senden angezeigt**. Der Benutzer kann sie
abwählen.

| Erlaubt | Beispiel |
|---|---|
| App-Version, Build, Plattform, OS-Version, Gerätemodell | `EstrichONE 1.0.4 (112) · iOS 26.1 · iPhone16,2` |
| Gerätetyp, Geräte-ID (intern) | `Pool · 7d1e…` |
| Fehlercode(s) und Zeitpunkt | `SYNC_UPLOAD_FAILED 2026-10-19T07:12:03Z` |
| Synchronisationsstatus | `ausstehend: 14 Vorgänge, 23 Dateien (412 MB); ältester: 2026-10-17` |
| Netzwerkzustand | `Mobilfunk, eingeschränkt` |
| Speicher frei | `1,2 GB` |
| Technisches Protokoll der letzten 24 h, **bereinigt** | Ereigniscodes und Zeiten; IDs gekürzt; **keine** Inhalte |

**Nie enthalten** (technisch ausgeschlossen, nicht nur per Richtlinie): Passwörter, PINs, Tokens,
Schlüssel, Inhalte von Bauakten, Notizen, Fotos, Namen von Kunden und Adressen. Das Protokoll schreibt
diese Daten gar nicht erst. Der Logger hat für Fachinhalte keine Schnittstelle. Ein Test prüft, dass
Testdaten mit bekannten Zeichenfolgen nicht im Diagnosepaket landen.

## 4. Bearbeitung (Web, Desktop – Recht `helpdesk.agent`)

- **Liste:** Filter nach Status, Kategorie, Priorität, „Mir zugewiesen“, „Nicht zugewiesen“. Spalte „wartet seit“.
- **Detail:** Verlauf (Kommentare öffentlich/intern, Ereignisse), Anhänge, Diagnosedaten (aufklappbar),
  Seitenleiste mit Gerät (Link ins IT Center, inkl. der dortigen Aktionen), Benutzer, Bauakte.
- **Aktionen:** Zuweisen, Status, Priorität, Kommentar (öffentlich), **interne Notiz** (für den Melder
  unsichtbar), Lösung dokumentieren (Pflicht beim Schließen; Textbaustein-Vorschläge aus früheren Lösungen
  derselben Kategorie **[1.1]**), Gerät verknüpfen, Anhänge verwalten.
- **Benachrichtigung des Melders** bei öffentlichem Kommentar, Rückfrage und Lösung: In-App, optional
  E-Mail über den Betreiber-SMTP. Keine externen Dienste.

## 5. Problem melden ohne Anmeldung

Gilt auf einem eingerichteten Gerät, z. B. wenn die Anmeldung scheitert. Das Gerät ist dem Server bekannt und
signiert die Anfrage mit seinem Geräteschlüssel (11). `POST /tickets/device` nimmt nur von **registrierten,
nicht gesperrten** Geräten Tickets an. Die Anfrage ist gedrosselt (5 pro Stunde), hat keine Leserechte und
erzeugt kein Konto. Als Melder erscheint „Gerät *Fahrzeug 3*, Benutzer laut Eingabe: …“.

## 6. Version

Ticketanlage und Bearbeitung im Web in **1.0** (geringer Aufwand, hoher Nutzen beim Rollout der Apps).
Bearbeitung im Desktop mit diesem. Statistik, SLA und Textbausteine in 1.1.

## 7. Akzeptanzkriterien

- **AK-1:** Ein offline erstelltes Ticket mit Foto erscheint nach Wiederverbindung genau einmal.
- **AK-2:** Interne Notizen sind über die API für den Melder nicht abrufbar (Berechtigungstest).
- **AK-3:** Das Diagnosepaket enthält bei einem Test mit präparierten Bauakten-, Kunden- und Tokendaten
  keine dieser Zeichenfolgen.

## 8. Ausbau zum Service-Desk (1.1)

- **Ansichten und Liste:** Meine, Nicht zugewiesen, Überfällig, Wartet auf Rückmeldung, Alle offenen, Meine Gruppen,
  Erledigt, Alle; Volltextsuche (Nummer, Titel, Beschreibung, Melder, Antworten), Sortierung je Spalte,
  Sammelbearbeitung (`POST /tickets/bulk`, je Ticket gespeichert, Fehler je Ticket zurück).
- **SLA:** Reaktions- und Lösungsziel je Priorität in Geschäftszeit (`helpdesk_settings`, Standard 1/4, 4/16, 8/40,
  24/80 Stunden, Mo–Fr 07:00–17:00, Europe/Berlin, Feiertage). „Rückfrage“ hält die Uhr an; die angehaltene Geschäftszeit
  verschiebt das Lösungsziel. Erste Antwort = erste öffentliche Antwort der Bearbeitung oder Rückfrage/Lösung. Die Wartung
  meldet überschrittene Ziele einmal an Zuständige, sonst Gruppe, sonst alle Bearbeiter (`ticket.overdue`).
- **Gruppen, Unterkategorien, Regeln:** erste passende Regel (Kategorie, Unterkategorie, Stichwort) setzt Gruppe,
  Person (fest oder „wenigste offene Tickets“) und Priorität, wenn der Melder keine gewählt hat.
- **Zusammenführen und Verknüpfen:** Duplikat wird geschlossen, sein Melder wird Beobachter des Ziels (`watcher_user_ids`),
  Beschreibung und Anhänge als interne Notiz. Verknüpfungen „hängt zusammen“/„Duplikat“ (`ticket_links`).
- **Zeiterfassung** (`ticket_time_entries`), **Textbausteine** (`ticket_canned_responses`, gemeinsam oder persönlich),
  **Erwähnungen** (`mention_user_ids`, nur Bearbeiter), **Prüfpfad** aus `ticket_events`.
- **Wissensdatenbank** (`knowledge_articles`): veröffentlicht für alle, Entwürfe für die Bearbeitung; Vorschläge beim
  Melden (`GET /helpdesk/articles/suggest`).
- **Kontext:** Melder, seine Geräte mit App-Version und Update-Stand (`IAppUpdatePolicy.EvaluateAsync`), weitere Tickets.
- **Bewertung** nach der Lösung (1–5, nur Melder). **Kennzahlen** über `GET /tickets/stats`.
- **Rechte:** `helpdesk.agent` (Bearbeitung, Textbausteine, Wissensdatenbank), neu `helpdesk.manage` (Ziele, Gruppen,
  Unterkategorien, Regeln). Die Standardrolle IT-Administration erhält `helpdesk.manage` per Migration.
