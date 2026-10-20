# 0012 – iOS: Datentresor je Benutzer und handgeschriebener API-Client

- **Status:** angenommen · **Datum:** 2026-10-20

## Kontext
11, Abschnitt 4.3 verlangt je Benutzer einen verschlüsselten Container, dessen Datenschlüssel doppelt umhüllt
ist: mit einem aus der PIN abgeleiteten Schlüssel (dort: Argon2id) und mit einem nicht exportierbaren
Geräteschlüssel. 07 sieht einen mit swift-openapi-generator erzeugten API-Client vor.

## Entscheidung
**Datentresor**
- Datenschlüssel: 256 Bit zufällig. Innere Hülle: AES-GCM mit einem Schlüssel aus der PIN über
  **PBKDF2-HMAC-SHA256** (210.000 Durchläufe, 16 Byte Salt). Äußere Hülle: ECIES mit einem P-256-Schlüssel der
  **Secure Enclave** (Schlüsselvereinbarung → HKDF-SHA256 → AES-GCM).
- Persönliche Geräte haben keine PIN: Dort gibt es nur die äußere Hülle; die App-Sperre übernimmt
  Face ID/Touch ID bzw. der Gerätecode.
- Argon2id gibt es in Apples Frameworks nicht. Eine C-Bibliothek dafür einzubinden, lohnt sich nicht: Die
  Schutzwirkung kommt aus der Secure-Enclave-Hülle. Ohne genau dieses Gerät lässt sich die PIN nicht
  durchprobieren, auf dem Gerät begrenzt die App auf 5 Versuche. PBKDF2 ist die zweite Verteidigungslinie.
- Im Simulator gibt es keine Secure Enclave. Dort verwendet die App Software-Schlüssel im Schlüsselbund. Auf
  echten Geräten (alle iPhones mit iOS 17) ist die Secure Enclave immer vorhanden.

**API-Client**
- Der Client in `EstrichCore` ist handgeschrieben: wenige, schlanke `Codable`-Typen statt eines Generators
  mit Build-Plugin. Abweichungen vom Vertrag fängt ein Test ab, der jeden Swift-Typ gegen
  `contracts/openapi/v1.json` prüft (Feldnamen, Pflichtfelder).
- Zusätzlich läuft in der CI ein Integrationstest des Swift-Kerns gegen einen echten Server
  (Anmeldung, Geräteregistrierung, Signaturen, Einrichtungscodes).

## Folgen
Keine zusätzliche Abhängigkeit für Kryptografie (CryptoKit; unter Linux das API-gleiche swift-crypto für
Tests). Der Vertragstest macht Abweichungen sichtbar, bevor die App sie bemerkt.
