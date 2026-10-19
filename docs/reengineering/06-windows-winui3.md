# 06 – EstrichONE Desktop (Windows, WinUI 3)

**[Verbindlich]** C#/.NET, WinUI 3, Windows App SDK. Kein WPF, Electron, Tauri oder WebView als Hauptoberfläche.
**Status:** nicht vorhanden, Neuentwicklung.

## 1. Rolle des Desktops

Das Büro arbeitet am Desktop mit **hoher Informationsdichte**: Bauakten in Tabellen, Disposition der Kolonnen,
Stammdaten, Druck. Geräteverwaltung und Helpdesk-Bearbeitung kommen dazu. Die Systemadministration
(Benutzer, SSO, Backup) bleibt **im Web**. Der Desktop verlinkt dorthin, statt sie doppelt zu implementieren.
Das hält den Wartungsaufwand beherrschbar.

| Im Desktop | Nur im Web (Desktop öffnet den Browser) |
|---|---|
| Bauakten, Suche, Akte mit allen Reitern, Druck/PDF | Benutzer & Rollen, Entra-SSO-Konfiguration |
| Einsatzplanung (Woche/Tag), Kalender | Backup Center, Systemeinstellungen |
| Kunden, Kontakte, Ressourcen, Materialbestellungen, Anrufe/Anfragen | Audit-Log |
| IT Center: Geräteliste und Freigabe, Helpdesk-Bearbeitung | Geräte-Richtlinien bearbeiten |

## 2. Ribbon

### 2.1 Komponentenprüfung

| Option | Bewertung |
|---|---|
| Ribbon aus den **Windows Community Toolkit Labs** | Entspricht dem Fluent-Ribbon-Muster. Labs-Komponenten sind **experimentell** und haben keine Stabilitätszusage. Vor Projektstart prüfe ich erneut, ob sie in das stabile Toolkit übernommen wurden. |
| Kommerzielle Ribbons (Syncfusion, DevExpress, Telerik) | ausgereift, aber kostenpflichtig oder mit Umsatzgrenzen in der Community-Lizenz → widerspricht „keine kostenpflichtigen Pflichtabhängigkeiten“ |
| **Eigenes `RibbonControl` aus nativen WinUI-Komponenten** | **[Vorschlag]**, falls das Labs-Ribbon nicht stabil ist: überschaubarer Umfang (~1.500 Zeilen), vollständig kontrollierbar |

### 2.2 Aufbau des eigenen Ribbons

- **Registerleiste:** `SelectorBar` bzw. `TabView`-ähnlicher Kopf mit echten `RadioButton`-Semantiken für die
  Bedienungshilfen (UIA `TabItem`).
- **Gruppen:** `RibbonGroup` = `StackPanel` mit `AppBarButton`, `AppBarToggleButton`, `SplitButton` und
  `DropDownButton`, Gruppenbeschriftung unten. Bei schmalem Fenster klappen Gruppen schrittweise zu
  `DropDownButton`s zusammen (Priorität je Gruppe).
- **Kontextregister:** erscheinen farbig markiert nur bei passender Auswahl, z. B. „Bauakte“, wenn eine Akte
  geöffnet ist, und „Foto“, wenn Fotos ausgewählt sind.
- **Schnellzugriffsleiste** in der Titelleiste (`AppWindow.TitleBar`, Extend-into-Titlebar): Speichern,
  Rückgängig, Neue Bauakte, Synchronisieren. Konfigurierbar.
- **Tastatur:** Alt zeigt `KeyTip`s (WinUI unterstützt `KeyTipPlacementMode` nativ); F6 wechselt zwischen
  Ribbon, Navigation und Inhalt.
- **Minimieren:** Doppelklick auf das Register bzw. Strg+F1.
- **Befehlsmodell:** Alle Befehle sind `ICommand`s aus einem zentralen `CommandRegistry`. Das Ribbon,
  Kontextmenüs, Tastenkürzel und die Befehlspalette (Strg+K) nutzen dieselbe Instanz, deshalb ist ein Befehl
  überall gleichzeitig aktiv oder deaktiviert.

### 2.3 Registerstruktur (abgeleitet aus dem Funktionskatalog)

| Register | Gruppen |
|---|---|
| **Start** | Neu (Bauakte, Kunde, Anruf) · Zwischenablage · Ansicht (Dichte, Spalten) · Suchen · Synchronisieren |
| **Bauakten** | Öffnen/Neu · Status · Zuständigkeit · Export/Druck · Papierkorb |
| **Planung** | Woche/Tag/Kalender · Einsatz neu · Kolonne · Konflikte · Drucken (Wochenplan) |
| **Büro** | Kunden · Anrufe & Anfragen · Materialbestellungen · Lieferanten |
| **Ressourcen** | Mitarbeiter · Kolonnen · Fahrzeuge · Pumpen · Maschinen · Abwesenheiten |
| **IT & Support** | Geräte · Freigaben · Tickets · Problem melden · Verwaltung im Browser öffnen |
| Kontext **Bauakte** | Notiz · Aufgabe/Mangel · Foto/Dokument hinzufügen · Räume · Erstbegehung · Bautagebuch · PDF |
| Kontext **Tabelle** | Gruppieren · Filter speichern · Spalten · Export CSV |

„Dokumentation“ und „Mitarbeiter“ aus dem Vorschlag im Master-Prompt sind in „Bauakten“ (Kontextregister)
und „Ressourcen“ aufgegangen, weil Dokumentation immer zu einer Bauakte gehört.

## 3. Fensterstruktur

