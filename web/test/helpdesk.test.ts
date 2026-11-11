import { describe, expect, it } from 'vitest';
import {
  articleHtml, auditRows, compareTickets, describeRule, deviceLine, duration, fillPlaceholders, formatHolidays, formatMinutes, insertMention, kpiTiles,
  mentionCandidates, mentionedAgents, mentionQuery, pageText, parseHolidays, parseMinutes, queueViews, slaInfo, ticketChannels,
  type HdSummary, type HelpdeskStats,
} from '../webapp/util/helpdesk';
import { routes } from '../webapp/util/routes';

const ticket = (patch: Partial<HdSummary>): HdSummary => ({
  id: crypto.randomUUID(), number: 'T-2026-0001', title: 'Drucker', category: 'officeIt', priority: 'normal', status: 'new', reporterBlocked: false,
  reporterUserId: null, reporterDisplayName: 'Lukas Brandl', assigneeUserId: null, assigneeName: null, source: 'web',
  createdAt: '2026-11-09T07:00:00Z', lastActivityAt: '2026-11-09T07:00:00Z', version: 1, ...patch,
});

describe('Helpdesk: Ampel und Zeiten', () => {
  const now = Date.parse('2026-11-11T10:00:00Z');

  it('beschreibt überfällige, bald fällige und angehaltene Ziele verständlich', () => {
    expect(slaInfo({ slaState: 'overdue', dueAt: '2026-11-11T07:30:00Z', dueKind: 'response' }, now))
      .toMatchObject({ text: 'Antwort überfällig seit 2 h 30 min', tone: 'Error' });
    expect(slaInfo({ slaState: 'dueSoon', dueAt: '2026-11-11T10:45:00Z', dueKind: 'resolution' }, now))
      .toMatchObject({ text: 'Lösung fällig in 45 min', tone: 'Warning' });
    expect(slaInfo({ slaState: 'paused', dueAt: '2026-11-12T10:00:00Z', dueKind: 'resolution' }, now).text).toBe('angehalten (Rückfrage)');
    expect(slaInfo({ slaState: 'ok', dueAt: '2026-11-12T15:00:00Z', dueKind: 'resolution' }, now).text).toMatch(/^Lösung bis Do\., 12\.11\., \d{2}:00/);
    expect(slaInfo({ slaState: null, dueAt: null, dueKind: null }, now).text).toBe('–');
  });

  it('formatiert Dauern und liest Zeiteingaben', () => {
    expect(duration(45 * 60_000)).toBe('45 min');
    expect(duration(26 * 3_600_000)).toBe('1 T 2 h');
    expect(formatMinutes(65)).toBe('1:05 h');
    expect(formatMinutes(40)).toBe('40 min');
    expect(parseMinutes('45')).toBe(45);
    expect(parseMinutes('1:30')).toBe(90);
    expect(parseMinutes('1,5 h')).toBe(90);
    expect(parseMinutes('2 Std')).toBe(120);
    expect(parseMinutes('0,25')).toBe(15);
    expect(parseMinutes('90 min')).toBe(90);
    expect(parseMinutes('bald')).toBeNull();
  });
});

describe('Helpdesk: Arbeitsliste', () => {
  it('sortiert nach Spalten, Priorität fachlich und Fälligkeit ohne Ziel zuletzt', () => {
    const a = ticket({ number: 'T-2026-0002', priority: 'low', dueAt: '2026-11-11T12:00:00Z', assigneeName: 'Zoe' });
    const b = ticket({ number: 'T-2026-0010', priority: 'urgent', dueAt: null, assigneeName: 'Anna' });
    const c = ticket({ number: 'T-2026-0003', priority: 'high', dueAt: '2026-11-11T09:00:00Z', assigneeName: null, reporterBlocked: true });
    const list = [a, b, c];
    expect([...list].sort((x, y) => compareTickets(x, y, 'priority')).map((t) => t.priority)).toEqual(['urgent', 'high', 'low']);
    expect([...list].sort((x, y) => compareTickets(x, y, 'due')).map((t) => t.number)).toEqual(['T-2026-0003', 'T-2026-0002', 'T-2026-0010']);
    expect([...list].sort((x, y) => compareTickets(x, y, 'number', true)).map((t) => t.number)).toEqual(['T-2026-0010', 'T-2026-0003', 'T-2026-0002']);
    expect([...list].sort((x, y) => compareTickets(x, y, 'assignee')).map((t) => t.assigneeName)).toEqual(['Anna', 'Zoe', null]);
    expect([...list].sort((x, y) => compareTickets(x, y, 'waiting'))[0]).toBe(c);
  });

  it('bietet die gespeicherten Ansichten der IT an', () => {
    expect(queueViews.map((v) => v.text)).toEqual(
      ['Meine', 'Nicht zugewiesen', 'Überfällig', 'Wartet auf Rückmeldung', 'Alle offenen', 'Meine Gruppen', 'Erledigt', 'Alle']);
  });

  it('hat Seiten für Arbeitsplatz, Einstellungen und Wissensdatenbank', () => {
    const paths = routes.map((r) => r.pattern);
    expect(paths).toContain('/it-center/helpdesk');
    expect(paths).toContain('/it-center/helpdesk-einstellungen');
    expect(paths).toContain('/wissen');
    expect(paths).toContain('/wissen/:id');
  });
});

