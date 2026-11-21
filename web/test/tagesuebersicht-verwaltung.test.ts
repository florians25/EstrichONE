import { describe, expect, it } from 'vitest';
import { groupByCrew, relativeDay } from '../webapp/util/today';
import { adminAreas, adminGroups, adminSections, companyTabKeys, matchesArea } from '../webapp/util/adminCatalog';
import { adminStatus } from '../webapp/util/adminStatus';
import { fixedNavigation, navKeyFor, navigation, navTargets, routes } from '../webapp/util/routes';
import type { AssignmentView } from '../webapp/util/models';

const a = (over: Partial<AssignmentView>): AssignmentView => ({
  id: 'x', day: '2026-11-04', startTime: null, endTime: null, activity: '', bauakteId: 'b', bauakteNumber: '2026-001', bauakteTitle: 'Halle',
  bauakteAddress: null, crewId: null, crewName: null, crewColor: null, people: [], resources: [], notes: null, warnings: [], version: 1,
  kind: 'screed', bauakteStatus: null, ...over,
});

describe('Tagesübersicht', () => {
  it('Tagesbezeichnungen relativ zu heute', () => {
    expect(relativeDay('2026-11-04', '2026-11-04')).toBe('Heute');
    expect(relativeDay('2026-11-05', '2026-11-04')).toBe('Morgen');
    expect(relativeDay('2026-11-03', '2026-11-04')).toBe('Gestern');
    expect(relativeDay('2026-11-07', '2026-11-04')).toBe('in 3 Tagen');
    expect(relativeDay('2026-10-30', '2026-11-04')).toBe('vor 5 Tagen');
  });

  it('Einsätze je Kolonne, nach Beginn sortiert, ohne Kolonne am Ende', () => {
    const crews = groupByCrew([
      a({ id: '1', crewId: 'k2', crewName: 'Zeta', startTime: '09:00:00', people: [{ id: 'p1', name: 'Anna' }] }),
      a({ id: '2', crewId: null, startTime: '07:00:00' }),
      a({ id: '3', crewId: 'k1', crewName: 'Alpha', startTime: '10:00:00', endTime: '12:30:00', warnings: ['Doppelt verplant', 'Gerät fehlt'] }),
      a({ id: '4', crewId: 'k2', crewName: 'Zeta', startTime: '07:30:00', people: [{ id: 'p1', name: 'Anna' }, { id: 'p2', name: 'Ben' }] }),
    ]);
    expect(crews.map((c) => c.name)).toEqual(['Alpha', 'Zeta', 'Ohne Kolonne']);
    expect(crews[1].items.map((i) => i.id)).toEqual(['4', '1']);
    expect(crews[1].countText).toBe('2 Einsätze, 2 Personen');
    expect(crews[0].items[0].startText).toBe('10:00');
    expect(crews[0].items[0].endText).toBe('bis 12:30');
    expect(crews[0].warningText).toBe('2 Hinweise');
    expect(crews[2].items[0].kindText).toBe('Estrich / Ausführung');
  });

  it('ganztägige Einsätze', () => {
    const [crew] = groupByCrew([a({ activity: 'Estrich einbringen EG' })]);
    expect(crew.items[0].startText).toBe('ganztags');
    expect(crew.items[0].kindText).toBe('Estrich / Ausführung, Estrich einbringen EG');
  });
});

