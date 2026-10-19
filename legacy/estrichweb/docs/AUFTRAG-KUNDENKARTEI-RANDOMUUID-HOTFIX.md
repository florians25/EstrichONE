# Auftragserfassung Kundenkartei + HTTP Offline-ID Hotfix

## Auftrag anlegen

Die Auftragserfassung erzeugt Kunden nicht mehr automatisch neu. Im ersten Schritt muss jetzt ein bestehender Kunde aus der Kundenkartei gewählt werden. Der Workflow speichert danach Auftrag, Baustelle, Räume, Termin, Plantafel-Eintrag und Startaufgabe mit `customer_id` des bestehenden Kunden.

## Backend

`POST /api/workflow/customer-project-site` akzeptiert nur noch einen bestehenden Kunden über `customer.id` oder `customer.customer_id`. Der Kunde wird mandantenbezogen geprüft. Wird kein bestehender Kunde gefunden, antwortet die API mit `422` oder `404`.

## HTTP-Umgebung / Offline Queue

`crypto.randomUUID()` ist in HTTP-Umgebungen beziehungsweise älteren Browsern nicht zuverlässig verfügbar. Die Offline-Queue nutzt jetzt eine eigene UUID-Fallback-Erzeugung mit `crypto.getRandomValues()` oder, falls nicht vorhanden, einem Math.random-Fallback. Dadurch bricht die App bei fehlgeschlagenen API-Aufrufen nicht mehr mit `crypto.randomUUID is not a function` ab.
