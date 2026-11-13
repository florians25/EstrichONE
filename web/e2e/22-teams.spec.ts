import { expect, test, type Page } from '@playwright/test';
import { adminPassword, apiCall, nav, signIn, toast } from './helpers';

// Microsoft Teams ohne echten Tenant: Chat-Modus, Teams-Chat anlegen (ohne App-Registrierung: Teams-Link), Verknüpfen per Link,
// „In Teams öffnen“, Kolonne „Teams-Chat“, Chat-Archiv.
const TEAMS_LINK = 'https://teams.microsoft.com/l/channel/19%3ae2e0001%40thread.tacv2/E2E%20Teams?groupId=a1b2c3d4-0000-4000-8000-00000000babe&tenantId=t';

const shot = async (page: Page, name: string, keepOpen = false) => {
  if (!process.env['SCREENSHOTS']) return;
  // keepOpen: offene Menüs/Dialoge nicht durch Größenänderung schließen (nur aktuelle Größe).
  for (const width of keepOpen ? [0] : [1920, 1280]) {
    if (width) await page.setViewportSize({ width, height: width === 1920 ? 1080 : 800 });
    await page.waitForTimeout(800);
    await page.screenshot({ path: `test-results/screenshots/teams-${name}-${width || 'offen'}.png` });
  }
  if (!keepOpen) await page.setViewportSize({ width: 1440, height: 900 });
};

