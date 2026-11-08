import { describe, expect, it } from 'vitest';
import {
  areaForPath, browserName, collectContext, contextRows, createRequest, detailRows, emptyDraft, filterFeedback, flowProgress, missingFields,
  normalizeNumber, platformName, sortFeedback, summaryRows, type FeedbackDetail, type FeedbackSummary,
} from '../webapp/util/feedback';
import { adminAreas } from '../webapp/util/adminCatalog';
import { routes } from '../webapp/util/routes';

const chromeWindows = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36';
const edge = `${chromeWindows} Edg/140.0.3485.54`;
const safariIpad = 'Mozilla/5.0 (iPad; CPU OS 18_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.1 Mobile/15E148 Safari/604.1';
const firefoxMac = 'Mozilla/5.0 (Macintosh; Intel Mac OS X 14.6; rv:132.0) Gecko/20100101 Firefox/132.0';

describe('Meldungen: Bereich und technische Angaben', () => {
  it('ordnet die aktuelle Seite einem Bereich zu (längster Pfadanfang)', () => {
    expect(areaForPath('/bauakten/0190-abc?abschnitt=dokumente')).toBe('Bauakten');
    expect(areaForPath('/planung/cockpit')).toBe('Planung');
    expect(areaForPath('/bestellungen')).toBe('Material und Bestellungen');
    expect(areaForPath('/it-center/helpdesk/1')).toBe('IT Center und Helpdesk');
    expect(areaForPath('/bauaktenliste')).toBe('Sonstiges');
    expect(areaForPath('')).toBe('Sonstiges');
  });

  it('erkennt Browser und Betriebssystem', () => {
    expect(browserName(chromeWindows)).toBe('Chrome 140');
    expect(browserName(edge)).toBe('Edge 140');
    expect(browserName(safariIpad)).toBe('Safari 18');
    expect(browserName(firefoxMac)).toBe('Firefox 132');
    expect(browserName('curl/8')).toBe('Unbekannter Browser');
    expect(platformName(edge)).toBe('Windows');
    expect(platformName(safariIpad)).toBe('iOS/iPadOS');
    expect(platformName(firefoxMac)).toBe('macOS');
  });

  it('stellt Seite, Browser, Bildschirm und Version zusammen', () => {
    const context = collectContext({
      path: '/planung?tag=2026-11-08', title: 'Kolonnenplanung – EstrichONE', userAgent: edge, screenWidth: 1920, screenHeight: 1080,
      innerWidth: 1536, innerHeight: 730, devicePixelRatio: 1.25, language: 'de-DE', timeZone: 'Europe/Berlin', appVersion: '1.4.0',
    });
    expect(context).toMatchObject({ page: '/planung?tag=2026-11-08', pageTitle: 'Kolonnenplanung', browser: 'Edge 140', platform: 'Windows',
      screen: '1920 × 1080', viewport: '1536 × 730', pixelRatio: '125 %', appVersion: '1.4.0' });
    const rows = contextRows(context);
    expect(rows.map((r) => r.label)).toEqual(['Seite', 'Browser', 'Bildschirm', 'Fenster', 'EstrichONE-Version', 'Sprache / Zeitzone']);
    expect(rows[2]!.value).toBe('1920 × 1080 (Skalierung 125 %)');
    expect(contextRows(context, true).at(-1)).toEqual({ label: 'User-Agent', value: edge });
    expect(contextRows(null)).toEqual([]);
  });
});

describe('Meldungen: Pflichtfelder, Zusammenfassung, Anfrage', () => {
  it('prüft Pflichtfelder je Art', () => {
    const bug = { ...emptyDraft('bug', 'Bauakten'), title: 'Upload hängt' };
    expect(missingFields(bug).map((m) => m.key)).toEqual(['actualResult', 'frequency', 'impact']);
    expect(missingFields({ ...bug, actualResult: '  ', frequency: 'always', impact: 'high' }).map((m) => m.key)).toEqual(['actualResult']);
    expect(missingFields({ ...bug, actualResult: 'Ladeanzeige', frequency: 'always', impact: 'high' })).toEqual([]);
    const feature = emptyDraft('feature');
    expect(missingFields(feature).map((m) => m.label)).toEqual(['Kurze Zusammenfassung', 'Bereich', 'Problem bzw. Nutzen', 'Gewünschte Lösung', 'Wichtigkeit']);
  });

  it('fasst nur ausgefüllte Angaben zusammen und nennt Anhänge', () => {
    const rows = summaryRows({ ...emptyDraft('feature', 'Bauakten'), title: 'Excel-Export', problem: 'Abtippen', desiredSolution: 'Knopf', impact: 'medium' },
      ['liste.png']);
    expect(rows).toEqual([
      { label: 'Art', value: 'Funktionswunsch' }, { label: 'Zusammenfassung', value: 'Excel-Export' }, { label: 'Bereich', value: 'Bauakten' },
      { label: 'Problem bzw. Nutzen', value: 'Abtippen' }, { label: 'Gewünschte Lösung', value: 'Knopf' },
      { label: 'Wichtigkeit', value: 'Wichtig: spart regelmäßig Zeit' }, { label: 'Anhänge', value: 'liste.png' },
    ]);
  });

  it('sendet nur die Felder der gewählten Art', () => {
    const draft = { ...emptyDraft('feature', ' Planung '), title: ' Wunsch ', problem: 'p', desiredSolution: 'l', actualResult: 'bleibt weg', impact: 'low' as const };
    const body = createRequest('id-1', draft, collectContext({ path: '/', title: '', userAgent: '', screenWidth: 1, screenHeight: 1, innerWidth: 1,
      innerHeight: 1, devicePixelRatio: 1, language: '', timeZone: '', appVersion: null }), ['f1']);
    expect(body).toMatchObject({ id: 'id-1', kind: 'feature', title: 'Wunsch', area: 'Planung', actualResult: null, frequency: null, problem: 'p',
      desiredSolution: 'l', impact: 'low', attachmentFileIds: ['f1'], description: null });
  });
});

