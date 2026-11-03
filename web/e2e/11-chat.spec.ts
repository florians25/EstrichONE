import { expect, test, type Page } from '@playwright/test';
import { adminPassword, apiCall, nav, signIn } from './helpers';

const OFFICE_ROLE = '01a00000-0000-7000-8000-000000000002';

/** Foto für den Test (im Browser gezeichnet, damit es keine Binärdatei im Repo braucht). */
async function photo(page: Page, label: string): Promise<Buffer> {
  const base64 = await page.evaluate(async (text) => {
    const canvas = new OffscreenCanvas(1600, 1200);
    const g = canvas.getContext('2d')!;
    const sky = g.createLinearGradient(0, 0, 0, 1200);
    sky.addColorStop(0, '#9fb8cf');
    sky.addColorStop(0.55, '#d9d4c7');
    sky.addColorStop(1, '#8c8577');
    g.fillStyle = sky;
    g.fillRect(0, 0, 1600, 1200);
    g.fillStyle = '#b9b2a3';
    g.beginPath();
    g.moveTo(0, 1200); g.lineTo(520, 640); g.lineTo(1080, 640); g.lineTo(1600, 1200); g.closePath();
    g.fill();
    g.strokeStyle = 'rgba(80,72,60,0.35)';
    g.lineWidth = 6;
    for (let i = 0; i < 9; i++) { g.beginPath(); g.moveTo(800 + (i - 4) * 70, 640); g.lineTo(800 + (i - 4) * 260, 1200); g.stroke(); }
    g.fillStyle = 'rgba(255,255,255,0.85)';
    g.font = '600 64px sans-serif';
    g.fillText(text, 60, 110);
    const blob = await canvas.convertToBlob({ type: 'image/jpeg', quality: 0.9 });
    const bytes = new Uint8Array(await blob.arrayBuffer());
    let binary = '';
    for (const b of bytes) binary += String.fromCharCode(b);
    return btoa(binary);
  }, label);
  return Buffer.from(base64, 'base64');
}

const shot = (page: Page, name: string) => process.env['SCREENSHOTS']
  ? page.waitForTimeout(700).then(() => page.screenshot({ path: `test-results/screenshots/${name}.png` }))
  : Promise.resolve();

