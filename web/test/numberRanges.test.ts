import { describe, expect, it } from 'vitest';
import { capacity, formatNumber, isYearly, patternError, rangeError, rangeText, remainingLevel, remainingText } from '../webapp/util/numberRanges';

describe('Nummernkreise', () => {
  it('formatiert Muster mit Jahr und Mindeststellen wie der Server', () => {
    expect(formatNumber('60{NNNN}', 1)).toBe('600001');
    expect(formatNumber('{JJJJ}-{NNN}', 42, 2026)).toBe('2026-042');
    expect(formatNumber('BA-{JJ}/{NNNN}', 7, 2026)).toBe('BA-26/0007');
    expect(formatNumber('{JJJJ}-{NNN}', 1234, 2026)).toBe('2026-1234');
    expect(isYearly('A-{JJJJ}-{NNNN}')).toBe(true);
    expect(isYearly('70{NNNN}')).toBe(false);
    expect(capacity('K{NN}')).toBe(99);
  });

  it('prüft Muster: genau eine laufende Nummer, erlaubte Zeichen, Länge', () => {
    expect(patternError('60{NNNN}')).toBeNull();
    expect(patternError('LF{JJ}-{NNN}')).toBeNull();
    expect(patternError('')).toMatch(/Muster angeben/);
    expect(patternError('K-')).toMatch(/genau eine laufende Nummer/);
    expect(patternError('{NN}{NN}')).toMatch(/genau eine laufende Nummer/);
    expect(patternError('K<{NN}>')).toMatch(/Erlaubt sind/);
    expect(patternError('{NNNNNNNNNN}')).toMatch(/höchstens 9 Stellen/);
    expect(patternError('X'.repeat(20) + '{NN}', 18)).toMatch(/höchstens 18 Zeichen/);
  });

  it('prüft Bereich und nächste Nummer', () => {
    expect(rangeError({ pattern: 'K{NN}', rangeStart: 1, rangeEnd: null, nextNumber: null })).toBeNull();
    expect(rangeError({ pattern: 'K{NN}', rangeStart: 1, rangeEnd: 500, nextNumber: null })).toMatch(/Stellen/);
    expect(rangeError({ pattern: 'K{NN}', rangeStart: 10, rangeEnd: 5, nextNumber: null })).toMatch(/Ende/);
    expect(rangeError({ pattern: 'K{NN}', rangeStart: 10, rangeEnd: 20, nextNumber: 30 })).toMatch(/im Bereich/);
    expect(rangeError({ pattern: '{JJJJ}-{NNN}', rangeStart: 1, rangeEnd: 5000, nextNumber: 1200 })).toBeNull();
  });

  it('zeigt Verbleibend mit Warnung und Fehler', () => {
    expect(remainingLevel({ remaining: 9999, rangeStart: 1, rangeEnd: 9999 })).toBe('ok');
    expect(remainingLevel({ remaining: 80, rangeStart: 1, rangeEnd: 9999 })).toBe('warning');
    expect(remainingLevel({ remaining: 0, rangeStart: 1, rangeEnd: 9999 })).toBe('error');
    expect(remainingLevel({ remaining: null, rangeStart: 1, rangeEnd: null })).toBe('ok');
    expect(remainingLevel({ remaining: 9, rangeStart: 1, rangeEnd: 99 })).toBe('warning');
    expect(remainingText({ remaining: null, yearly: true })).toBe('unbegrenzt (je Jahr neu)');
    expect(remainingText({ remaining: 0, yearly: false })).toBe('ausgeschöpft');
    expect(remainingText({ remaining: 1234, yearly: false })).toBe('1.234 frei');
    expect(rangeText({ rangeStart: 1, rangeEnd: 9999, yearly: false })).toBe('1 bis 9.999');
    expect(rangeText({ rangeStart: 1, rangeEnd: null, yearly: true })).toBe('1 bis ∞ je Jahr');
  });
});
