import { describe, expect, it } from 'vitest';
import { addDays, isoWeek, mondayOf } from './days';

describe('Kalendertage', () => {
  it('findet den Montag der Woche', () => {
    expect(mondayOf('2026-11-05')).toBe('2026-11-02');
    expect(mondayOf('2026-11-08')).toBe('2026-11-02');
    expect(mondayOf('2026-11-02')).toBe('2026-11-02');
  });

  it('rechnet über Monats- und Jahresgrenzen', () => {
    expect(addDays('2026-12-30', 3)).toBe('2027-01-02');
    expect(addDays('2026-03-01', -1)).toBe('2026-02-28');
  });

  it('kennt die ISO-Kalenderwoche', () => {
    expect(isoWeek('2026-11-02')).toBe(45);
    expect(isoWeek('2027-01-01')).toBe(53);
    expect(isoWeek('2026-01-01')).toBe(1);
  });
});