describe('Verwaltung', () => {
  it('jeder Bereich gehört zu einer Gruppe und hat einen eindeutigen Schlüssel', () => {
    const keys = new Set(adminGroups.map((g) => g.key));
    expect(adminAreas.every((x) => keys.has(x.group))).toBe(true);
    expect(new Set(adminAreas.map((x) => x.key)).size).toBe(adminAreas.length);
  });

  it('jede Seite ist erreichbar: Navigation oder Verwaltungsübersicht – keine verloren', () => {
    const navPaths = navTargets().map((e) => e.path);
    const areaPaths = adminAreas.map((x) => x.path);
    for (const r of routes.filter((x) => x.nav && !x.pattern.includes(':') && x.name !== 'dispatch')) {
      expect([...navPaths, ...areaPaths, '/personal/urlaub', '/planung/tageseinteilung', '/planung/ressourcen', '/benachrichtigungen'], r.pattern)
        .toContain(r.pattern);
    }
    // Verwaltung unten fest; ihre Seiten markieren „Verwaltung“.
    expect(fixedNavigation.map((e) => e.key)).toEqual(['groupHelpdesk', 'adminHub']);
    expect(navKeyFor('backup', new URLSearchParams())).toBe('adminHub');
    expect(navKeyFor('users', new URLSearchParams())).toBe('adminHub');
    expect(navKeyFor('helpdesk', new URLSearchParams())).toBe('helpdesk');
  });

  it('Gruppen oben mit höchstens sieben Einträgen, Schlüssel eindeutig', () => {
    for (const g of navigation.filter((e) => e.children)) expect(g.children!.length, g.key).toBeLessThanOrEqual(7);
    const keys = navTargets().map((e) => e.key);
    expect(new Set(keys).size).toBe(keys.length);
    expect(navigation.map((e) => e.key)).toEqual(['start', 'chat', 'teams', 'groupOrders', 'groupPlanning', 'groupMaterial', 'groupOffice', 'groupHr']);
    expect(navigation.find((e) => e.key === 'groupPlanning')?.children?.map((e) => e.key)).toContain('crews');
  });

  it('Suche über Titel, Beschreibung und Suchbegriffe, ohne Umlaut-Probleme', () => {
    const find = (q: string) => adminAreas.filter((x) => matchesArea(x, q)).map((x) => x.key);
    expect(find('backup')).toContain('backup');
    expect(find('logo')).toEqual(['company']);
    expect(find('UBERWACHUNG')).toContain('monitoring');
    expect(find('apns')).toEqual(['push']);
    expect(find('gibt es nicht')).toEqual([]);
  });

  it('Rechte filtern Bereiche und leere Gruppen', () => {
    const sections = adminSections((p) => p === 'users.manage');
    expect(sections.map((s) => s.key)).toEqual(['groupAdminUsers', 'groupHelpdesk']);
    expect(sections[0].areas.map((x) => x.key)).toEqual(['users', 'roles']);
    expect(sections[1].areas.map((x) => x.key)).toEqual(['helpdesk', 'knowledge']);
  });

  it('IT-Service/Helpdesk ist eine eigene Gruppe unten in der Seitennavigation', () => {
    const group = fixedNavigation.find((e) => e.key === 'groupHelpdesk');
    expect(group?.children?.map((e) => e.key)).toEqual(['helpdesk', 'helpdeskNew', 'knowledge', 'myFeedback', 'feedbackAdmin', 'helpdeskSettings']);
    expect(group?.children?.find((e) => e.key === 'feedbackAdmin')?.permission).toBe('feedback.manage');
    expect(group?.children?.find((e) => e.key === 'helpdeskSettings')?.permission).toBe('helpdesk.agent');
    const others = navigation.flatMap((e) => e.children ?? [e]).map((e) => e.key);
    expect(others).not.toContain('helpdesk');
    expect(others).not.toContain('knowledge');
    expect(others).not.toContain('feedbackAdmin');
    expect(navKeyFor('helpdesk', new URLSearchParams('neu=1'))).toBe('helpdeskNew');
    // „Verwaltung“ nur mit Verwaltungsrechten – nicht allein mit Helpdesk-Rechten.
    const admin = fixedNavigation.find((e) => e.key === 'adminHub');
    expect(admin?.anyPermission).toContain('settings.manage');
    expect(admin?.anyPermission).not.toContain('helpdesk.agent');
    expect(admin?.anyPermission).not.toContain('feedback.manage');
  });

  it('Reiter der Systemeinstellungen sind Verwaltungsbereiche und markieren „Verwaltung“', () => {
    expect(navKeyFor('company', new URLSearchParams('bereich=speicher'))).toBe('adminHub');
    expect(Object.values(companyTabKeys).every((k) => adminAreas.some((x) => x.key === k))).toBe(true);
  });

  it('Statuszeile: Alles in Ordnung oder Handlungsbedarf', () => {
    expect(adminStatus([false, null], '3 Konten')).toMatchObject({ type: 'Success', text: 'Alles in Ordnung, 3 Konten' });
    expect(adminStatus(['A fehlt', { text: 'B kaputt', level: 'error' }], 'x')).toMatchObject({ type: 'Error', text: 'Handlungsbedarf: B kaputt, A fehlt' });
    expect(adminStatus([{ text: '2 wartend', level: 'info' }], '5 archiviert')).toMatchObject({ type: 'Information', text: '5 archiviert, 2 wartend' });
  });
});
