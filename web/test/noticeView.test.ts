import { describe, expect, it } from 'vitest';
import {
  announcementCard, bannerSelection, buildCards, contentHash, duration, formatPeriod, maintenanceCard, maintenanceKind, moreText,
  relativeLabel, remainingLabel,
} from '../webapp/util/noticeView';

// Ortszeit (unabhängig von der Zeitzone des Testrechners): Freitag, 06.11.2026, 12:00 Uhr.
const now = new Date(2026, 10, 6, 12, 0);
const at = (day: number, h: number, m = 0, month = 10, year = 2026) => new Date(year, month, day, h, m).toISOString();
const plus = (minutes: number) => new Date(now.getTime() + minutes * 60_000).toISOString();

describe('Zeitraum der Systemhinweise', () => {
  it('gleicher Tag, über Mitternacht, nur Beginn bzw. Ende, anderes Jahr', () => {
    expect(formatPeriod(at(8, 22), at(8, 23, 30), now)).toBe('So., 08.11., 22:00–23:30 Uhr');
    expect(formatPeriod(at(7, 22), at(8, 2), now)).toBe('Sa., 07.11., 22:00 Uhr – So., 08.11., 02:00 Uhr');
    expect(formatPeriod(at(7, 6, 5), null, now)).toBe('ab Sa., 07.11., 06:05 Uhr');
    expect(formatPeriod(null, at(13, 18), now)).toBe('bis Fr., 13.11., 18:00 Uhr');
    expect(formatPeriod(null, null, now)).toBe('');
    expect(formatPeriod(at(2, 8, 0, 0, 2027), at(2, 9, 0, 0, 2027), now)).toBe('Sa., 02.01.2027, 08:00–09:00 Uhr');
  });

  it('Dauer in Worten (Dativ für „in/seit“, Akkusativ für „noch“)', () => {
    expect(duration(10 * 60_000)).toBe('10 Min.');
    expect(duration(20_000)).toBe('1 Min.');
    expect(duration(90 * 60_000)).toBe('1 Std. 30 Min.');
    expect(duration(5 * 3_600_000)).toBe('5 Std.');
    expect(duration(86_400_000)).toBe('1 Tag');
    expect(duration(3 * 86_400_000)).toBe('3 Tagen');
    expect(duration(3 * 86_400_000, 'acc')).toBe('3 Tage');
  });

  it('relative Angabe vor, während und nach dem Zeitraum', () => {
    expect(relativeLabel(plus(45), plus(90), now)).toBe('in 45 Min.');
    expect(relativeLabel(plus(0.5), null, now)).toBe('beginnt gleich');
    expect(relativeLabel(at(6, 17), null, now)).toBe('heute, in 5 Std.');
    expect(relativeLabel(at(7, 9), null, now)).toBe('morgen');
    expect(relativeLabel(at(8, 22), at(8, 23, 30), now)).toBe('in 2 Tagen');
    expect(relativeLabel(plus(-10), plus(20), now)).toBe('läuft seit 10 Min.');
    expect(relativeLabel(plus(-150), null, now)).toBe('läuft seit 2 Std. 30 Min.');
    expect(relativeLabel(plus(-60), plus(-1), now)).toBe('beendet');
    expect(relativeLabel(null, null, now)).toBe('');
    expect(remainingLabel(plus(20), now)).toBe('noch 20 Min.');
    expect(remainingLabel(plus(60 * 24 * 3), now)).toBe('noch 3 Tage');
    expect(remainingLabel(plus(-5), now)).toBe('');
  });
});

