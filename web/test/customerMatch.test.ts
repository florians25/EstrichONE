import { describe, expect, it } from 'vitest';
import {
  atSameAddress, candidateLine, duplicateQueries, filterByStatus, latest, mergeCandidates, normalize, phoneDigits, routeUrl, sameAddress,
  siteFromCustomer, validateCustomer, type DuplicateCandidate,
} from '../webapp/util/customerMatch';
import type { ContactResponse } from '../webapp/util/models';

const contact = (id: string, name: string, extra: Partial<ContactResponse> = {}) =>
  ({ id, name, kind: 'person', customerNumber: null, street: null, postalCode: null, city: null, phone: null, email: null, ...extra }) as ContactResponse;

describe('Dublettenabgleich bei der Kundenanlage', () => {
  it('fragt erst ab drei Zeichen oder mit Nummer, E-Mail oder vollständiger Anschrift', () => {
    expect(duplicateQueries({ name: 'Kr' })).toEqual([]);
    expect(duplicateQueries({ name: '', phones: ['0871'] })).toEqual([]);
    expect(duplicateQueries({ name: 'Kra' })).toHaveLength(1);
    expect(duplicateQueries({ name: '', street: 'Hauptstr. 3', postalCode: '84028' })).toHaveLength(1);
    expect(duplicateQueries({ name: '', emails: ['kein-mail'] })).toEqual([]);
  });

  it('bildet je Telefonnummer und E-Mail-Adresse eine Abfrage', () => {
    const queries = duplicateQueries({ name: ' Familie Kraus ', city: 'Landshut', phones: ['0871 123456', '0171 5550001', 'x'], emails: ['kraus@example.de'] });
    expect(queries).toEqual([
      { name: 'Familie Kraus', street: null, postalCode: null, city: 'Landshut', phone: '0871 123456', email: 'kraus@example.de' },
      { name: 'Familie Kraus', street: null, postalCode: null, city: 'Landshut', phone: '0171 5550001', email: null },
    ]);
  });

  it('führt Treffer zusammen: höchster Wert, alle Gründe, bester zuerst, eigener Kunde ausgenommen', () => {
    const kraus = contact('a', 'Familie Kraus');
    const huber = contact('b', 'Bau Huber GmbH');
    const lists: DuplicateCandidate[][] = [
      [{ contact: kraus, score: 60, reasons: ['gleicher Name'] }, { contact: huber, score: 45, reasons: ['gleiche Telefonnummer'] }],
      [{ contact: kraus, score: 95, reasons: ['gleicher Name', 'gleiche E-Mail-Adresse'] }],
    ];
    const merged = mergeCandidates(lists);
    expect(merged.map((d) => d.contact.id)).toEqual(['a', 'b']);
    expect(merged[0]).toMatchObject({ score: 95, reasons: ['gleicher Name', 'gleiche E-Mail-Adresse'] });
    expect(lists[0][0].reasons).toEqual(['gleicher Name']);
    expect(mergeCandidates(lists, 'a').map((d) => d.contact.id)).toEqual(['b']);
  });

  it('normalisiert Namen, Straßen und Telefonnummern wie der Server', () => {
    expect(normalize('Müller-Lüdenscheidt, Hauptstr. 3')).toBe('mueller luedenscheidt hauptstrasse 3');
    expect(normalize('Bahnhofstr 1')).toBe('bahnhofstrasse 1');
    expect(phoneDigits('+49 (871) 12 34 56')).toBe('0871123456');
    expect(phoneDigits('0049 871 123456')).toBe('0871123456');
    expect(phoneDigits('12345')).toBeNull();
  });

  it('zeigt Treffer mit Kundennummer, Anschrift und Erreichbarkeit', () => {
    expect(candidateLine(contact('a', 'X', { customerNumber: '10001', street: 'Am Hang 3', postalCode: '84028', city: 'Landshut', phone: '0871 1' })))
      .toBe('Kd.-Nr. 10001 · Am Hang 3, 84028 Landshut · 0871 1');
    expect(candidateLine(contact('a', 'X'))).toBe('Keine weiteren Angaben');
  });

  it('prüft Pflichtfelder, Postleitzahl, Telefon und E-Mail', () => {
    expect(validateCustomer({ kind: 'company', name: ' ' })).toEqual({ name: 'Bitte den Firmennamen angeben.' });
    const errors = validateCustomer({ kind: 'person', name: 'Kraus', postalCode: '840', phones: ['0871 123456', 'abc'], emails: ['a@b.de', 'falsch'] });
    expect(Object.keys(errors).sort()).toEqual(['emails', 'phones', 'postalCode']);
    expect(errors.phones).toContain('„abc“');
    expect(validateCustomer({ kind: 'person', name: 'Kraus', postalCode: '84028', phones: ['+49 871/12 34-56'], emails: ['a@b.de'] })).toEqual({});
  });
});

