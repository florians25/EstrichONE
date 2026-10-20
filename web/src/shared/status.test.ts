import { describe, expect, it } from 'vitest';
import { waitingSince } from './status';

describe('waitingSince', () => {
  const now = Date.parse('2026-10-20T12:00:00Z');

  it('zeigt Minuten, Stunden und Tage', () => {
    expect(waitingSince('2026-10-20T11:45:00Z', now)).toBe('15 Min.');
    expect(waitingSince('2026-10-20T09:00:00Z', now)).toBe('3 Std.');
    expect(waitingSince('2026-10-16T12:00:00Z', now)).toBe('4 Tage');
  });

  it('wird nie negativ', () => {
    expect(waitingSince('2026-10-20T12:05:00Z', now)).toBe('0 Min.');
  });
});