describe('Helpdesk: Antworten', () => {
  it('füllt Textbausteine mit Melder, Ticket und Bearbeiter', () => {
    expect(fillPlaceholders('Hallo {Vorname}, zu {Ticket}: bitte neu starten. Gruß {Bearbeiter}',
      { reporter: 'Lukas Brandl (laut Eingabe)', ticket: 'T-2026-0007', agent: 'Anna Berger' }))
      .toBe('Hallo Lukas, zu T-2026-0007: bitte neu starten. Gruß Anna Berger');
    expect(fillPlaceholders('Hallo {Melder}', {})).toBe('Hallo zusammen');
  });

  it('erkennt Erwähnungen mit vollem Namen oder Vornamen, nicht als Wortanfang', () => {
    const agents = [{ id: 'a', displayName: 'Anna Berger' }, { id: 'b', displayName: 'Ben Krüger' }, { id: 'c', displayName: 'Annabell Roth' }];
    expect(mentionedAgents('@Anna Berger bitte Drucker prüfen', agents)).toEqual(['a']);
    expect(mentionedAgents('Rückfrage an @ben und @Annabell', agents).sort()).toEqual(['b', 'c']);
    expect(mentionedAgents('Mail an anna@firma.de', agents)).toEqual([]);
  });
});

describe('Helpdesk: Kennzahlen und Kontext', () => {
  const stats: HelpdeskStats = {
    open: 12, unassigned: 3, overdue: 2, waiting: 4, mine: 5, blocked: 1, createdToday: 2, createdThisWeek: 9, resolvedThisWeek: 7,
    avgFirstResponseMinutes: 95, avgResolutionHours: 20.5, slaMetPercent: 87.5, satisfactionAverage: 4.4, ratings: 11,
    byCategory: [], byAgent: [], trend: [],
  };

  it('zeigt Kacheln mit Ampelfarben und Sprungzielen', () => {
    const tiles = kpiTiles(stats);
    expect(tiles.map((t) => t.label)).toEqual(['Offen', 'Nicht zugewiesen', 'Überfällig', 'Rückfrage', 'Mir zugewiesen', 'Gelöst diese Woche',
      'Ø erste Antwort', 'Ø Lösungszeit', 'Zufriedenheit']);
    expect(tiles[2]).toMatchObject({ key: 'overdue', value: '2', color: 'Error' });
    expect(tiles[6]).toMatchObject({ value: '1,6', unit: 'h', footer: '87,5 % im Ziel gelöst' });
    expect(tiles[7]).toMatchObject({ value: '20,5', unit: 'h' });
    expect(tiles[8]).toMatchObject({ value: '4,4', unit: 'von 5' });
    // Server rechnet in Geschäftszeit: Hinweis in der Kachel.
    const business = kpiTiles({ ...stats, timeBasis: 'business' });
    expect(business[6].footer).toBe('Geschäftszeit · 87,5 % im Ziel gelöst');
    expect(business[7].footer).toBe('Geschäftszeit ohne Rückfrage');
    expect(kpiTiles({ ...stats, overdue: 0, satisfactionAverage: null, ratings: 0 })[2].color).toBe('Good');
  });

  it('fasst Geräte und Prüfpfad zusammen', () => {
    expect(deviceLine({
      id: '1', name: 'Fahrzeug 3', platform: 'ios', kind: 'personal', status: 'active', model: 'iPhone 15', osVersion: '18.1', appVersion: '1.0.12',
      updateState: 'recommended', latestVersion: '1.0.13', lastSeenAt: null, isTicketDevice: true,
    })).toBe('iPhone 15 · iOS 18.1');
    const rows = auditRows([
      { id: '1', at: '2026-11-10T08:00:00Z', kind: 'created', actorUserId: null, actorName: 'Lukas', summary: '', text: 'Drucker', fromValue: null,
        toValue: null, isInternal: false, isReporter: true, attachmentFileIds: [] },
      { id: '2', at: '2026-11-10T09:00:00Z', kind: 'group', actorUserId: null, actorName: null, summary: 'Gruppe: keine → IT', text: null,
        fromValue: null, toValue: 'x', isInternal: true, isReporter: false, attachmentFileIds: [] },
    ]);
    expect(rows[0]).toMatchObject({ who: 'System', what: 'Gruppe', detail: 'Gruppe: keine → IT', internal: true });
    expect(rows[1]).toMatchObject({ what: 'Gemeldet', detail: 'Drucker' });
  });
});