test('Chat: Modul einschalten, Datenschutzhinweis, Einzelchat, Nachricht (Enter), Häkchen, Echtzeit, Foto, Antwort, Reaktion, Suche, Gruppe', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  const status = await apiCall<{ enabled: boolean; canAdmin: boolean; privacyAccepted: boolean }>(page, 'GET', '/api/v1/chat/status');
  expect(status.json.canAdmin).toBe(true);
  // Standard aus: kein Eintrag in der Navigation.
  await expect(nav(page, 'Chat')).toHaveCount(0);

  const settings = await apiCall(page, 'PUT', '/api/v1/chat/admin/settings', { enabled: true, retentionDays: 90, showPreview: false, workersCanChatEachOther: true });
  expect(settings.status).toBe(200);
  const name = `Clara Chat ${Math.floor(Math.random() * 9000) + 1000}`;
  const user = await apiCall<{ user: { id: string } }>(page, 'POST', '/api/v1/users', {
    displayName: name, userName: `chat${Date.now()}`, email: null, phone: null, roleIds: [OFFICE_ROLE],
  });
  expect(user.status).toBeLessThan(300);
  const claraId = user.json.user.id;

  await page.reload();
  await nav(page, 'Chat').click();
  await expect(page).toHaveURL(/\/chat$/);
  // Datenschutzhinweis beim ersten Öffnen.
  const privacy = page.getByRole('dialog', { name: 'Datenschutz im Chat' });
  if (!status.json.privacyAccepted) {
    await expect(privacy).toContainText('verschlüsselt');
    await privacy.getByRole('button', { name: 'Verstanden' }).click();
  }
  await expect(privacy).toHaveCount(0);
  await expect.poll(async () => (await apiCall<{ privacyAccepted: boolean }>(page, 'GET', '/api/v1/chat/status')).json.privacyAccepted).toBe(true);
  // Echtzeit (Server-Sent Events) steht – hinter Caddy und Nginx (CI). Der Entwicklungsserver (ui5 serve) puffert den Strom.
  if (!/localhost:(4200|4291)/.test(page.url())) await expect(page.locator('.eoChatLive')).toBeVisible({ timeout: 15_000 });

  await page.getByRole('button', { name: 'Neuer Einzelchat' }).first().click();
  const picker = page.getByRole('dialog', { name: 'Neuer Einzelchat' });
  await picker.getByPlaceholder('Name suchen').fill(name);
  await picker.getByText(name, { exact: true }).click();
  await expect(page).toHaveURL(/\/chat\/[0-9a-f-]{36}$/);
  await expect(page.locator('.eoChatHead')).toContainText(name);
  const conversationId = page.url().split('/').pop()!;

  const input = page.getByPlaceholder('Nachricht schreiben …');
  await input.fill('Bitte den Lieferschein von heute ins Büro legen. Plan: https://example.org/plan');
  await input.press('Enter');
  const bubble = page.locator('.eoChatMine .eoChatBubble').filter({ hasText: 'Bitte den Lieferschein von heute ins Büro legen.' });
  await expect(bubble).toBeVisible();
  await expect(bubble.locator('.eoChatTick[title="Gesendet"], .eoChatTick[title^="Zugestellt"]')).toBeVisible();
  await expect(bubble.locator('a.eoChatLink')).toHaveAttribute('href', 'https://example.org/plan');
  await expect(input).toHaveValue('');
  await expect(page.locator('.eoChatConv').filter({ hasText: name })).toContainText('Sie: Bitte den Lieferschein');

  // Verlauf über die API: verschlüsselt gespeichert, im Klartext nur für Mitglieder.
  const list = await apiCall<{ id: string; title: string; lastMessage: { text: string } }[]>(page, 'GET', '/api/v1/chat/conversations');
  expect(list.json.find((c) => c.title === name)?.lastMessage.text).toContain('Bitte den Lieferschein von heute ins Büro legen.');

  // Echtzeit: Nachricht, die nicht über das Eingabefeld kommt, erscheint ohne Neuladen (Ereignis statt 20-s-Abfrage).
  const external = await apiCall(page, 'POST', `/api/v1/chat/conversations/${conversationId}/messages`, { text: 'Kommt per Ereignisstrom' });
  expect(external.status).toBe(201);
  await expect(page.locator('.eoChatBubble').filter({ hasText: 'Kommt per Ereignisstrom' })).toBeVisible({ timeout: 8_000 });

  // Foto über „Anhängen → Fotos“ mit Vorschau vor dem Senden.
  await page.getByRole('button', { name: /Foto, Dokument, Kamera oder Standort/ }).click();
  const chooser = page.waitForEvent('filechooser');
  await page.getByRole('button', { name: 'Fotos' }).click();
  await (await chooser).setFiles({ name: 'Halle-2.jpg', mimeType: 'image/jpeg', buffer: await photo(page, 'Halle 2 · Heizestrich') });
  await expect(page.locator('.eoChatPendingItem')).toHaveCount(1);
  await input.fill('Halle 2 ist fertig abgezogen.');
  await input.press('Enter');
  const photoBubble = page.locator('.eoChatMine .eoChatBubble').filter({ hasText: 'Halle 2 ist fertig abgezogen.' });
  await expect(photoBubble.locator('.eoChatPhoto img')).toBeVisible();
  await expect.poll(() => photoBubble.locator('.eoChatPhoto img').evaluate((img: HTMLImageElement) => img.naturalWidth)).toBeGreaterThan(0);
  await expect(page.locator('.eoChatPendingItem')).toHaveCount(0);

  // Antworten mit Zitat und Reaktion.
  const first = page.locator('.eoChatMsg').filter({ hasText: 'Bitte den Lieferschein' });
  await first.hover();
  await first.getByRole('button', { name: 'Antworten' }).click();
  await expect(page.locator('.eoChatContext')).toContainText('Bitte den Lieferschein');
  await input.fill('Erledigt, liegt im Fach.');
  await input.press('Enter');
  await expect(page.locator('.eoChatMine').filter({ hasText: 'Erledigt, liegt im Fach.' }).locator('.eoChatQuote')).toContainText('Bitte den Lieferschein');
  const reply = page.locator('.eoChatMsg').filter({ hasText: 'Kommt per Ereignisstrom' });
  await reply.hover();
  await reply.getByRole('button', { name: 'Reagieren', exact: true }).click();
  await reply.getByRole('button', { name: 'Reagieren mit 👍' }).click();
  await expect(reply.locator('.eoChatReaction')).toContainText('👍');

  // Suche über alle Chats (Texte liegen verschlüsselt vor, der Server sucht nach dem Entschlüsseln).
  await page.getByPlaceholder('Chats und Nachrichten durchsuchen').fill('Lieferschein');
  await expect(page.locator('.eoChatHits')).toContainText('Lieferschein', { timeout: 8_000 });
  await page.getByPlaceholder('Chats und Nachrichten durchsuchen').fill('');
  await shot(page, 'chat-einzelchat');

  // Ankündigungsgruppe mit Beschreibung; Gruppeninfo zeigt Admins und Einstellungen.
  const group = await apiCall<{ id: string }>(page, 'POST', '/api/v1/chat/conversations', {
    title: 'Alle Mitarbeiter', memberIds: [claraId], description: 'Ankündigungen aus dem Büro', announcementOnly: true,
  });
  expect(group.status).toBe(201);
  await apiCall(page, 'POST', `/api/v1/chat/conversations/${group.json.id}/messages`, { text: 'Freitag 14 Uhr Betriebsversammlung im Lager.' });
  await page.goto(`/chat/${group.json.id}`);
  await expect(page.locator('.eoChatHead')).toContainText('Alle Mitarbeiter');
  await page.getByRole('button', { name: 'Gruppeninfo' }).click();
  const info = page.getByRole('dialog', { name: 'Gruppeninfo' });
  await expect(info).toContainText('Ankündigungsgruppe – nur Admins schreiben');
  await expect(info).toContainText('Admin');
  await shot(page, 'chat-gruppeninfo');
  await info.getByRole('button', { name: 'Schließen' }).click();

  // Abbestellen je Benutzer.
  const prefs = await apiCall(page, 'PUT', '/api/v1/notifications/preferences', { items: [{ event: 'chat.message', delivery: 'inbox' }] });
  expect(prefs.status).toBe(200);
  const back = await apiCall<{ items: { event: string; delivery: string }[] }>(page, 'GET', '/api/v1/notifications/preferences');
  expect(back.json.items.find((i) => i.event === 'chat.message')?.delivery).toBe('inbox');
  await apiCall(page, 'PUT', '/api/v1/notifications/preferences', { items: [{ event: 'chat.message', delivery: 'push' }] });
});