const item = (over: Partial<FeedbackSummary>): FeedbackSummary => ({
  id: 'x', number: 'F-2026-0001', kind: 'bug', title: 'Titel', area: 'Bauakten', status: 'new', priority: 'normal', impact: 'medium',
  reporterUserId: 'u', reporterName: 'Lukas Brandl', assigneeUserId: null, assigneeName: null, duplicateOfId: null, duplicateOfNumber: null,
  attachmentCount: 0, createdAt: '2026-11-01T08:00:00Z', lastActivityAt: '2026-11-01T08:00:00Z', closedAt: null, version: 1, ...over,
});

describe('Meldungen: Listen', () => {
  const items = [
    item({ id: 'a', number: 'F-2026-0001', title: 'Upload hängt', status: 'inReview', priority: 'high', lastActivityAt: '2026-11-03T08:00:00Z' }),
    item({ id: 'b', number: 'F-2026-0002', kind: 'feature', title: 'Excel-Export', area: 'Planung', status: 'done', lastActivityAt: '2026-11-04T08:00:00Z' }),
    item({ id: 'c', number: 'F-2026-0010', title: 'Straße falsch', status: 'duplicate', priority: 'urgent', lastActivityAt: '2026-11-02T08:00:00Z' }),
  ];

  it('filtert nach offen/abgeschlossen, Art, Bereich und Suche (ohne Umlaute zu unterscheiden)', () => {
    expect(filterFeedback(items, { status: 'open' }).map((f) => f.id)).toEqual(['a']);
    expect(filterFeedback(items, { status: 'closed' }).map((f) => f.id)).toEqual(['b', 'c']);
    expect(filterFeedback(items, { status: 'done' }).map((f) => f.id)).toEqual(['b']);
    expect(filterFeedback(items, { kind: 'feature' }).map((f) => f.id)).toEqual(['b']);
    expect(filterFeedback(items, { area: 'Planung' }).map((f) => f.id)).toEqual(['b']);
    expect(filterFeedback(items, { query: 'strasse' }).map((f) => f.id)).toEqual(['c']);
    expect(filterFeedback(items, { query: '0002 excel' }).map((f) => f.id)).toEqual(['b']);
    expect(filterFeedback(items, { query: 'brandl' })).toHaveLength(3);
  });

  it('sortiert nach Aktivität, Priorität und Nummer', () => {
    expect(sortFeedback(items, 'recent').map((f) => f.id)).toEqual(['b', 'a', 'c']);
    expect(sortFeedback(items, 'priority').map((f) => f.id)).toEqual(['c', 'a', 'b']);
    expect(sortFeedback(items, 'number').map((f) => f.id)).toEqual(['c', 'b', 'a']);
  });

  it('zeigt den Fortschritt im Statusablauf und normalisiert Nummern', () => {
    expect(flowProgress('planned')).toEqual({ step: 3, total: 5, text: 'Schritt 3 von 5: Geplant' });
    expect(flowProgress('rejected').text).toBe('Abgelehnt');
    expect(normalizeNumber('f-2026-3')).toBe('F-2026-0003');
    expect(normalizeNumber(' F20260012 ')).toBe('F-2026-0012');
    expect(normalizeNumber('T-2026-0001')).toBeNull();
  });

  it('zeigt die Angaben passend zur Art', () => {
    const detail = { summary: item({ kind: 'bug', impact: 'high' }), description: null, stepsToReproduce: '1. Öffnen', expectedResult: null,
      actualResult: 'Fehler 500', frequency: 'sometimes', problem: 'nie sichtbar', desiredSolution: null, context: null, attachments: [], timeline: [] } as FeedbackDetail;
    expect(detailRows(detail)).toEqual([
      { label: 'Schritte zum Nachstellen', value: '1. Öffnen' }, { label: 'Tatsächlich passiert', value: 'Fehler 500' },
      { label: 'Häufigkeit', value: 'Manchmal' }, { label: 'Auswirkung', value: 'Hoch: Arbeiten nicht möglich' },
    ]);
  });
});

describe('Meldungen: Einstieg und Rechte', () => {
  it('„Meine Meldungen“ für alle, IT-Verwaltung nur mit feedback.manage', () => {
    expect(routes.find((r) => r.pattern === '/meine-meldungen')?.permission).toBeUndefined();
    expect(routes.find((r) => r.pattern === '/meine-meldungen/:id')?.view).toBe('Feedback');
    expect(routes.filter((r) => r.pattern.startsWith('/it-center/meldungen')).map((r) => r.permission)).toEqual(['feedback.manage', 'feedback.manage']);
    expect(adminAreas.find((a) => a.path === '/it-center/meldungen')).toMatchObject({ permission: 'feedback.manage', group: 'groupAdminUsers' });
  });
});
