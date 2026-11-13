import { describe, expect, it } from 'vitest';
import {
  akteButtonText, akteButtonVisible, chatDeepLink, chatPresentation, confirmationMatches, isTeamsUrl, jobText, personChatState, teamsNoticeKey, linkRequest, linkSummary, mappingState, teamsHome,
  crewLabel, participantHint, participantText, resolveParticipants,
  type BauakteTeams, type TeamsLink, type TeamsParticipants, type TeamsStatus, type TeamsUserMapping,
} from '../webapp/util/teams';
import { adminAreas, matchesArea } from '../webapp/util/adminCatalog';
import { navigation, routes } from '../webapp/util/routes';

const status = (chatMode: TeamsStatus['chatMode'], siteTeamUrl: string | null = null): TeamsStatus => ({
  chatMode, chatReadOnly: chatMode === 'teams', available: chatMode !== 'estrichOne', graphConfigured: false, postVia: 'off', canManage: false,
  siteTeamName: null, siteTeamUrl, siteTeamAppUrl: null, teamsWebUrl: 'https://teams.microsoft.com', teamsAppUrl: 'msteams:',
});

const link: TeamsLink = {
  kind: 'channel', created: true, displayName: '2026-0042 EFH Müller', webUrl: 'https://teams.microsoft.com/l/channel/19%3aabc/x?groupId=g',
  appUrl: 'msteams:/l/channel/19%3aabc/x?groupId=g', teamId: 'g', channelId: '19:abc', chatId: null, syncMembers: true, lastSyncAt: '2026-11-12T10:00:00Z',
  lastSyncError: null, syncedMembers: 4, unmappedMembers: 0, lastPostAt: null, lastPostError: null, hasWebhook: false, linkedAt: '2026-11-12T09:00:00Z',
  linkedByName: 'Chefin',
};

const akte = (over: Partial<BauakteTeams>): BauakteTeams => ({
  bauakteId: 'b', chatMode: 'both', link: null, canManage: false, canCreate: false, canCreateChannel: false, defaultLinkKind: 'channel', ...over,
});

describe('Chat-Modus', () => {
  it('ohne Teams-Anbindung bleibt alles wie bisher', () => {
    expect(chatPresentation(null)).toEqual({ chatLabel: 'Chat', chatWritable: true, showTeams: false });
    expect(chatPresentation(status('estrichOne'))).toEqual({ chatLabel: 'Chat', chatWritable: true, showTeams: false });
  });

  it('„Microsoft Teams“ macht den Chat zum Archiv und zeigt Teams', () => {
    expect(chatPresentation(status('teams'))).toEqual({ chatLabel: 'Chat-Archiv', chatWritable: false, showTeams: true });
    expect(chatPresentation(status('both'))).toEqual({ chatLabel: 'Chat', chatWritable: true, showTeams: true });
  });

  it('Teams-Ziel: Team „Baustellen“, sonst Teams allgemein', () => {
    expect(teamsHome(status('teams', 'https://teams.microsoft.com/l/team/x?groupId=g'))).toContain('/l/team/');
    expect(teamsHome(status('teams'))).toBe('https://teams.microsoft.com');
    expect(teamsHome(null)).toBe('https://teams.microsoft.com');
  });
});

