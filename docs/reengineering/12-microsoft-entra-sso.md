# 12 – Optionale Anmeldung mit Microsoft 365 / Microsoft Entra ID

**[Verbindlich]** Optional, vollständig abschaltbar, lokale Konten funktionieren immer. Standardverfahren
(OIDC, OAuth 2.0, Authorization Code Flow mit PKCE), offizielle Microsoft-Bibliotheken, keine Speicherung
von Microsoft-Passwörtern.

## 1. Bestand und Bewertung

Estrich Web enthält eine Entra-Anmeldung (`/auth/microsoft/*`, `index.php:1562–1613`). Im Login wurde der
Button entfernt, die Endpunkte sind aber weiterhin erreichbar. **Sie wird nicht übernommen.**

| Mangel | Folge |
|---|---|
| ID-Token wird **ohne Signaturprüfung** decodiert | Integrität beruht allein auf der TLS-Verbindung zum Token-Endpunkt. Es gibt keine Prüfung von Aussteller, Zielgruppe oder `nonce`. |
| Zuordnung per `preferred_username`/`email` | Diese Werte sind veränderlich und nicht als Identität gedacht. Mit Mandant `common` kann eine Person aus **einem fremden Microsoft-Tenant** mit gleichlautendem Benutzernamen in das EstrichONE-Konto gelangen. |
| Standard-Tenant `common` | jeder Microsoft-Mandant zugelassen |
| Client-Secret mit JWT-Secret verschlüsselt | Schlüsselwiederverwendung; ein Rotieren des JWT-Secrets macht das gespeicherte SSO-Secret unlesbar |

## 2. Zielbild

```
              Web (Browser)                      Apps (iOS, Android, Windows)
                   │                                      │
   1. GET /auth/entra/web/start                 1. MSAL interaktiv (System-Browser / Broker / WAM)
   2. Redirect zu Entra (Code + PKCE + nonce)   2. erhält ID-Token (aud = EstrichONE-App-ID)
   3. Callback → Server tauscht Code             3. POST /auth/entra/exchange { id_token, device_proof }
      (vertraulicher Client, Secret/Zertifikat)        │
                   └──────────────┬───────────────────┘
                                  ▼
          Server prüft Token (Microsoft.Identity.Web / JwtBearer, OIDC-Metadaten + JWKS):
            Signatur · iss = https://login.microsoftonline.com/{tid}/v2.0 · tid ∈ erlaubte Mandanten
            aud = Client-ID · exp/nbf · nonce (Web)
                                  ▼
          Zuordnung (tid, oid) → EstrichONE-Benutzer  →  EstrichONE-Sitzung (eigene Tokens)
```

**Wichtig:** Die Clients verwenden **keine Microsoft-Tokens** für den Zugriff auf die EstrichONE-API.
Das Microsoft-Token dient nur einmal als Identitätsnachweis. Danach gelten die eigenen Sitzungstokens
mit Gerätebindung, Widerruf und Richtlinien (11). Sperren, Gerätewiderruf und Rechte bleiben damit vollständig
in EstrichONE.

## 3. Konfiguration (Web › Administration › Anmeldung)

| Einstellung | Bemerkung |
|---|---|
| Microsoft-Anmeldung aktiv | Schalter, Standard aus |
| Verzeichnis-ID (Tenant ID) | **eine konkrete GUID**, nicht `common`/`organizations`. Weitere Mandanten nur über die Liste „Erlaubte Mandanten“ |
| Anwendungs-ID (Client ID) | aus der App-Registrierung |
| Client-Geheimnis **oder** Zertifikat | nur für den Web-Flow; gespeichert über ASP.NET Core Data Protection (eigener Schlüsselring, im Backup enthalten) und nie wieder angezeigt |
| Redirect-URIs | werden **angezeigt** (zum Kopieren in das Entra-Portal): Web `https://<server>/api/v1/auth/entra/web/callback`; iOS `msauth.<bundle-id>://auth`; Android `msauth://<package>/<signatur-hash>`; Windows Broker-URI |
| Zuordnung | „Nur bestehende verknüpfte Benutzer“ (Standard) · „Per E-Mail einmalig verknüpfen“ · „Benutzer automatisch anlegen“ |
| Standardrolle bei automatischer Anlage | Pflichtauswahl, darf **keine** Administratorrechte enthalten (Server erzwingt das) |
| Gruppen → Rollen | optional, nur explizit konfigurierte Gruppen-IDs. Administratorrollen sind **nicht** über Gruppen vergebbar (Schutz gegen Fehlkonfiguration im Microsoft-Tenant). |
| Lokale Anmeldung für SSO-Benutzer | erlauben/verbieten (Standard: verbieten, wenn verknüpft; Ausnahme: Notfall-Administrator) |
| Verbindung testen | führt einen Login im Admin-Browser durch und zeigt die empfangenen, geprüften Claims an |

