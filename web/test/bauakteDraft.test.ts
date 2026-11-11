import { describe, expect, it } from 'vitest';
import { appointmentSpan, partyBody, splitSchema } from '../webapp/util/bauakteDraft';
import type { FormSchema, SchemaField } from '../webapp/util/formSchema';

const field = (key: string, required = false): SchemaField => ({ key, label: key, type: 'text', path: `project.${key}`, required });

describe('Anlage-Assistent: Formular-Schema aufteilen', () => {
  const schema: FormSchema = {
    id: 's', key: 'bauakte', version: 1, lists: {},
    groups: [
      { key: 'bauvorhaben', label: 'Bauvorhaben', fields: [field('objectType'), field('floors'), field('totalArea'), field('plannedStart'), field('plannedEnd'), field('priority')] },
      { key: 'estrich', label: 'Estrich', fields: [field('screedType'), field('construction'), field('nominalThicknessMm'), field('strengthClass')] },
      { key: 'eigene', label: 'Eigene Felder', fields: [field('bauherrNummer', true), field('farbe')] },
    ],
  };

  it('Eckdaten mit Pflichtfeldern, Ausführung getrennt, übrige Felder je Gruppe', () => {
    const { primary, schedule, more } = splitSchema(schema);
    expect(primary.groups.map((g) => g.fields.map((f) => f.key))).toEqual([['objectType', 'floors', 'totalArea', 'screedType', 'nominalThicknessMm', 'bauherrNummer']]);
    expect(schedule.groups.map((g) => g.fields.map((f) => f.key))).toEqual([['plannedStart', 'plannedEnd', 'priority']]);
    expect(more.groups.map((g) => [g.label, g.fields.map((f) => f.key)])).toEqual([['Estrich', ['construction', 'strengthClass']], ['Eigene Felder', ['farbe']]]);
  });

  it('leere Bereiche entfallen', () => {
    const { schedule, more } = splitSchema({ ...schema, groups: [{ key: 'x', label: 'X', fields: [field('objectType')] }] });
    expect(schedule.groups).toEqual([]);
    expect(more.groups).toEqual([]);
  });
});

describe('Anlage-Assistent: Beteiligte und Termin', () => {
  it('Beteiligter nur mit Name oder Firma; ohne Namen gilt die Firma', () => {
    expect(partyBody('siteManager', { name: ' ', company: '', phone: '0871 1', email: '' })).toBeNull();
    expect(partyBody('architect', { name: '', company: 'Architekturbüro Lehner', phone: ' 0871 12345 ', email: '' })).toEqual({
      role: 'architect', contactId: null, name: 'Architekturbüro Lehner', company: 'Architekturbüro Lehner', phone: '0871 12345', email: null, note: null, version: null,
    });
    expect(partyBody('siteManager', { name: 'Thomas Berger', company: 'Wohnbau Süd GmbH', phone: '', email: 't.berger@example.de' })?.name).toBe('Thomas Berger');
  });

  it('Termin aus Datum, Uhrzeit und Dauer (lokale Zeit, auch über Mitternacht)', () => {
    expect(appointmentSpan('2026-11-12', '09:00', 60)).toEqual({ start: '2026-11-12T09:00:00', end: '2026-11-12T10:00:00' });
    expect(appointmentSpan('2026-11-12', '23:30', 90)).toEqual({ start: '2026-11-12T23:30:00', end: '2026-11-13T01:00:00' });
    expect(appointmentSpan('', '09:00', 60)).toBeNull();
    expect(appointmentSpan('2026-11-12', '', 60)).toBeNull();
  });
});