describe('Helpdesk: Einstellungen', () => {
  it('liest Feiertage in deutscher und ISO-Schreibweise', () => {
    expect(parseHolidays('24.12.2026 Heiligabend\n2026-12-31\n31.12.2026\n30.02.2026\nirgendwann')).toEqual({
      dates: ['2026-12-24', '2026-12-31'], invalid: ['30.02.2026', 'irgendwann'],
    });
    expect(formatHolidays(['2026-12-24', '2027-01-01'])).toBe('24.12.2026\n01.01.2027');
  });

  it('beschreibt Zuweisungsregeln in einem Satz', () => {
    const names = { categories: { officeIt: 'Drucker/Büro-IT' }, subcategories: new Map([['s', 'Drucker']]), groups: new Map([['g', 'IT']]),
      people: new Map([['p', 'Anna Berger']]) };
    expect(describeRule({ id: '1', name: 'Drucker', category: 'officeIt', subcategoryId: 's', keyword: null, groupId: 'g', strategy: 'leastBusy',
      assigneeUserId: null, priority: 'high', isActive: true, sortOrder: 1, version: 1 }, names)).toEqual({
      when: 'Kategorie „Drucker/Büro-IT“ und Unterkategorie „Drucker“', then: 'Gruppe IT, an das Mitglied mit den wenigsten offenen Tickets, Priorität hoch' });
    expect(describeRule({ id: '2', name: 'VPN', category: null, subcategoryId: null, keyword: 'vpn', groupId: null, strategy: 'fixed',
      assigneeUserId: 'p', priority: null, isActive: true, sortOrder: 2, version: 1 }, names)).toEqual({ when: 'Text enthält „vpn“', then: 'an Anna Berger' });
  });
});

describe('Helpdesk: Wissensdatenbank', () => {
  it('formatiert Artikel als sicheres HTML', () => {
    expect(articleHtml('# Drucker\nSo geht es:\n1. Klappe öffnen\n2. Papier **vorsichtig** entfernen\n\n- Hinweis <script>x</script>'))
      .toBe('<h4>Drucker</h4><p>So geht es:</p><ol><li>Klappe öffnen</li><li>Papier <strong>vorsichtig</strong> entfernen</li></ol>'
        + '<ul><li>Hinweis &lt;script&gt;x&lt;/script&gt;</li></ul>');
  });
});

describe('Erwähnen mit @ beim Tippen', () => {
  const agents = [
    { id: 'a', displayName: 'Anna Becker', openTickets: 3 }, { id: 'b', displayName: 'Ben Albrecht', openTickets: 1 },
    { id: 'c', displayName: 'Carla Anders-Wolf', openTickets: 0 },
  ];

  it('erkennt den angefangenen Namen an der Schreibmarke', () => {
    expect(mentionQuery('Bitte @an', 9)).toEqual({ start: 6, query: 'an' });
    expect(mentionQuery('@', 1)).toEqual({ start: 0, query: '' });
    expect(mentionQuery('Hallo\n@Ben A', 12)).toEqual({ start: 6, query: 'Ben A' });
    // E-Mail-Adresse, Zeilenumbruch, fertiger Name mit Leerzeichen dahinter: keine Auswahl.
    expect(mentionQuery('it@firma.de', 11)).toBeNull();
    expect(mentionQuery('@Anna\nweiter', 12)).toBeNull();
    expect(mentionQuery('@Anna Becker hat', 16)).toBeNull();
    expect(mentionQuery('ohne at', 7)).toBeNull();
  });

  it('schlägt passende Bearbeiter vor, Namensanfang zuerst', () => {
    expect(mentionCandidates('an', agents).map((a) => a.id)).toEqual(['a', 'c']);
    expect(mentionCandidates('alb', agents).map((a) => a.id)).toEqual(['b']);
    expect(mentionCandidates('wolf', agents).map((a) => a.id)).toEqual(['c']);
    expect(mentionCandidates('', agents, 2)).toHaveLength(2);
    expect(mentionCandidates('xyz', agents)).toEqual([]);
  });

  it('ersetzt den angefangenen Namen und setzt die Schreibmarke dahinter', () => {
    expect(insertMention('Bitte @an prüfen', 6, 9, 'Anna Becker')).toEqual({ text: 'Bitte @Anna Becker prüfen', caret: 19 });
    expect(insertMention('@be', 0, 3, 'Ben Albrecht')).toEqual({ text: '@Ben Albrecht ', caret: 14 });
    const inserted = insertMention('Bitte @an', 6, 9, 'Anna Becker');
    expect(mentionedAgents(inserted.text, agents)).toEqual(['a']);
  });
});

describe('Arbeitsliste seitenweise und Eingangskanal', () => {
  it('zählt geladene und alle Tickets', () => {
    expect(pageText(100, 742)).toBe('100 von 742 Tickets');
    expect(pageText(12, 12, 2)).toBe('12 Tickets · 2 mit neuer Antwort');
    expect(pageText(1, 1)).toBe('1 Ticket');
    expect(pageText(1000, 1250)).toBe('1.000 von 1.250 Tickets');
  });

  it('benennt die Eingangskanäle', () => {
    expect(ticketChannels.phone.text).toBe('Telefon');
    expect(ticketChannels.inPerson.text).toBe('Vor Ort');
    expect(Object.keys(ticketChannels)).toEqual(['selfService', 'phone', 'email', 'inPerson']);
  });
});
