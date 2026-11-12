import { describe, expect, it } from 'vitest';
import {
  confirmMatches, customerState, describeReferences, removalActions, removalMessage, summarizeBulk,
} from '../webapp/util/customerRemoval';
import { candidateLine } from '../webapp/util/customerMatch';
import type { ContactResponse } from '../webapp/util/models';

const all = { delete: true, edit: true, anonymize: true };

describe('Kunden löschen oder archivieren', () => {
  it('beschreibt die Bezüge mit Anzahl je Art', () => {
    expect(describeReferences([])).toBe('');
    expect(describeReferences([{ count: 1, label: 'Bauakte' }])).toBe('1 Bauakte');
    expect(describeReferences([{ count: 3, label: 'Bauakten' }, { count: 1, label: 'Projektbeteiligung' }, { count: 2, label: 'Anrufe und Anfragen' }]))
      .toBe('3 Bauakten, 1 Projektbeteiligung und 2 Anrufe und Anfragen');
    expect(describeReferences([{ count: 0, label: 'Bauakten' }, { count: 2, label: 'Anrufe und Anfragen' }])).toBe('2 Anrufe und Anfragen');
  });

  it('verlangt die eingetippte Kundennummer (ohne Groß-/Kleinschreibung und Rand)', () => {
    expect(confirmMatches('600123', '600123')).toBe(true);
    expect(confirmMatches(' 600123 ', '600123')).toBe(true);
    expect(confirmMatches('60012', '600123')).toBe(false);
    expect(confirmMatches('', '')).toBe(false);
    expect(confirmMatches('familie huber', 'Familie Huber')).toBe(true);
    expect(confirmMatches(undefined, 'x')).toBe(false);
  });

  it('bietet Löschen nur ohne Bezüge und mit Recht an, sonst Archivieren', () => {
    expect(removalActions({ deletable: true, archived: false, anonymized: false }, all)).toEqual(['delete', 'archive']);
    expect(removalActions({ deletable: false, archived: false, anonymized: false }, all)).toEqual(['archive', 'anonymize']);
    expect(removalActions({ deletable: false, archived: false, anonymized: false }, { ...all, anonymize: false })).toEqual(['archive']);
    expect(removalActions({ deletable: true, archived: false, anonymized: false }, { delete: false, edit: true, anonymize: false })).toEqual(['archive']);
    expect(removalActions({ deletable: false, archived: true, anonymized: false }, all)).toEqual(['reactivate', 'anonymize']);
    expect(removalActions({ deletable: false, archived: true, anonymized: true }, all)).toEqual([]);
    expect(removalActions({ deletable: true, archived: false, anonymized: false }, { delete: true, edit: false, anonymize: false })).toEqual(['delete']);
  });

  it('erklärt, warum nicht gelöscht wird und was beim Löschen wegfällt', () => {
    const blocked = removalMessage({ deletable: false, name: 'Familie Huber', customerNumber: '600007',
      references: [{ kind: 'bauakten', label: 'Bauakten', count: 2, link: null, items: [] }] });
    expect(blocked).toContain('Familie Huber (Kd.-Nr. 600007) kann nicht gelöscht werden');
    expect(blocked).toContain('2 Bauakten');
    expect(blocked).toContain('archiviert');
    const free = removalMessage({ deletable: true, name: 'Eva Lang', customerNumber: '600008', references: [] });
    expect(free).toContain('Ansprechpartner endgültig entfernt');
    expect(free).toContain('600008 wird nicht erneut vergeben');
    expect(removalMessage({ deletable: true, name: 'Ohne Nummer', customerNumber: null, references: [] })).not.toContain('Kundennummer');
  });

  it('fasst die Mehrfachauswahl zusammen', () => {
    expect(summarizeBulk([])).toBe('Nichts geändert');
    expect(summarizeBulk([{ name: 'a', action: 'deleted' }, { name: 'b', action: 'deleted' }, { name: 'c', action: 'archived' }, { name: 'd', action: 'failed' }]))
      .toBe('2 gelöscht, 1 archiviert, 1 fehlgeschlagen');
  });

  it('kennzeichnet archivierte und anonymisierte Kunden', () => {
    expect(customerState({})).toEqual({ text: '', state: 'None' });
    expect(customerState({ archivedAt: '2026-11-01T10:00:00Z' }).text).toBe('Archiviert');
    expect(customerState({ archivedAt: '2026-11-01T10:00:00Z', anonymizedAt: '2026-11-02T10:00:00Z' }).text).toBe('Anonymisiert');
    const archived = { id: '1', name: 'Alt', customerNumber: '600001', archivedAt: '2026-11-01T10:00:00Z' } as ContactResponse;
    expect(candidateLine(archived)).toMatch(/^archiviert · Kd\.-Nr\. 600001/);
  });
});