```
┌ Titelleiste: Logo · Schnellzugriff · Suchfeld (Strg+K) ·············· Benutzer · Sync-Status ┐
├ Ribbon ──────────────────────────────────────────────────────────────────────────────────────┤
├ NavigationView (links, kompakt) │ Inhalt: Liste │ Detail (Master-Detail, Splitter speicherbar) ┤
└ Statusleiste: Verbindung · Server · ausgewählte Zeilen · letzte Synchronisation · Version ─────┘
```

- **Mehrfenster:** Eine Bauakte kann in ein eigenes Fenster gelöst werden (Kontextmenü, Ziehen des Reiters,
  Strg+Umschalt+Enter). Die Fensterpositionen werden je Monitor gespeichert. Der Wochenplan lässt sich auf
  einem zweiten Monitor öffnen.
- **Tabellen:** **[Offen] E-11** WinUI.TableView (MIT) oder eigene `ItemsRepeater`-Tabelle.
  Anforderungen: Virtualisierung (10.000 Zeilen flüssig), Sortierung, Filter je Spalte, Gruppierung,
  Spaltenreihenfolge und -breite speicherbar, Mehrfachauswahl, Kopieren als TSV.
- **Drag-and-drop:** Dateien aus dem Explorer in eine Bauakte ziehen (`DataPackage` → Upload-Warteschlange).
  Im Wochenplan Einsätze per Ziehen verschieben (mit Konfliktprüfung).
- **Druck:** Für Berichte werden serverseitig erzeugte PDFs angezeigt und über den Windows-Druckdialog
  gedruckt. So ist der Druck auf allen Plattformen identisch. Den Wochenplan gibt der Desktop als eigene
  `PrintDocument`-Ansicht aus.
- **Native Dialoge:** `ContentDialog`, `FileOpenPicker`/`FileSavePicker` (mit Fenster-Handle).
- **Benachrichtigungen:** `AppNotificationManager` (Windows App SDK), z. B. bei neuen Tickets oder
  Einsatzänderungen; nur während die App läuft, ohne eigenen Dienst.
- **DPI, Mehrmonitor, Hell/Dunkel, Kontrastmodi:** von WinUI unterstützt. Testfälle stehen in 19.
- **Barrierefreiheit:** `AutomationProperties.Name` für alle Symbolschaltflächen, Prüfung mit Accessibility
  Insights.

## 4. Anmeldung und Sitzung

- Server festlegen: Erststart-Assistent analog zu 10, mit Serveradresse oder Einrichtungsdatei bzw. Link.
- Lokale Anmeldung oder Entra über MSAL.NET mit **WAM**: nutzt das Windows-Konto, wenn das Gerät in Entra
  eingebunden ist.
- Refresh-Token wird mit **DPAPI** (`ProtectedData`, Benutzerbereich) bzw. `PasswordVault` gespeichert.
  Die Entsperrung kann optional über **Windows Hello** erfolgen.
- **Offline:** Der Desktop ist ein Online-Client mit Lese-Cache (letzte geöffnete Bauakten). Volle
  Offline-Bearbeitung ist im Büro nicht nötig und würde den Aufwand verdoppeln. Ausnahme: Uploads laufen über
  eine dauerhafte Warteschlange.

## 5. Verteilung und Updates

| Weg | Bewertung |
|---|---|
| **MSIX + `.appinstaller`-Datei auf dem eigenen EstrichONE-Server** | **[Vorschlag]**. Updates werden beim Start geprüft; die Windows-App-SDK-Runtime wird als Abhängigkeit automatisch nachgeladen. |
| Signierung | MSIX **muss signiert sein**. Optionen: (a) eigenes Zertifikat, das auf den Firmen-PCs als vertrauenswürdig installiert wird (kostenlos, per Gruppenrichtlinie oder Skript verteilbar); (b) Signaturdienst wie Azure Artifact Signing / Trusted Signing (kostenpflichtig, monatlich); (c) klassisches Code-Signing-Zertifikat (kostenpflichtig). Für Drittbetriebe ist (a) mit einem mitgelieferten Skript der kostenlose Weg. Das ist transparent zu dokumentieren. |
| Microsoft Store | möglich (Entwicklerkonto für Einzelpersonen inzwischen kostenlos), aber für eine selbst gehostete Firmen-Software ohne Mehrwert; **optional** |
| Unverpackte Variante (self-contained, ZIP) | als Notlösung für Umgebungen ohne MSIX-Freigabe |

**Mindestversion [Vorschlag]:** Windows 10 22H2 und Windows 11. Windows 10 ist seit Oktober 2025 ohne
regulären Support; ob es unterstützt wird, entscheidet die Gerätebasis der Betriebe. **[Offen] E-16.**

## 6. Projektstruktur

```
desktop/
  EstrichOne.Desktop/          WinUI 3 App (Views, ViewModels, Ribbon, Services)
  EstrichOne.Desktop.Ribbon/   Ribbon-Steuerelement (eigene Bibliothek, testbar)
  EstrichOne.Desktop.Tests/    ViewModel-Tests (xUnit)
contracts/EstrichOne.Contracts gemeinsam mit dem Backend
```

## 7. Aufwand und Risiken

- **Aufwand:** 60–90 Personentage bis zur Funktionsgleichheit mit dem Web für die Büroarbeit.
- **R-1:** Der Desktop dupliziert vieles aus dem Web. Ohne klaren Mehrwert (Ribbon-Effizienz, Mehrfenster,
  Drag-and-drop, Druck) ist er für einen Einzelentwickler die teuerste Plattform je Nutzen.
  → In der Roadmap **zuletzt** und mit Prüfpunkt (20).
- **R-2:** Die Tabellenkomponente ist die größte technische Unsicherheit → früher Prototyp (Spike) vor
  Projektbeginn.