describe('Adressübernahme und Bauakten an derselben Adresse', () => {
  it('übernimmt die Kundenanschrift als Baustellenadresse', () => {
    expect(siteFromCustomer({ street: ' Am Hang 3 ', postalCode: '84028', city: 'Landshut' })).toEqual({ street: 'Am Hang 3', postalCode: '84028', city: 'Landshut' });
    expect(siteFromCustomer(null)).toEqual({ street: null, postalCode: null, city: null });
  });

  it('erkennt dieselbe Adresse trotz Schreibweise, nicht aber andere Hausnummern', () => {
    expect(sameAddress({ street: 'Hauptstraße 3', postalCode: '84028' }, { street: 'Hauptstr. 3', postalCode: '84028' })).toBe(true);
    expect(sameAddress({ street: 'Hauptstraße 3', postalCode: '84028' }, { street: 'Hauptstraße 5', postalCode: '84028' })).toBe(false);
    expect(sameAddress({ street: 'Hauptstraße 3', postalCode: '84028' }, { street: 'Hauptstraße 3', postalCode: '84034' })).toBe(false);
    expect(sameAddress({ street: 'Hauptstraße 3', city: 'Landshut' }, { street: 'Hauptstraße 3', city: 'landshut' })).toBe(true);
    expect(sameAddress({ street: '', postalCode: '84028' }, { street: '', postalCode: '84028' })).toBe(false);
  });

  it('filtert Bauakten an derselben Adresse', () => {
    const list = [
      { id: '1', street: 'Hauptstr. 3', postalCode: '84028' }, { id: '2', street: 'Hauptstraße 3', postalCode: '84028' },
      { id: '3', street: 'Am Hang 1', postalCode: '84028' },
    ];
    expect(atSameAddress(list, { street: 'Hauptstraße 3', postalCode: '84028' }).map((b) => b.id)).toEqual(['1', '2']);
    expect(atSameAddress(list, { street: 'Hauptstraße 3', postalCode: '84028' }, '2').map((b) => b.id)).toEqual(['1']);
  });

  it('baut die Routenplanung aus der Anschrift', () => {
    expect(routeUrl({ street: 'Am Hang 3', postalCode: '84028', city: 'Landshut' }))
      .toBe('https://www.google.com/maps/dir/?api=1&destination=Am%20Hang%203%2C%2084028%20Landshut');
    expect(routeUrl({})).toBeNull();
  });
});

describe('Kundenakte', () => {
  it('filtert Bauakten nach Status', () => {
    const list = ['inquiry', 'planned', 'inProgress', 'drying', 'completed', 'cancelled'].map((status) => ({ status }));
    expect(filterByStatus(list, 'active').map((b) => b.status)).toEqual(['planned', 'inProgress', 'drying']);
    expect(filterByStatus(list, 'inquiry')).toHaveLength(1);
    expect(filterByStatus(list, 'old')).toHaveLength(2);
    expect(filterByStatus(list, '')).toHaveLength(6);
  });

  it('ermittelt die letzte Aktivität', () => {
    expect(latest(null, '2026-10-01T08:00:00Z', '2026-11-02T08:00:00Z', undefined)).toBe('2026-11-02T08:00:00Z');
    expect(latest(null)).toBeNull();
  });
});
