import { describe, expect, it } from 'vitest';
import { uuidv7 } from './ids';

describe('uuidv7', () => {
  it('hat Version 7, Variante 10 und den Zeitstempel vorne', () => {
    const id = uuidv7(Date.parse('2026-10-20T08:00:00Z'));
    expect(id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
    expect(parseInt(id.replace(/-/g, '').slice(0, 12), 16)).toBe(Date.parse('2026-10-20T08:00:00Z'));
  });

  it('ist zeitlich sortierbar', () => {
    expect(uuidv7(1000) < uuidv7(2000)).toBe(true);
  });
});
