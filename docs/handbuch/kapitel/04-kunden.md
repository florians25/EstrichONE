# Kunden, Anrufe und Anfragen

## Kundenstamm

**Büro und Stammdaten** → **Kunden** ist der Kundenstamm für Bauakten und Anfragen – Privatkunden ebenso wie Firmen,
Bauträger, Architekten und Gemeinden.

![Kundenliste](bilder/kunden.png "Der Kundenstamm mit Filter Firmen/Privat, Sortierung und Suche")

- Die Suche findet Name, Kundennummer, Ort, Telefon, E-Mail und Ansprechpartner.
- **Alle**, **Firmen** und **Privat** filtern nach Art; sortiert wird nach Name, Kundennummer, Ort oder letzter Änderung.
- **Neuer Kunde** legt einen Kunden an. Auch hier prüft EstrichONE beim Speichern auf Dubletten.
- **CSV importieren** übernimmt einen vorhandenen Kundenbestand, z. B. aus der bisherigen Software.
- **Serien-E-Mail** öffnet eine E-Mail an alle Kunden, die Sie mit dem Häkchen markiert haben.
- **Bauakten** in einer Zeile springt zu den Bauakten dieses Kunden.
- Die Auswahl **Aktive / Archivierte / Alle (auch inaktive)** blendet archivierte Kunden ein. Standardmäßig sind nur
  aktive Kunden zu sehen.
- Das Papierkorb-Symbol in einer Zeile bzw. **Löschen/Archivieren** in der Werkzeugleiste (für alle mit Häkchen
  markierten Kunden) löscht oder archiviert Kunden – siehe unten.

### Kundenakte

Ein Klick auf den Namen öffnet die **Kundenakte**. Im Kopf stehen Kundennummer, Anschrift und Telefon sowie
Schaltflächen für **Anrufen**, **E-Mail**, **Bearbeiten** und **Neue Bauakte** (mit diesem Kunden vorbelegt).

![Kundenakte](bilder/kunde-detail.png "Die Kundenakte mit Stammdaten, Bauakten, Dokumenten und Kontakten")

| Abschnitt | Inhalt |
|---|---|
| **Übersicht / Stammdaten** | Art, Anrede und Titel, Geburtstag, bevorzugter Kontaktweg, Website, Rechnungs- und Lieferanschrift, USt-IdNr., Steuernummer, Notiz |
| **Bauakten** | Alle Bauakten des Kunden mit Status – auch Altakten |
| **Dokumente** | Alle Dokumente aus allen Bauakten des Kunden, gegliedert nach Bauakte und Dokumentart; ein Klick öffnet die Vorschau |
| **Kontakte** | Weitere Telefonnummern, E-Mail-Adressen und Ansprechpartner |
| **Verlauf** | Änderungen am Kunden |

### Kunden löschen, archivieren oder anonymisieren {#kunden-loeschen}

**Löschen/Archivieren** in der Kundenakte (oder das Papierkorb-Symbol in der Kundenliste) prüft zuerst, ob am Kunden
noch Daten hängen: Bauakten (auch im Papierkorb), Projektbeteiligungen in fremden Bauakten sowie Anrufe und Anfragen.

| Situation | Was EstrichONE anbietet |
|---|---|
| **Keine verknüpften Daten** (z. B. versehentlich oder doppelt angelegt) | **Endgültig löschen**: Zur Bestätigung tippen Sie die **Kundennummer** ein (ohne Nummer den Namen). Gelöscht werden Stammdaten, Telefonnummern, E-Mail-Adressen, Notiz und Ansprechpartner. Die Kundennummer wird **nie wieder vergeben**. Alternativ: **Stattdessen archivieren**. |
| **Verknüpfte Daten vorhanden** | Löschen ist gesperrt. Der Dialog nennt die Verknüpfungen mit Anzahl je Art (z. B. „2 Bauakten und 1 Anruf oder Anfrage“) und verlinkt die Bauakten. Stattdessen **Archivieren**. |

**Archivieren** setzt den Kunden inaktiv (neue Bauakten, Anfragen oder Projektbeteiligungen mit ihm lehnt EstrichONE ab: „Kunde ist archiviert – zuerst reaktivieren“; die Dublettenprüfung bietet dafür **Reaktivieren und verwenden**): Er bleibt mit allen Bauakten, Dokumenten und Anrufen erhalten, erscheint aber
nicht mehr in Auswahllisten (Bauaktenanlage, Anrufe, Kundensuche) und in der Kundenliste nur unter **Archivierte**. Die
Kundenakte zeigt dann den Hinweis „Archiviert“; **Reaktivieren** macht den Kunden wieder aktiv. In den Apps verschwindet
ein gelöschter Kunde beim nächsten Abgleich; archivierte Kunden zeigt die App nur mit **Archivierte anzeigen**.

