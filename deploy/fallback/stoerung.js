/* EstrichONE Störungs- und Wartungsseite (Caddy bzw. Service Worker liefern sie, wenn der Server nicht antwortet).
 * Prüft alle 30 Sekunden /api/v1/server-info und kehrt zur ursprünglichen Seite zurück, sobald der Server wieder da ist.
 * Bei Wartung (503, code MAINTENANCE) zeigt sie Titel, Text und geplantes Ende aus /api/v1/system/notices.
 * Update-Ablauf (deploy/README.md): /update-status.json liefert Caddy auch ohne API – dann Phase, Zielversion und Countdown,
 * Prüfung alle 5 Sekunden; die Weboberfläche übernimmt, sobald die API wieder antwortet. */
(function () {
  'use strict';
  var INTERVAL = 30;
  var params = new URLSearchParams(location.search);
  var back = params.get('zurueck') || (location.pathname === '/stoerung.html' ? '/' : location.pathname + location.search);
  var started = new Date();
  var left = INTERVAL;
  var update = null;
  var $ = function (id) { return document.getElementById(id); };
  var time = function (d) { return d.toLocaleString('de-DE', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }); };
  var clock = function (s) { s = Math.max(0, Math.ceil(s)); return Math.floor(s / 60) + ':' + ('0' + (s % 60)).slice(-2); };
  $('since').textContent = 'Seit ' + time(started) + ' Uhr';

  function maintenance(m) {
    document.title = 'EstrichONE – Wartungsarbeiten';
    $('title').textContent = 'Wartungsarbeiten';
    $('text').textContent = (m && m.message) || 'EstrichONE wird gerade gewartet. Ihre Eingaben in der App bleiben auf dem Gerät gespeichert.';
    $('hint').textContent = m && m.endsAt ? 'Voraussichtlich bis ' + time(new Date(m.endsAt)) + ' Uhr.' : 'Danach geht es automatisch weiter.';
    $('state').className = 'state maintenance';
  }

  var TITLES = { announce: 'Update angekündigt', update: 'Update wird eingespielt', finish: 'Update wird abgeschlossen',
    rollback: 'Update wird zurückgenommen', failed: 'Update fehlgeschlagen' };

  function showUpdate(u) {
    update = u;
    var version = u.targetVersion ? ' auf Version ' + u.targetVersion : '';
    document.title = 'EstrichONE – ' + (TITLES[u.phase] || 'Update');
    $('title').textContent = TITLES[u.phase] || 'Update';
    $('text').textContent = u.phase === 'failed'
      ? (u.message || 'Das Update' + version + ' konnte nicht eingespielt werden.') + ' Der Administrator ist informiert.'
      : 'EstrichONE wird gerade' + version + ' aktualisiert. Ihre Daten sind sicher; Eingaben in der App bleiben auf dem Gerät gespeichert.';
    $('hint').textContent = 'Nach dem Update bitte erneut anmelden. Die Seite lädt automatisch neu.';
    $('state').className = 'state maintenance';
    $('update').hidden = false;
    var order = ['announce', 'update', 'finish'];
    var index = u.phase === 'rollback' || u.phase === 'failed' ? 2 : order.indexOf(u.phase);
    ['s1', 's2', 's3'].forEach(function (id, i) { $(id).className = i < index ? 'done' : i === index ? 'now' : ''; });
    $('s3').textContent = index === 2 && u.phase !== 'finish' ? (u.phase === 'failed' ? 'Fehlgeschlagen' : 'Rücknahme') : 'Abschluss';
    renderCountdown();
  }

  function renderCountdown() {
    if (!update) return;
    var now = Date.now();
    var end = update.phaseEndsAt ? Date.parse(update.phaseEndsAt) : NaN;
    var start = update.phaseStartedAt ? Date.parse(update.phaseStartedAt) : NaN;
    var rest = isNaN(end) ? 0 : (end - now) / 1000;
    var label = update.phase === 'announce' ? 'Beginnt in ' : update.phase === 'update' ? 'Läuft · mindestens noch ' : 'Freigabe in ';
    $('count').textContent = rest > 0 ? label + clock(rest) : update.phase === 'failed' ? 'Gesperrt' : 'Gleich geschafft …';
    var total = !isNaN(end) && !isNaN(start) && end > start ? (end - start) : 0;
    $('bar').style.width = total ? Math.min(100, Math.max(0, (now - start) / total * 100)) + '%' : '100%';
  }

  function readUpdate() {
    return fetch('/update-status.json', { cache: 'no-store' }).then(function (r) { return r.ok ? r.json() : null; })
      .catch(function () { return null; });
  }

  function check() {
    $('status').textContent = 'Verbindung wird geprüft …';
    readUpdate().then(function (u) {
      if (u && u.phase) showUpdate(u); else if (update) { location.replace(back); return; }
      return fetch('/api/v1/server-info', { cache: 'no-store', credentials: 'same-origin' }).then(function (r) {
        if (r.ok) { location.replace(back); return; }
        return fetch('/api/v1/system/notices', { cache: 'no-store' }).then(function (n) { return n.ok ? n.json() : null; }).then(function (n) {
          if (n && n.update) { showUpdate(n.update); return; }
          if (n && n.maintenance && n.maintenance.active) maintenance(n.maintenance);
          if (n && !(n.maintenance && n.maintenance.active)) { location.replace(back); }
        });
      });
    }).catch(function () { /* weiterhin nicht erreichbar */ }).then(function () { left = update ? 5 : INTERVAL; tick(); });
  }

  var timer;
  function tick() {
    clearTimeout(timer);
    renderCountdown();
    $('status').textContent = navigator.onLine === false ? 'Keine Internetverbindung – Neuversuch in ' + left + ' Sekunden …'
      : update ? 'Stand wird alle 5 Sekunden geprüft …' : 'Automatischer Neuversuch in ' + left + ' Sekunden …';
    if (left-- <= 0) { check(); return; }
    timer = setTimeout(tick, 1000);
  }

  $('retry').addEventListener('click', check);
  window.addEventListener('online', check);
  if (params.get('grund') === 'wartung') maintenance(null);
  readUpdate().then(function (u) { if (u && u.phase) { showUpdate(u); left = 5; } tick(); });
})();
