# 0006 – Client-generierte UUIDv7 als Primärschlüssel

- **Status:** angenommen · **Datum:** 2026-10-19

## Kontext
Bauakten, Fotos und Notizen müssen offline angelegt und bei Verbindungsabbrüchen gefahrlos erneut gesendet
werden können.

## Entscheidung
Primärschlüssel sind `uuid`. Clients erzeugen sie als UUIDv7 (zeitlich sortierbar, gut für B-Baum-Indizes).
Der Server übernimmt die ID und lehnt eine Anlage mit bereits vergebener ID **anderen Inhalts** ab.
Serverseitig erzeugte Datensätze verwenden `Guid.CreateVersion7()`.

## Folgen
Idempotente Anlage ohne ID-Zuordnungstabellen. Fachliche Nummern (Bauaktennummer `JJJJ-NNN`) vergibt
weiterhin ausschließlich der Server.