describe('Art und Darstellung', () => {
  it('Wartung: geplant = Information, binnen einer Stunde = Warnung, läuft = Warnung und nicht ausblendbar', () => {
    const planned = maintenanceCard({ startsAt: at(8, 22), endsAt: at(8, 23, 30), message: 'Update' }, now);
    expect(planned).toMatchObject({ kind: 'maintenance', tone: 'Information', title: 'Geplante Wartung', relative: 'in 2 Tagen',
      period: 'So., 08.11., 22:00–23:30 Uhr', text: 'Update', dismissible: true });
    expect(maintenanceKind({ startsAt: plus(30) }, now)).toBe('maintenanceSoon');
    const running = maintenanceCard({ active: true, startsAt: plus(-10), endsAt: plus(20) }, now, true);
    expect(running).toMatchObject({ kind: 'maintenanceRunning', tone: 'Warning', dismissible: false, relative: 'läuft seit 10 Min.', remaining: 'noch 20 Min.' });
    expect(running.title).toContain('Wartungsmodus aktiv');
    // Früh ausgeblendet → eine Stunde vorher neuer Schlüssel (erinnert erneut).
    expect(maintenanceCard({ startsAt: plus(30) }, now).key).not.toBe(maintenanceCard({ startsAt: plus(30) }, new Date(now.getTime() - 3 * 3_600_000)).key);
  });

  it('Ankündigungen: Schwere → Farbe/Symbol, Störung nicht ausblendbar, geänderter Inhalt = neuer Schlüssel', () => {
    const info = announcementCard({ id: 'a', title: 'Neu', text: 'PDF-Druck', severity: 'info', endsAt: plus(60 * 24 * 2), source: 'instance' }, now);
    expect(info).toMatchObject({ tone: 'Information', icon: 'sap-icon://message-information', dismissible: true, remaining: 'noch 2 Tage', source: 'Verwaltung' });
    const critical = announcementCard({ id: 'b', title: 'Störung', text: 'Mail', severity: 'critical', source: 'console' }, now);
    expect(critical).toMatchObject({ kind: 'critical', tone: 'Error', dismissible: false, source: 'Betreiber' });
    expect(announcementCard({ id: 'c', title: 'x', text: '', severity: 'unbekannt' }, now).tone).toBe('Information');
    expect(announcementCard({ id: 'a', title: 'Neu', text: 'PDF-Druck!', severity: 'info' }, now).key)
      .not.toBe(announcementCard({ id: 'a', title: 'Neu', text: 'PDF-Druck', severity: 'info' }, now).key);
    expect(contentHash('abc')).toBe(contentHash('abc'));
  });

  it('Reihenfolge, Ausblenden und Zusammenfassen mehrerer Hinweise', () => {
    const cards = buildCards([
      { id: 'i', title: 'Info', text: '', severity: 'info' },
      { id: 'w', title: 'Warnung', text: '', severity: 'warning' },
      { id: 'c', title: 'Störung', text: '', severity: 'critical' },
    ], { startsAt: at(8, 22), endsAt: at(8, 23) }, now);
    expect(cards.map((c) => c.kind)).toEqual(['critical', 'warning', 'maintenance', 'info']);
    // Abgelaufene Wartung erscheint nicht.
    expect(buildCards([], { startsAt: plus(-60), endsAt: plus(-1) }, now)).toHaveLength(0);

    const collapsed = bannerSelection(cards, new Set(), false);
    expect(collapsed.shown.map((c) => c.kind)).toEqual(['critical']);
    expect(collapsed.hidden).toBe(3);
    expect(bannerSelection(cards, new Set(), true).shown).toHaveLength(4);

    // Ausgeblendet: Warnung weg; Störung lässt sich nicht ausblenden.
    const hidden = new Set([cards[0]!.key, cards[1]!.key]);
    const after = bannerSelection(cards, hidden, true);
    expect(after.shown.map((c) => c.kind)).toEqual(['critical', 'maintenance', 'info']);
    expect(bannerSelection([cards[3]!], new Set([cards[3]!.key]), false)).toEqual({ shown: [], hidden: 0 });
    expect(moreText(1)).toBe('1 weiterer Hinweis');
    expect(moreText(3)).toBe('3 weitere Hinweise');
  });
});
