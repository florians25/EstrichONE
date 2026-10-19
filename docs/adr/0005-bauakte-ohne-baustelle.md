# 0005 – Bauakte und Baustelle sind eine Entität

- **Status:** angenommen · **Datum:** 2026-10-19

## Kontext
Estrich Web führte `projects` und `construction_sites` mit fast identischen Feldern. Beim Hochladen legte die
API stillschweigend Baustellen als Kopie der Bauakte an.

## Entscheidung
Eine Bauakte trägt Bezeichnung und Baustellenadresse. Bauabschnitte (Haus A/B, Geschosse) werden als
`bauakte_bereiche` abgebildet.

## Folgen
Einfacheres Modell, einfachere Schnellanlage. Mehrere Adressen je Bauakte sind nicht vorgesehen.
In diesem Fall werden mehrere Bauakten angelegt.
