import { describe, expect, it } from 'vitest';
import {
  akteButtonText, akteButtonVisible, chatPresentation, isTeamsUrl, linkRequest, linkSummary, mappingState, teamsHome,
  type BauakteTeams, type TeamsLink, type TeamsStatus, type TeamsUserMapping,
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
    expect(akteButtonText(akte({ canManage: true }))).toBe('Mit Teams verknüpfen');
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
