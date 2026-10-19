# 02 – Funktionskatalog

Die Klassifizierung jeder bestehenden Funktion und Tabelle (Spalte **Entscheidung**) folgt den Vorgaben:
**Beibehalten · Verbessern · Zusammenführen · Entfernen · Neu entwickeln · Vormerken (spätere Version).**

Spalte **Plattform:** W = Web, D = Windows Desktop, M = Mobile (iOS und Android). **Fett** = Schwerpunktplattform.

Spalte **Status heute:** ✅ vorhanden · ⚠️ unvollständig · ❌ Platzhalter · – nicht vorhanden. Belege stehen in 01.

> **Bitte prüfen:** Die Spalte „Entscheidung“ ist ein **Vorschlag**. Ob eine Funktion im Betrieb tatsächlich
> genutzt wird, kann ich nur aus dem Code schließen. Eine Abfrage der Produktivdaten (siehe 18, Schritt M-1)
> zeigt, welche Tabellen leer sind. Dieses Ergebnis sollte die Liste vor Version 1.0 bestätigen.

## 1. Bauakten (Kern)

| Funktion | Status heute | Entscheidung | Version | Plattform | Begründung |
|---|---|---|---|---|---|
| Bauakte mit Übersicht, Kontakten, Notizen, Fotos, Videos, Dokumenten | ✅ | **Verbessern** | 1.0 | **W D M** | Kernfunktion; Objektrechte und Offlinefähigkeit fehlen |
| Bauakte + Baustelle (`projects` + `construction_sites`) | ✅ doppelt | **Zusammenführen** | 1.0 | – | Eine Bauakte hat eine Baustellenadresse. Mehrere Bauabschnitte werden über **Bereiche** abgebildet (Haus A/B, Geschoss). Siehe 03, E-05 |
| Bauaktennummer `JJJJ-NNN` | ✅ | Beibehalten | 1.0 | – | wird erst bei Synchronisation vergeben, offline vorläufig |
| Neue Bauakte – Büro (Assistent) | ✅ 5 Schritte | **Verbessern** | 1.0 | **W D** | eine Seite mit Abschnitten statt Schrittfolge |
| **Neue Bauakte – Baustelle (Schnellanlage)** | – | **Neu entwickeln** | 1.0 | **M** | höchste Priorität, siehe 09 |
| Bauaktensuche | ✅ | Verbessern | 1.0 | W D M | Volltext (PostgreSQL), offline auf lokalen Daten |
| Notizen im Ticket-Stil, anheften, einfärben | ✅ | Beibehalten | 1.0 | W D M | bewährt; offline als reine Anfüge-Operation konfliktfrei |
| Hinweiszeilen in der Akte (Einsatz heute, Erstbegehung offen …) | ✅ | Beibehalten | 1.0 | W D M | serverseitig berechnet, damit alle Plattformen gleich anzeigen |
| Präsenz „Aktuell in dieser Akte aktiv“ | ✅ | Vormerken | 1.1 | W D | nützlich, für 1.0 nicht nötig |
| Status, Stornierung, Löschen mit Passwortbestätigung | ✅ | Verbessern | 1.0 | W D | Rechte statt Rollencodes; Löschen nur als Papierkorb mit Frist (siehe 17) |
| Verlauf/Historie der Bauakte | ✅ (`/projects/{id}/history`) | Verbessern | 1.0 | W D M | aus dem Audit-Log abgeleitet |
| Räume & Flächen, Geschosse, Flächenabgleich | ✅ | Beibehalten | 1.0 | **W D** M | estrichspezifisch |
| Estrichaufbau je Raum, Estricharten, Komponenten | ✅ | Beibehalten | 1.0 | W D | Stammdaten im Büro |
| Erstbegehung (Assistent, Zähler-/Anmischfotos, „nicht notwendig“) | ✅ | Verbessern | 1.0 | **M** W | offlinefähig, Fotos über die Upload-Warteschlange |
| CM-Messungen | ✅ (generisch) | Verbessern | 1.0 | **M** W | eigene Erfassungsmaske statt generischem Formular |
| Prüfungen allgemein (`measurements`) | ⚠️ | Zusammenführen | 1.0 | – | mit CM-Messung/Erstbegehung zu „Prüfungen“ |
| Erstbegehungs-Vorlagen, Checklisten-Vorlagen, Workflows | ⚠️ | Zusammenführen | 1.1 | W | eine Vorlagenverwaltung für Prüfungen |
| Tagesdokumentation | ✅ | **Verbessern** → „Bautagebuch“ | 1.0 | **M** W | Wetter optional, offline |
| Abnahmen | ⚠️ (generisch) | Verbessern | 1.1 | M W | mit Unterschrift; PDF serverseitig |
| Fotos: Galerie, Varianten, Kategorien | ✅ | Verbessern | 1.0 | W D **M** | Prüfsumme, fortsetzbarer Upload, Original optional |
| Fotoannotationen (`photo_annotations`) | ❌ | Vormerken | 1.2 | M | |
| Dokumentversionen | ❌ | Vormerken | 1.2 | W | |
| Videos | ✅ | Beibehalten | 1.0 | M W | Größenlimit, Upload in Teilen |
| Material in der Bauakte, Materialbestellungen, Lieferanten | ✅ | Beibehalten | 1.0 | **W** M (lesen) | |
| Mängelmanagement (`defects`) | ⚠️ | **Zusammenführen** mit Aufgaben | 1.0 | W D M | eine Aufgabe mit Typ „Mangel“ statt zweier Systeme |
| Aufgaben | ✅ | Verbessern | 1.0 | W D M | Zuweisung, Fälligkeit, offline |
| Qualitätsprotokolle, Gewährleistung | ❌ | Vormerken | 2.0 | W | ohne erkennbaren Ablauf |
| Projektphasen | ❌ | Entfernen | – | – | Status reicht |