describe('Bauakte', () => {
  it('Knopf nur im Teams-Modus und wenn es etwas zu öffnen oder zu verknüpfen gibt', () => {
    expect(akteButtonVisible(null)).toBe(false);
    expect(akteButtonVisible(akte({ chatMode: 'estrichOne', link }))).toBe(false);
    expect(akteButtonVisible(akte({}))).toBe(false);
    expect(akteButtonVisible(akte({ canManage: true }))).toBe(true);
    expect(akteButtonVisible(akte({ link }))).toBe(true);
    expect(akteButtonText(akte({ link }))).toBe('In Teams öffnen');
    expect(akteButtonText(akte({ canManage: true }))).toBe('Teams-Chat anlegen');
  });

  it('Zusammenfassung nennt Art, Name und Abgleich', () => {
    expect(linkSummary(link)).toBe('Kanal „2026-0042 EFH Müller“ · 4 Mitglieder abgeglichen');
    expect(linkSummary({ ...link, kind: 'groupChat', lastSyncError: 'fehlt' })).toContain('Gruppenchat');
    expect(linkSummary({ ...link, lastSyncError: 'fehlt' })).toContain('Abgleich mit Hinweis');
    expect(linkSummary(null)).toBe('Noch nicht mit Teams verknüpft');
  });

  it('öffnet nur echte Teams-Adressen', () => {
    expect(isTeamsUrl('https://teams.microsoft.com/l/chat/19:x/0')).toBe(true);
    expect(isTeamsUrl('https://teams.cloud.microsoft/l/chat/19:x/0')).toBe(true);
    expect(isTeamsUrl('http://teams.microsoft.com/l/chat')).toBe(false);
    expect(isTeamsUrl('https://teams.microsoft.com.example.org/l')).toBe(false);
    expect(isTeamsUrl('javascript:alert(1)')).toBe(false);
    expect(isTeamsUrl(null)).toBe(false);
  });

  it('Verknüpfungsanfrage aus der Auswahl', () => {
    expect(linkRequest('channel', '', true)).toEqual({ kind: 'channel', syncMembers: true });
    expect(linkRequest('groupChat', 'egal', false)).toEqual({ kind: 'groupChat', syncMembers: false });
    expect(linkRequest('url', ' https://teams.microsoft.com/l/channel/x/y ', false)).toEqual({ url: 'https://teams.microsoft.com/l/channel/x/y', syncMembers: false });
    expect(linkRequest('url', '', false)).toMatch(/Link aus Teams/);
    expect(linkRequest('url', 'https://example.org', false)).toMatch(/kein Link/);
  });
});

describe('Teams-Chat anlegen', () => {
  const person = (userId: string, upn: string | null) => ({ userId, name: userId.toUpperCase(), upn, fromEmail: false, reason: upn ? null : 'Kein Microsoft-Konto.' });
  const data: TeamsParticipants = {
    selfUserId: 'me', viaGraph: true, minMembers: 3, topic: '2026-001 · EFH',
    people: [person('me', 'me@x.de'), person('anna', 'anna@x.de'), person('bernd', 'bernd@x.de'), person('ohne', null)],
    options: [
      { key: 'team', kind: 'team', name: 'Bauakten-Team (Zuständige und Mitarbeiter)', userIds: ['anna'] },
      { key: 'crew:k1', kind: 'crew', name: 'Kolonne Nord', userIds: ['bernd', 'ohne', 'anna'] },
      { key: 'user:anna', kind: 'user', name: 'Anna', userIds: ['anna'] },
    ],
  };

  it('führt Gruppen und Personen ohne Doppelte zusammen, die eigene Person ist immer dabei', () => {
    const r = resolveParticipants(data, ['team', 'crew:k1', 'user:anna']);
    expect(r.withAccount.map((p) => p.userId).sort()).toEqual(['anna', 'bernd', 'me']);
    expect(r.withoutAccount.map((p) => p.userId)).toEqual(['ohne']);
    expect(participantText(data.options[1])).toBe('Kolonne Nord (3)');
    expect(participantText(data.options[2])).toBe('Anna');
    expect(crewLabel('Nord')).toBe('Kolonne Nord');
    expect(crewLabel(' Kolonne Süd ')).toBe('Kolonne Süd');
  });

  it('Hinweis nur, wenn nötig: ohne Konto bzw. zu wenige für einen Gruppenchat', () => {
    expect(participantHint(data, ['team', 'crew:k1'])).toMatch(/^1 Person hat kein Microsoft-Konto und wird nicht aufgenommen: OHNE\.$/);
    expect(participantHint(data, ['team'])).toContain('mindestens 3 Personen');
    expect(participantHint(data, [])).toBe('Bitte weitere Personen auswählen.');
    expect(participantHint({ ...data, people: data.people.map((p) => ({ ...p, upn: p.upn ?? 'x@x.de' })) }, ['crew:k1'])).toBe('');
    expect(participantHint({ ...data, viaGraph: false }, ['team'])).toBe(''); // Teams-Link: zwei Personen sind ein Einzelchat
  });
});

