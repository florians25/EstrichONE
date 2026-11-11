import { describe, expect, it } from 'vitest';
import { groupByCrew, relativeDay } from '../webapp/util/today';
import { adminAreas, adminGroups, adminSections, companyTabKeys, matchesArea } from '../webapp/util/adminCatalog';
import { adminStatus } from '../webapp/util/adminStatus';
import { navKeyFor, navigation, routes } from '../webapp/util/routes';
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
    expect(crews[1].countText).toBe('2 Einsätze · 2 Personen');
    expect(crews[0].items[0].startText).toBe('10:00');
    expect(crews[0].items[0].endText).toBe('bis 12:30');
    expect(crews[0].warningText).toBe('2 Hinweise');
    expect(crews[2].items[0].kindText).toBe('Estrich / Ausführung');
  });

  it('ganztägige Einsätze', () => {
    const [crew] = groupByCrew([a({ activity: 'Estrich einbringen EG' })]);
    expect(crew.items[0].startText).toBe('ganztags');
    expect(crew.items[0].kindText).toBe('Estrich / Ausführung · Estrich einbringen EG');
  });
});

describe('Verwaltung', () => {
  it('jeder Bereich gehört zu einer Gruppe und hat einen eindeutigen Schlüssel', () => {
    const keys = new Set(adminGroups.map((g) => g.key));
    expect(adminAreas.every((x) => keys.has(x.group))).toBe(true);
    expect(new Set(adminAreas.map((x) => x.key)).size).toBe(adminAreas.length);
  });

  it('jede Verwaltungsseite steht in der Navigation (Gruppen) – keine verloren', () => {
    const navPaths = navigation.flatMap((e) => e.children ?? [e]).map((e) => e.path);
    for (const r of routes.filter((x) => x.view.startsWith('admin.') && !x.pattern.includes(':') && x.name !== 'messages')) {
      expect(navPaths, r.pattern).toContain(r.pattern);
    }
    expect(navPaths).toContain('/verwaltung');
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
    expect(sections.map((s) => s.key)).toEqual(['groupAdminUsers']);
    expect(sections[0].areas.map((x) => x.key)).toEqual(['users', 'roles', 'helpdesk', 'knowledge']);
  });

  it('Reiter der Systemeinstellungen markieren ihren Navigationseintrag', () => {
    expect(navKeyFor('company', new URLSearchParams('bereich=speicher'))).toBe('storage');
    expect(navKeyFor('company', new URLSearchParams('bereich=firma'))).toBe('company');
    expect(Object.values(companyTabKeys).every((k) => adminAreas.some((x) => x.key === k))).toBe(true);
  });

  it('Statuszeile: Alles in Ordnung oder Handlungsbedarf', () => {
    expect(adminStatus([false, null], '3 Konten')).toMatchObject({ type: 'Success', text: 'Alles in Ordnung · 3 Konten' });
    expect(adminStatus(['A fehlt', { text: 'B kaputt', level: 'error' }], 'x')).toMatchObject({ type: 'Error', text: 'Handlungsbedarf: B kaputt · A fehlt' });
    expect(adminStatus([{ text: '2 wartend', level: 'info' }], '5 archiviert')).toMatchObject({ type: 'Information', text: '5 archiviert · 2 wartend' });
  });
});