Eine **Schritt-für-Schritt-Anleitung** für die App-Registrierung im Entra-Portal wird mitgeliefert (mit
Screenshots). Erforderliche Berechtigungen: `openid`, `profile`, `email`, keine Graph-Berechtigungen. Für
Gruppen genügt der `groups`-Claim im Token.

## 4. Benutzerzuordnung

- Tabelle `external_identities (provider='entra', tenant_id, object_id, user_id, linked_at, last_login_at)`,
  eindeutig auf `(provider, tenant_id, object_id)`.
- **Verknüpfen:**
  - (a) Der Administrator verknüpft im Benutzerprofil („Microsoft-Konto zuordnen“ mit einer Suche, die nur
    nach dem Test-Login die `oid` kennt) **oder**
  - (b) der Benutzer meldet sich einmal lokal an und verknüpft unter *Profil › Microsoft-Konto verbinden*
    **oder**
  - (c) bei aktivierter Option „Per E-Mail einmalig verknüpfen“: Übereinstimmung der **verifizierten** E-Mail
    (Claim `email` aus dem eigenen Tenant). Danach gilt ausschließlich `tid + oid`.
- Spätere Änderungen von Name oder E-Mail im Microsoft-Konto aktualisieren nur Anzeigefelder.

## 5. Rollen

Microsoft-Anmeldung bedeutet **keine** Rechte. Rechte kommen aus EstrichONE-Rollen. Optional synchronisierte
Gruppenrollen werden bei jeder Anmeldung neu berechnet und im Audit-Log protokolliert. Manuell vergebene Rollen
bleiben davon unberührt.

## 6. Plattformen

| Plattform | Bibliothek | Besonderheiten |
|---|---|---|
| Web | ASP.NET Core `AddOpenIdConnect` / Microsoft.Identity.Web (Server-seitiger Code-Flow) | Browser erhält danach die EstrichONE-Sitzung als HttpOnly-Cookie (SameSite=Strict) |
| Windows | MSAL.NET mit **WAM**-Broker | nutzt das angemeldete Windows-/Entra-Konto (Single Sign-On) |
| iOS | MSAL für iOS/macOS | Systembrowser-Sitzung (`ASWebAuthenticationSession`) bzw. Microsoft Authenticator als Broker |
| Android | MSAL für Android | Broker (Authenticator/Unternehmensportal) falls vorhanden, sonst Custom Tab |

### Poolgeräte

| Risiko | Gegenmaßnahme |
|---|---|
| Vorheriger Benutzer wird still wieder angemeldet (Browser-Cookie bei Microsoft) | Anfrage mit `prompt=select_account` (Standard) bzw. `prompt=login` (Richtlinie „Microsoft-Kennwort auf Poolgeräten immer abfragen“); iOS: ephemere Browsersitzung (`prefersEphemeralWebBrowserSession`), damit keine Microsoft-Cookies bleiben |
| MSAL-Token-Cache enthält Konten früherer Benutzer | auf Poolgeräten **kein persistenter MSAL-Cache**; nach dem Austausch gegen die EstrichONE-Sitzung werden Konten aus MSAL entfernt (`removeAccount`) |
| Broker (Authenticator) hält Konten geräteweit | **Shared Device Mode** von Microsoft ist der offizielle Weg für gemeinsame Geräte (globale Abmeldung). Er erfordert die Einrichtung über Authenticator bzw. ein MDM und ist daher **optional dokumentiert, nicht vorausgesetzt** |
| Abmelden aus EstrichONE beendet nicht die Microsoft-Sitzung im System | **[Verbindlich] transparent anzeigen:** „Sie wurden von EstrichONE abgemeldet. Ihre Microsoft-Anmeldung auf diesem Gerät kann weiterhin bestehen.“ – mit Option „Auch bei Microsoft abmelden“ (Front-Channel-Logout im Systembrowser) |

## 7. Abschaltbarkeit

- Ohne Konfiguration: `server-info` meldet `auth: ["local"]`. Die Apps zeigen keine Microsoft-Schaltfläche, und
  die Entra-Endpunkte antworten mit `404`. Microsoft-Bibliotheken werden in den Apps erst bei Bedarf
  initialisiert, ohne Netzwerkzugriff beim Start.
- Fällt Microsoft aus oder wird die Integration abgeschaltet: Ein **Notfall-Administrator** mit lokalem
  Passwort bleibt immer bestehen (die Ersteinrichtung erzwingt ihn).

## 8. Akzeptanzkriterien

- **AK-1:** Ein Konto aus einem nicht erlaubten Microsoft-Tenant wird abgewiesen, auch bei identischer E-Mail.
- **AK-2:** Ein manipuliertes oder abgelaufenes ID-Token wird an `/auth/entra/exchange` abgewiesen.
- **AK-3:** Auf einem Poolgerät erscheint nach Abmeldung von Benutzer A bei der Microsoft-Anmeldung von B die
  Kontoauswahl bzw. Kennwortabfrage, nie direkt As Konto.
- **AK-4:** Bei deaktivierter Integration funktionieren alle Funktionen mit lokalen Konten unverändert
  (Testlauf der gesamten Suite ohne Entra-Konfiguration).