describe('Einstellungen', () => {
  it('Benutzerzuordnung verständlich', () => {
    const row: TeamsUserMapping = {
      userId: 'u', name: 'Anna', email: 'anna@firma.de', active: true, aadUserId: null, userPrincipalName: null, accountName: null, manual: false,
      error: null, checkedAt: null,
    };
    expect(mappingState({ ...row, aadUserId: 'x' })).toEqual({ text: 'automatisch zugeordnet', state: 'Success' });
    expect(mappingState({ ...row, aadUserId: 'x', manual: true }).text).toBe('von Hand zugeordnet');
    expect(mappingState({ ...row, error: 'Kein Konto' })).toEqual({ text: 'Kein Konto', state: 'Warning' });
    expect(mappingState({ ...row, email: null }).text).toMatch(/Keine E-Mail/);
  });

  it('Seite unter Integrationen mit eigenem Recht und auffindbar', () => {
    const area = adminAreas.find((a) => a.key === 'teamsSettings')!;
    expect(area.group).toBe('groupAdminIntegrations');
    expect(area.permission).toBe('settings.integrations');
    expect(routes.find((r) => r.pattern === area.path)?.permission).toBe('settings.integrations');
    expect(matchesArea(area, 'entra')).toBe(true);
    expect(navigation.some((n) => n.key === 'teams')).toBe(true);
  });
});

describe('Chat in Teams', () => {
  it('baut Deep Links wie der Server (Einzelchat ohne Thema, Gruppenchat mit Thema und Text)', () => {
    expect(chatDeepLink(['dieter@example.org'], 'Thema', null)).toBe('https://teams.microsoft.com/l/chat/0/0?users=dieter@example.org');
    expect(chatDeepLink(['a@x.de', 'b+c@x.de'], 'Bauakte 4711 EFH & Co', 'Hallo'))
      .toBe('https://teams.microsoft.com/l/chat/0/0?users=a@x.de,b%2Bc@x.de&topicName=Bauakte%204711%20EFH%20%26%20Co&message=Hallo');
    expect(isTeamsUrl(chatDeepLink(['a@x.de']))).toBe(true);
  });

  it('bietet „Chat in Teams“ nur mit Microsoft-Konto an', () => {
    expect(personChatState({ userId: '1', name: 'Anna', upn: 'anna@x.de', fromEmail: false, reason: null }).enabled).toBe(true);
    const none = personChatState({ userId: '2', name: 'Bernd', upn: null, fromEmail: false, reason: 'Kein Microsoft-Konto.' });
    expect(none.enabled).toBe(false);
    expect(none.tooltip).toContain('Kein Microsoft-Konto');
    expect(personChatState({ userId: '1', name: 'Ich', upn: 'ich@x.de', fromEmail: false, reason: null }, true).enabled).toBe(false);
    expect(personChatState(undefined).enabled).toBe(false);
  });

  it('prüft die Lösch-Bestätigung und beschreibt den Fortschritt', () => {
    expect(confirmationMatches(' 2026-001 ', '2026-001')).toBe(true);
    expect(confirmationMatches('2026-002', '2026-001')).toBe(false);
    expect(confirmationMatches('', '')).toBe(false);
    const job = { id: 'j', kind: 'channel' as const, total: 4, processed: 2, linked: 2, skipped: 0, failed: 0, errors: [], error: null,
      startedAt: '', finishedAt: null, startedByName: null };
    expect(jobText({ ...job, state: 'running' })).toBe('Läuft: 2 von 4 Bauakten (2 verknüpft, 0 übersprungen, 0 fehlgeschlagen)');
    expect(jobText({ ...job, state: 'failed', error: 'Keine Verbindung' })).toContain('Keine Verbindung');
    expect(teamsNoticeKey('A')).not.toBe(teamsNoticeKey('B'));
  });
});