## 2. Planung und Ressourcen

| Funktion | Status heute | Entscheidung | Version | Plattform | Begründung |
|---|---|---|---|---|---|
| Kolonnen-Wochenplaner | ✅ | Verbessern | 1.0 | **W D** | Kern der Disposition |
| Tageseinteilung | ✅ | Zusammenführen | 1.0 | W D | als Tagesansicht der Einsatzplanung |
| Plantafel, Plantafel-Ansichten, Planungskonflikte (Tabellen) | ⚠️ | Zusammenführen/Entfernen | 1.0 | – | Konflikte werden berechnet, nicht gespeichert |
| Arbeitsplan (Mitarbeitersicht) | ✅ | Verbessern | 1.0 | **M** W | „Mein Einsatz heute“ auf dem Startbildschirm |
| Kalender (Google-Stil) | ✅ | Beibehalten | 1.0 | W D (M lesen) | |
| Kalender-Board (alt) | ⚠️ | Entfernen | – | – | Doppelung |
| Externer Link „Kalender (Google)“ | ✅ | Entfernen | – | – | Abhängigkeit von einem Fremddienst im Menü |
| iCal-Abonnement eigener Termine | – | Vormerken | 1.1 | W | kostenlos, ohne Fremddienst |
| Kolonnen, Mitarbeiter, Fahrzeuge, Pumpen, Maschinen | ✅ | Beibehalten | 1.0 | W D | |
| Fahrzeugdokumente | ✅ | Beibehalten | 1.0 | W | |
| Urlaub & Krankmeldungen | ✅ | Beibehalten | 1.0 | W D | Datenschutz: Grund nur für berechtigte Rollen sichtbar |

## 3. Büro und Kontakte

| Funktion | Status heute | Entscheidung | Version | Plattform |
|---|---|---|---|---|
| Kunden, Ansprechpartner (Projektbeteiligte) | ✅ | Verbessern | 1.0 | W D M (Auswahl) |
| Anrufe und Anfragen, Umwandlung in Bauakte | ✅ | Beibehalten | 1.0 | **W D** |
| Dublettenprüfung Kunden (E-Mail/Telefon/Name) | ✅ | Verbessern | 1.0 | W D M |
| Kundenportal | ⚠️ abgeschaltet | Vormerken | 2.0 | W |
| Subunternehmerportal | ❌ | Vormerken | 2.0 | – |

## 4. Integrationen

| Funktion | Status heute | Entscheidung | Version | Begründung |
|---|---|---|---|---|
| Lexware Office: Kunden-/Projekt-Sync, Angebote verknüpfen, Auto-Sync | ✅ | **Entfernen aus dem Kern**, Vormerken als optionales Modul | 1.2 | Angebote/Rechnungen sind ausgeschlossen. Eine direkte Lexware-Abhängigkeit ist untersagt. Vorgesehen ist ein optionaler **Kundenimport** über eine allgemeine Import-Schnittstelle (CSV + optionaler Lexware-Adapter). Bestehende Verknüpfungen (`lexware_contact_id`) werden als „externe Referenz“ migriert und bleiben erhalten. |
| Wetter am Standort | ✅ | Beibehalten, optional | 1.0 | Anbieter konfigurierbar, ohne Konto (z. B. Open-Meteo) oder abschaltbar |
| Karte in der Bauakte | ✅ | Verbessern | 1.0 | Link in die Karten-App des Systems statt eingebetteter Fremdkarte; optional OSM-Kacheln |
| KI-Jobs, Export-Jobs, Webhooks, API-Tokens | ❌ | Entfernen | – | Platzhalter (Regel 17/18) |

