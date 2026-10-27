/* EstrichONE Störungs- und Wartungsseite (Caddy bzw. Service Worker liefern sie, wenn der Server nicht antwortet).
 * Prüft alle 30 Sekunden /api/v1/server-info und kehrt zur ursprünglichen Seite zurück, sobald der Server wieder da ist.
 * Bei Wartung (503, code MAINTENANCE) zeigt sie Titel, Text und geplantes Ende aus /api/v1/system/notices. */
(function () {
  'use strict';
  var INTERVAL = 30;
  var params = new URLSearchParams(location.search);
  var back = params.get('zurueck') || (location.pathname === '/stoerung.html' ? '/' : location.pathname + location.search);
  var started = new Date();
  var left = INTERVAL;
  var $ = function (id) { return document.getElementById(id); };
  var time = function (d) { return d.toLocaleString('de-DE', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }); };
  $('since').textContent = 'Seit ' + time(started) + ' Uhr';

  function maintenance(m) {
    document.title = 'EstrichONE – Wartungsarbeiten';
    $('title').textContent = 'Wartungsarbeiten';
    $('text').textContent = (m && m.message) || 'EstrichONE wird gerade gewartet. Ihre Eingaben in der App bleiben auf dem Gerät gespeichert.';
    $('hint').textContent = m && m.endsAt ? 'Voraussichtlich bis ' + time(new Date(m.endsAt)) + ' Uhr.' : 'Danach geht es automatisch weiter.';
    $('state').className = 'state maintenance';
  }

  function check() {
    $('status').textContent = 'Verbindung wird geprüft …';
    fetch('/api/v1/server-info', { cache: 'no-store', credentials: 'same-origin' }).then(function (r) {
      if (r.ok) { location.replace(back); return; }
      return fetch('/api/v1/system/notices', { cache: 'no-store' }).then(function (n) { return n.ok ? n.json() : null; }).then(function (n) {
        if (n && n.maintenance && n.maintenance.active) maintenance(n.maintenance);
        if (n && !(n.maintenance && n.maintenance.active)) { location.replace(back); }
      });
    }).catch(function () { /* weiterhin nicht erreichbar */ }).then(function () { left = INTERVAL; tick(); });
  }

  var timer;
  function tick() {
    clearTimeout(timer);
    $('status').textContent = navigator.onLine === false ? 'Keine Internetverbindung – Neuversuch in ' + left + ' Sekunden …'
      : 'Automatischer Neuversuch in ' + left + ' Sekunden …';
    if (left-- <= 0) { check(); return; }
    timer = setTimeout(tick, 1000);
  }

  $('retry').addEventListener('click', check);
  window.addEventListener('online', check);
  if (params.get('grund') === 'wartung') maintenance(null);
  tick();
})();