Mit der Mehrfachauswahl (Häkchen) und **Löschen/Archivieren** erledigen Sie mehrere Kunden auf einmal: Kunden ohne
verknüpfte Daten werden gelöscht, alle übrigen archiviert. Zur Bestätigung tippen Sie die Anzahl ein.

**Anonymisieren (DSGVO):** Verlangt ein Kunde mit Altdaten die Löschung seiner personenbezogenen Daten, ersetzt
**Anonymisieren …** Name, Anschriften, Telefon, E-Mail, Notiz und Ansprechpartner durch „Anonymisierter Kunde
600123“ – auch in den Projektbeteiligten der Bauakten, in Anrufnotizen (Name, Telefon, E-Mail des Anrufers) und im
E-Mail-Versandprotokoll. Bauakten, Dokumente und Kundennummer bleiben erhalten. Dafür tippen Sie die Kundennummer ein
und bestätigen ausdrücklich, dass sich der Schritt nicht rückgängig machen lässt.

| Recht | Wer es standardmäßig hat | Erlaubt |
|---|---|---|
| `contacts.edit` | Administrator, Büro und Bauleitung | Archivieren und Reaktivieren |
| `contacts.delete` | Administrator, Büro und Bauleitung | Kunden ohne verknüpfte Daten löschen |
| `contacts.anonymize` | nur Administrator | Kunden anonymisieren |

Jeder Schritt steht mit Kundennummer (ohne Namen) im **Protokoll** (`contact.deleted`, `contact.archived`,
`contact.reactivated`, `contact.anonymized`).

> **Tipp:** Pflegen Sie Telefonnummern im Kundenstamm vollständig. Dann erkennt die Dublettenprüfung bekannte Anrufer
> auch dann, wenn der Name anders geschrieben wird.

## Anrufe und Telefon-Schnellerfassung {#anrufe}

Während eines Gesprächs zählt jede Sekunde. Klicken Sie oben in der Kopfzeile auf das **Telefon**-Symbol: Die
**Telefon-Schnellerfassung** öffnet sich mit laufender Gesprächsdauer.

1. **Wer ruft an?** – Name, Firma oder Telefonnummer; bekannte Kunden werden vorgeschlagen.
2. **Rückrufnummer**, **Richtung** (**Eingehend** oder **Ausgehend**) und eine **Notiz**: Worum geht es? Was wurde
   besprochen?
3. Optional **Kunde zuordnen**, **Bauakte zuordnen** und eine **Wiedervorlage am** festlegen.
4. **Speichern (Strg+Enter)** – oder **Als Anfrage speichern**, wenn ein Interessent ein neues Bauvorhaben anfragt.

Der Zeitpunkt des Gesprächsbeginns wird übernommen; Details ergänzen Sie später.

**Büro und Stammdaten** → **Anrufe** listet alle Gesprächsnotizen. Fällige Wiedervorlagen sind orange markiert; **Nur
fällige Wiedervorlagen** blendet den Rest aus. Je Eintrag stehen **Erledigt**, **Als Aufgabe in die Bauakte**,
**Bearbeiten** und **Löschen** bereit. **Anruf mit allen Feldern** erfasst einen Anruf ausführlich, **Liste drucken**
erstellt eine PDF-Liste.

![Anrufe](bilder/anrufe.png "Gesprächsnotizen mit Wiedervorlage")

Die Zahl am Telefon-Symbol in der Kopfzeile zeigt, wie viele Wiedervorlagen heute fällig sind.

## Anfragen

**Auftragsbearbeitung** → **Anfragen** führt Anfragen als Vorgänge: **Offen** → **In Bearbeitung** → **Bauakte
erstellt** (oder abgebrochen). Jede Anfrage enthält Eingang, Anfragenden, Anliegen, Ort, Fläche, Wunschtermin und den
Kanal (Telefon, E-Mail, Website, persönlich).

![Anfragen](bilder/anfragen.png "Offene Anfragen mit Fläche und Kanal")

- **Anfrage erfassen** legt eine Anfrage an; aus der Telefon-Schnellerfassung entstehen Anfragen mit **Als Anfrage
  speichern**.
- Ein Klick öffnet die Anfrage. Dort setzen Sie den Status und übernehmen die Angaben mit einem Klick in eine **Bauakte**
  (Status Anfrage oder direkt als Auftrag).
- **Offen und in Bearbeitung** bzw. **Alle** schaltet die Liste um.

> **Hinweis:** Bauakten im Status **Anfrage** sind etwas anderes als Vorgänge unter **Anfragen**: Eine Anfrage ist der
> erste Kontakt; sobald Sie eine Bauakte daraus erstellen, ist die Anfrage erledigt und die weitere Arbeit läuft in der
> Bauakte.
