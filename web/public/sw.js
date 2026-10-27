/* EstrichONE Service Worker: nur Pushnachrichten (kein Offline-Cache). Inhalt ist Ende-zu-Ende verschlüsselt (RFC 8291). */
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()));

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