test('Microsoft Teams: Einstellungen, Chat-Modus, Bauakte per Link verknüpfen, In Teams öffnen, Chat-Archiv', async ({ page, context }) => {
  // Teams selbst wird nicht aufgerufen: neue Fenster auf teams.microsoft.com bekommen eine leere Seite.
  await context.route('https://teams.microsoft.com/**', (route) => route.fulfill({ status: 200, contentType: 'text/html', body: '<title>Teams</title>Teams' }));
  await signIn(page, 'admin', adminPassword);

  // Ohne Einrichtung: wie bisher – kein Teams-Eintrag, keine Teams-Knöpfe.
  const initial = await apiCall<{ chatMode: string; available: boolean }>(page, 'GET', '/api/v1/teams/status');
  expect(initial.json.chatMode).toBe('estrichOne');
  expect(initial.json.available).toBe(false);
  // Chat an und Datenschutzhinweis bestätigt (wie nach 11-chat), damit das Archiv ohne Dialog erscheint.
  expect((await apiCall(page, 'PUT', '/api/v1/chat/admin/settings', { enabled: true, retentionDays: 90 })).status).toBe(200);
  expect((await apiCall(page, 'PUT', '/api/v1/chat/me/settings', { privacyAccepted: true })).status).toBe(200);

  await page.goto('/system/microsoft-teams');
  await expect(page.getByText('Nicht eingerichtet – der eingebaute Chat arbeitet wie bisher')).toBeVisible();
  await page.getByRole('tab', { name: 'Berechtigungen' }).click();
  await expect(page.getByText('User.Read.All')).toBeVisible();
  await expect(page.getByText('ChannelMessage.Send')).toBeVisible();
  await page.getByRole('tab', { name: 'Verbindung' }).click();

  // Chat-Modus „Microsoft Teams“ speichern (ohne App-Registrierung: Verknüpfen per Link).
  await page.getByRole('option', { name: 'Microsoft Teams' }).click();
  await expect(page.getByText(/Der eingebaute Chat wird zum Archiv/)).toBeVisible();
  await page.locator('[id$="teamsSave"]').filter({ visible: true }).click();
  await expect(toast(page)).toHaveText('Gespeichert');
  await expect(page.getByText(/Chat-Modus: Microsoft Teams/)).toBeVisible();
  await toast(page).waitFor({ state: 'hidden', timeout: 8000 }).catch(() => undefined);
  await shot(page, 'einstellungen');

  // Navigation: „Microsoft Teams“ öffnet Teams, der Chat heißt „Chat-Archiv“ (Chat-Modul aus 11-chat ist an).
  await expect(nav(page, 'Chat-Archiv')).toBeVisible();
  const [teamsTab] = await Promise.all([page.waitForEvent('popup'), nav(page, 'Microsoft Teams').first().click()]);
  expect(teamsTab.url()).toContain('teams.microsoft.com');
  await teamsTab.close();
  await page.goto('/start');
  await expect(page.getByRole('button', { name: 'In Teams öffnen' })).toBeVisible();
  await shot(page, 'navigation');

  // Bauakte: ein Knopf „Teams-Chat anlegen“; Menü nur mit Anlegen und Per Link verknüpfen.
  const id = crypto.randomUUID();
  const created = await apiCall(page, 'POST', '/api/v1/bauakten', { id, title: 'EFH Teams, Heizestrich', city: 'Landshut' });
  expect(created.status).toBe(201);
  const me = await apiCall<{ id: string }>(page, 'GET', '/api/v1/auth/me');
  const people = await apiCall<{ userId: string; upn: string | null }[]>(page, 'GET', '/api/v1/teams/people');
  expect(people.status).toBe(200);
  const other = people.json.find((p) => p.upn && p.userId !== me.json.id);
  if (other) expect((await apiCall(page, 'PUT', `/api/v1/bauakten/${id}/members`, { userIds: [other.userId] })).status).toBe(200);
  await page.goto(`/bauakten/${id}`);
  const button = page.locator('[id$="akteTeamsButton"]').filter({ visible: true }).first();
  await expect(button).toContainText('Teams-Chat anlegen');
  await expect(page.locator('[id$="chatGroupButton"]').filter({ visible: true })).toHaveCount(0);
  await button.locator('.sapMSBArrow, .sapMBtnInner').last().click();
  await expect(page.getByRole('menuitem', { name: 'Teams-Chat anlegen …' })).toBeVisible();
  await expect(page.getByRole('menuitem', { name: 'Per Link verknüpfen …' })).toBeVisible();
  await expect(page.getByRole('menuitem')).toHaveCount(2);
  await shot(page, 'bauakte-menue', true);

  // „Teams-Chat anlegen …“: Name vorbelegt, Bauakten-Team vorausgewählt; ohne App-Registrierung öffnet sich Teams (nicht gespeichert).
  await page.getByRole('menuitem', { name: 'Teams-Chat anlegen …' }).click();
  const createDialog = page.getByRole('dialog', { name: 'Teams-Chat anlegen' });
  await expect(createDialog).toBeVisible();
  await expect(createDialog.getByLabel('Name')).toHaveValue(/EFH Teams, Heizestrich/);
  await expect(createDialog.getByText(/Bauakten-Team \(Zuständige und Mitarbeiter\)/)).toBeVisible();
  await expect(createDialog.getByText(/nicht in der Bauakte gespeichert/)).toBeVisible();
  await shot(page, 'chat-anlegen', true);
  if (other) {
    const [chat] = await Promise.all([page.waitForEvent('popup'), createDialog.getByRole('button', { name: 'Anlegen' }).click()]);
    expect(chat.url()).toContain('/l/chat/0/0?users=');
    expect(decodeURIComponent(chat.url())).toContain('EFH Teams, Heizestrich');
    await chat.close();
    await expect(createDialog).toBeHidden();
  } else {
    await createDialog.getByRole('button', { name: 'Abbrechen' }).click();
  }
  await expect(button).toContainText('Teams-Chat anlegen');

  // „Per Link verknüpfen …“: vorhandener Kanal → „In Teams öffnen“.
  await button.locator('.sapMSBArrow, .sapMBtnInner').last().click();
  await page.getByRole('menuitem', { name: 'Per Link verknüpfen …' }).click();
  const dialog = page.getByRole('dialog', { name: 'Per Link verknüpfen' });
  await expect(dialog).toBeVisible();
  await dialog.getByLabel('Link aus Teams').fill(TEAMS_LINK);
  await dialog.getByRole('button', { name: 'Verknüpfen' }).click();
  await expect(dialog).toBeHidden();
  await expect(button).toContainText('In Teams öffnen');
  const link = await apiCall<{ link: { webUrl: string; appUrl: string; displayName: string } }>(page, 'GET', `/api/v1/bauakten/${id}/teams`);
  expect(link.json.link.displayName).toBe('E2E Teams');
  expect(link.json.link.appUrl).toMatch(/^msteams:\/l\/channel\//);
  const [channel] = await Promise.all([page.waitForEvent('popup'), button.locator('.sapMSBText, .sapMBtnInner').first().click()]);
  expect(channel.url()).toContain('/l/channel/');
  await channel.close();
  await shot(page, 'bauakte');

  // Chat-Archiv: lesen und exportieren, nicht schreiben.
  await page.goto('/chat');
  await expect(page.getByText(/Unterhaltungen finden jetzt in Microsoft Teams statt/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Neuer Einzelchat' })).toHaveCount(0);
  await expect(page.locator('[id$="chatExport"]').filter({ visible: true })).toHaveCount(1);
  await shot(page, 'chat-archiv');

  // Chat-Archiv: neuer Chat führt nach Teams (Personenauswahl), kein eingebauter Einzelchat.
  await expect(page.locator('[id$="chatNewTeams"]').filter({ visible: true })).toHaveCount(1);

  // Einzel- und Gruppenchats per Deep Link (ohne App-Registrierung über die E-Mail-Adresse).
  if (other) {
    const chat = await apiCall<{ webUrl: string; appUrl: string; viaGraph: boolean }>(page, 'POST', '/api/v1/teams/chats', { userIds: [other.userId] });
    expect(chat.status).toBe(200);
    expect(chat.json.webUrl).toBe(`https://teams.microsoft.com/l/chat/0/0?users=${other.upn}`);
    expect(chat.json.appUrl).toMatch(/^msteams:\/l\/chat\/0\/0\?users=/);
    expect(chat.json.viaGraph).toBe(false);
  }

  // Bauakte ohne App-Registrierung: kein Löschen in Teams, nur „Verknüpfung lösen“.
  await page.goto(`/bauakten/${id}`);
  await button.locator('.sapMSBArrow, .sapMBtnInner').last().click();
  await expect(page.getByRole('menuitem', { name: 'Teams-Chat löschen …' })).toHaveCount(0);
  await expect(page.getByRole('menuitem', { name: 'Verknüpfung lösen' })).toBeVisible();
  await page.keyboard.press('Escape');

  // Kolonnen: Knopf „Teams-Chat“ je Kolonne (ohne App-Registrierung: Teams-Link mit den Mitgliedern).
  const crewId = crypto.randomUUID();
  const crewMembers = other ? [me.json.id, other.userId] : [me.json.id];
  expect((await apiCall(page, 'PUT', `/api/v1/planning/crews/${crewId}`, { name: 'E2E Teams', memberUserIds: crewMembers, isActive: true })).status).toBe(200);
  const crews = await apiCall<{ available: boolean; viaGraph: boolean; chats: unknown[] }>(page, 'GET', '/api/v1/teams/crews');
  expect(crews.json.available).toBe(true);
  expect(crews.json.viaGraph).toBe(false);
  await page.goto('/planung/kolonnen');
  const crewRow = page.getByRole('row').filter({ hasText: 'E2E Teams' });
  await expect(crewRow.getByRole('button', { name: 'Teams-Chat' })).toBeVisible();
  await shot(page, 'kolonnen');
  if (other) {
    const [crewChat] = await Promise.all([page.waitForEvent('popup'), crewRow.getByRole('button', { name: 'Teams-Chat' }).click()]);
    expect(crewChat.url()).toContain(`users=`);
    await crewChat.close();
  }
  expect((await apiCall(page, 'PUT', `/api/v1/planning/crews/${crewId}`, { name: 'E2E Teams', memberUserIds: crewMembers, isActive: false })).status).toBe(200);

  // Umstellung: Stand der Konten und Bauakten; Massenverknüpfung braucht die App-Registrierung.
  await page.goto('/system/microsoft-teams');
  await page.getByRole('tab', { name: 'Umstellung' }).click();
  await expect(page.getByText(/mit Teams verknüpft, \d+ ohne Verknüpfung/)).toBeVisible();
  await expect(page.getByText('Dafür ist die App-Registrierung nötig (Reiter „Verbindung“).')).toBeVisible();
  await expect(page.locator('[id$="tmLinkAll"]').filter({ visible: true })).toBeDisabled();
  await shot(page, 'umstellung');

  // Hinweis „Ab sofort chatten wir in Teams“: einmal auf der Startseite, wegklickbar und gemerkt.
  const notice = await apiCall(page, 'PUT', '/api/v1/settings/integrations/teams', { chatMode: 'teams', rolloutNotice: true });
  expect(notice.status).toBe(200);
  await page.goto('/start');
  await page.reload();
  const strip = page.locator('[id$="teamsNotice"]').filter({ visible: true });
  await expect(strip).toContainText('Ab sofort chatten wir in Microsoft Teams');
  await expect(strip.getByRole('link', { name: 'Teams installieren' })).toBeVisible();
  await shot(page, 'hinweis');
  await strip.getByRole('button').last().click();
  await expect(strip).toHaveCount(0);
  await page.reload();
  await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible();
  await expect(page.locator('[id$="teamsNotice"]').filter({ visible: true })).toHaveCount(0);

  // Verknüpfung lösen und Modus zurück auf den eingebauten Chat (folgende Tests erwarten ihn).
  const unlink = await apiCall(page, 'DELETE', `/api/v1/bauakten/${id}/teams`);
  expect(unlink.status).toBe(204);
  const reset = await apiCall(page, 'PUT', '/api/v1/settings/integrations/teams', { chatMode: 'estrichOne' });
  expect(reset.status).toBe(200);
});
