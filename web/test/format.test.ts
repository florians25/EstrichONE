import { describe, expect, it } from 'vitest';
import { area, cmNumber, fileSize, initials, parseNumber, timeRange, waitingSince, weatherLine } from '../webapp/util/format';
import { addDays, isoWeek, mondayOf } from '../webapp/util/days';
import { uuidv7 } from '../webapp/util/ids';
import { describeTicketEvent, inspectionResult } from '../webapp/util/labels';

describe('Anzeigeformate', () => {
  it('Dateigrößen, Flächen, CM-Werte', () => {
    expect(fileSize(200)).toBe('1 KB');
    expect(fileSize(2.5 * 1024 * 1024)).toBe('2,5 MB');
    expect(area(42.5)).toBe('42,50 m²');
    expect(cmNumber(1.6)).toBe('1,6');
    expect(cmNumber(null)).toBe('–');
  });

  it('Wetterzeile und Arbeitszeit im Bautagebuch', () => {
    expect(weatherLine({ weather: 'rain', temperatureCelsius: 9, workersPresent: null })).toBe('Regen · 9 °C');
    expect(weatherLine({ weather: null, temperatureCelsius: null, workersPresent: 3 })).toBe('3 anwesend');
    expect(timeRange('07:00:00', '15:30:00')).toBe('07:00–15:30');
    expect(timeRange(null, null)).toBe('');
  });

  it('Zahlen in deutscher Schreibweise', () => {
    expect(parseNumber('42,5')).toBe(42.5);
    expect(parseNumber('1.234,5')).toBe(1234.5);
    expect(parseNumber('3.5')).toBe(3.5);
    expect(parseNumber('')).toBeNull();
    expect(parseNumber('abc')).toBeNaN();
  });

  it('Wartezeit und Initialen', () => {
    const now = Date.parse('2026-10-20T12:00:00Z');
    expect(waitingSince('2026-10-20T11:45:00Z', now)).toBe('15 Min.');
    expect(waitingSince('2026-10-20T09:00:00Z', now)).toBe('3 Std.');
    expect(waitingSince('2026-10-16T12:00:00Z', now)).toBe('4 Tage');
    expect(waitingSince('2026-10-20T12:05:00Z', now)).toBe('0 Min.');
    expect(initials('Anna Admin')).toBe('AA');
  });

  it('Ticket-Ereignisse und Erstbegehung', () => {
    expect(describeTicketEvent('status', 'InProgress')).toBe('hat den Status geändert: In Bearbeitung');
    expect(describeTicketEvent('priority', 'Urgent')).toBe('hat die Priorität geändert: Dringend');
    expect(inspectionResult({ notRequired: false, checks: { substrate: 'ok', edgeInsulation: 'notOk' } }).text).toBe('Mit Mängeln – Klärung nötig');
    expect(inspectionResult({ notRequired: true, checks: {} }).text).toBe('Nicht notwendig');
  });
});

describe('Kalendertage', () => {
  it('Montag, Monats- und Jahresgrenzen, ISO-Woche', () => {
    expect(mondayOf('2026-11-08')).toBe('2026-11-02');
    expect(addDays('2026-12-30', 3)).toBe('2027-01-02');
    expect(addDays('2026-03-01', -1)).toBe('2026-02-28');
    expect(isoWeek('2026-11-02')).toBe(45);
    expect(isoWeek('2027-01-01')).toBe(53);
  });
});

describe('uuidv7', () => {
  it('Version 7, Zeitstempel vorne, sortierbar', () => {
    const id = uuidv7(Date.parse('2026-10-20T08:00:00Z'));
    expect(id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
    expect(uuidv7(1000) < uuidv7(2000)).toBe(true);
  });
});
