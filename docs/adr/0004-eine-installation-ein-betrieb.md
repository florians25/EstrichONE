# 0004 – Eine Installation = ein Betrieb

- **Status:** angenommen · **Datum:** 2026-10-19

## Kontext
Estrich Web führte `tenant_id` in jeder Tabelle. EstrichONE wird von jedem Betrieb selbst gehostet.

## Entscheidung
Keine Mandantenspalte. Wer mehrere Firmen betreibt, betreibt mehrere Installationen.

## Folgen
Weniger Fehlerquellen (keine vergessenen Mandantenfilter), einfacheres Backup und Restore.
Ein gemeinsamer Hosting-Dienst für viele Betriebe ist ohne Umbau nicht möglich und nicht vorgesehen.