## 5. Benutzer, Sicherheit, Betrieb

| Funktion | Status heute | Entscheidung | Version | Plattform |
|---|---|---|---|---|
| Lokale Anmeldung | ✅ | **Neu entwickeln** (Argon2id, Rehash von bcrypt beim Login) | 1.0 | alle |
| Sperre nach Fehlversuchen | ✅ | Beibehalten (Konzept) | 1.0 | – |
| Passwortänderung erzwingen | ✅ | Beibehalten | 1.0 | alle |
| Passwort-Zurücksetzen per E-Mail | – | Neu entwickeln | 1.0 | W (nur mit SMTP) |
| Administrator-Reset mit Einmalpasswort | ✅ | Beibehalten | 1.0 | W D |
| PIN-Schnellanmeldung | ✅ | **Neu entwickeln** als Geräte-PIN für Poolgeräte | 1.0 | M |
| 2FA (TOTP) | ❌ | Neu entwickeln | 1.1 | W D (für Administratoren empfohlen) |
| Microsoft-Entra-SSO | ⚠️ unsicher | **Neu entwickeln** | 1.0 optional | alle |
| Rollen & Rechte | ✅ mit festen Ausnahmen | **Neu entwickeln** (Rechtekatalog + Objektregeln) | 1.0 | W D |
| Audit-Log | ❌ | Neu entwickeln | 1.0 | W |
| Login-Protokoll | ✅ | Zusammenführen in Sicherheitsereignisse | 1.0 | W |
| Gerätesitzungen | ❌ | Neu entwickeln → IT Center | 1.0 | W D |
| Security Center (statisch) | ❌ | Entfernen | – | – |
| Offline-Center | ❌ | Neu entwickeln als „Synchronisation“ | 1.0 | M (W: nur Status) |
| Firmendaten & Optionen, SMTP | ✅ | Beibehalten | 1.0 | W |
| Erinnerungen fällige Aufgaben (HTTP-Cron) | ✅ | Verbessern (interner Hintergrunddienst) | 1.0 | – |
| „Was ist neu“ | ✅ | Beibehalten | 1.0 | alle |
| Benachrichtigungen (Tabelle) | ❌ | Neu entwickeln (In-App, optional E-Mail/Push) | 1.0 | alle |
| Nachrichten-Vorlagen | ❌ | Entfernen | – | – |
| Browser-Installer `/install` | ✅ | Ersetzen durch Ersteinrichtung mit Einmal-Code | 1.0 | – |

## 6. Neue Module

| Modul | Version | Plattform | Dokument |
|---|---|---|---|
| Mobile Schnellanlage | 1.0 | M | 09 |
| Server-Onboarding (Einrichtungsassistent) | 1.0 | M D | 10 |
| Persönliche Geräte / Poolgeräte | 1.0 | M (D: persönliches Gerät) | 11 |
| Microsoft Entra SSO (optional) | 1.0 | alle | 12 |
| IT Center – Geräteverwaltung | 1.0 Basis, 1.1 Richtlinien vollständig | W D | 13 |
| Helpdesk | 1.1 (Ticketanlage mobil bereits 1.0 möglich) | alle | 14 |
| Backup Center | **1.0 (vor Inbetriebnahme Pflicht)** | W + CLI | 16 |
| Synchronisation | 1.0 | M (D optional) | 15 |

## 7. Plattformverteilung (gemeinsam vs. plattformabhängig)

| Gemeinsam (Backend, gilt überall) | Plattformabhängig |
|---|---|
| Datenmodell, Rechteprüfung, Bauaktennummern, Hinweisberechnung, Duplikatprüfung, Dateiverarbeitung (Varianten, EXIF), PDF-Erzeugung, Audit, Benachrichtigungen, Sync-Regeln | Kamera und Scanner (M), Biometrie (M, D: Windows Hello), Ribbon/Mehrfenster/Drucken (D), Tabellen mit hoher Dichte (D, W), Offline-Datenbank (M), Push (M), Administration (W; D teilweise) |

**Grundsatz:** Geschäftslogik gehört auf den Server. Clients enthalten nur das, was offline nötig ist:
Validierung von Pflichtfeldern, lokale Suche und die Sync-Warteschlange.
