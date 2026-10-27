/* EstrichONE Service Worker: Pushnachrichten (Inhalt Ende-zu-Ende verschlüsselt, RFC 8291) und Offline-Seite.
 * Kein Offline-Cache der App: nur die Störungsseite (deploy/fallback) wird vorgehalten und bei Seitenaufrufen ohne
 * Verbindung gezeigt; sie kehrt von selbst zurück, sobald der Server wieder antwortet (docs/backup.md). */
const OFFLINE_CACHE = 'estrichone-offline-v1';
const OFFLINE_FILES = ['/stoerung.html', '/stoerung.js', '/logo-mark.svg', '/favicon.svg'];
self.addEventListener('install', (event) => event.waitUntil((async () => {
  try {
    const cache = await caches.open(OFFLINE_CACHE);
    await cache.addAll(OFFLINE_FILES);
  } catch {
    // Ohne Störungsseite (z. B. Entwicklungsserver) weiter wie bisher.
  }
  await self.skipWaiting();
})()));
self.addEventListener('activate', (event) => event.waitUntil((async () => {
  for (const key of await caches.keys()) if (key.startsWith('estrichone-offline-') && key !== OFFLINE_CACHE) await caches.delete(key);
  await self.clients.claim();
})()));

self.addEventListener('fetch', (event) => {
  const request = event.request;
  if (request.mode !== 'navigate') {
    if (OFFLINE_FILES.includes(new URL(request.url).pathname)) {
      event.respondWith(fetch(request).catch(async () => (await caches.match(request)) || Response.error()));
    }
    return;
  }
  event.respondWith(fetch(request).catch(async () => (await caches.match('/stoerung.html')) || Response.error()));
});

self.addEventListener('push', (event) => {
  let data = {};
  try {
    data = event.data ? event.data.json() : {};
  } catch {
    data = { title: 'EstrichONE', body: event.data ? event.data.text() : '' };
  }
  const title = data.title || 'EstrichONE';
  event.waitUntil((async () => {
    await self.registration.showNotification(title, {
      body: data.body || '',
      icon: '/icon-192.png',
      badge: '/icon-192.png',
      tag: data.id || undefined,
      lang: 'de',
      requireInteraction: data.urgency === 'high',
      data: { id: data.id, link: data.link || '/start' },
    });
    if (typeof data.badge === 'number' && self.navigator.setAppBadge) {
      await self.navigator.setAppBadge(data.badge).catch(() => undefined);
    }
    // Offene Fenster aktualisieren Glocke und Postfach sofort.
    for (const client of await self.clients.matchAll({ type: 'window' })) client.postMessage({ type: 'estrichone-notification', id: data.id });
  })());
});

self.addEventListener('notificationclick', (event) => {
  event.notification.close();
  const { id, link } = event.notification.data || {};
  const target = new URL(link || '/start', self.location.origin);
  if (id) target.searchParams.set('nid', id);
  event.waitUntil((async () => {
    const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    const open = windows.find((w) => new URL(w.url).origin === self.location.origin);
    if (open) {
      await open.focus();
      open.postMessage({ type: 'estrichone-open', url: target.pathname + target.search });
      return;
    }
    await self.clients.openWindow(target.href);
  })());
});
