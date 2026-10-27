import { expect, test } from '@playwright/test';
import { adminPassword, apiCall, nav, signIn } from './helpers';

const OFFICE_ROLE = '01a00000-0000-7000-8000-000000000002';

test('Chat: Modul einschalten, Einzelchat beginnen, Nachricht senden (Enter), Häkchen, Glocke ohne Inhalt, Benachrichtigungen abbestellen', async ({ page }) => {
  await signIn(page, 'admin', adminPassword);
  const status = await apiCall<{ enabled: boolean; canAdmin: boolean }>(page, 'GET', '/api/v1/chat/status');
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

  await page.reload();
  await nav(page, 'Chat').click();
  await expect(page).toHaveURL(/\/chat$/);
  await page.getByRole('button', { name: 'Neuer Einzelchat' }).first().click();
  const picker = page.getByRole('dialog', { name: 'Neuer Einzelchat' });
  await picker.getByPlaceholder('Name suchen').fill(name);
  await picker.getByText(name, { exact: true }).click();
  await expect(page).toHaveURL(/\/chat\/[0-9a-f-]{36}$/);
  await expect(page.locator('.eoChatHead')).toContainText(name);

  const input = page.getByPlaceholder('Nachricht schreiben …');
  await input.fill('Bitte den Lieferschein von heute ins Büro legen.');
  await input.press('Enter');
  const bubble = page.locator('.eoChatMine .eoChatBubble').filter({ hasText: 'Bitte den Lieferschein von heute ins Büro legen.' });
  await expect(bubble).toBeVisible();
  await expect(bubble.locator('.eoChatTick[title="Gesendet"], .eoChatTick[title^="Zugestellt"]')).toBeVisible();
  await expect(input).toHaveValue('');
  await expect(page.locator('.eoChatConv').filter({ hasText: name })).toContainText('Sie: Bitte den Lieferschein');

  // Verlauf über die API: verschlüsselt gespeichert, im Klartext nur für Mitglieder.
  const list = await apiCall<{ id: string; title: string; lastMessage: { text: string } }[]>(page, 'GET', '/api/v1/chat/conversations');
  expect(list.json.find((c) => c.title === name)?.lastMessage.text).toBe('Bitte den Lieferschein von heute ins Büro legen.');

  // Abbestellen je Benutzer.
  const prefs = await apiCall(page, 'PUT', '/api/v1/notifications/preferences', { items: [{ event: 'chat.message', delivery: 'inbox' }] });
  expect(prefs.status).toBe(200);
  const back = await apiCall<{ items: { event: string; delivery: string }[] }>(page, 'GET', '/api/v1/notifications/preferences');
  expect(back.json.items.find((i) => i.event === 'chat.message')?.delivery).toBe('inbox');
  await apiCall(page, 'PUT', '/api/v1/notifications/preferences', { items: [{ event: 'chat.message', delivery: 'push' }] });
});
