import { describe, expect, it } from 'vitest';
import {
  appointmentText, initialAppointments, initialStatus, initialStatusView, isInitialInspectionDocument, relevantAppointment,
} from '../webapp/util/initialInspectionPlan';
import { crewOf, personInitials, teamCardText, teamCards } from '../webapp/util/teamCards';

const akte = 'a1';
const assignment = (id: string, kind: string, day: string, startTime: string | null = '08:00:00') => ({
  id, kind, day, startTime, endTime: null, crewName: 'Kolonne Nord', people: [{ id: 'u1', name: 'Max Huber' }],
});

describe('Erstbegehung: Termin und Status', () => {
  it('nimmt nur Einsätze „Erstbegehung“ und Kalendertermine der Akte mit „Erstbegehung“ im Titel', () => {
    const list = initialAppointments(akte, [assignment('x', 'screed', '2026-11-20'), assignment('p', 'initialInspection', '2026-11-19')], [
      { eventId: 'e1', kind: 'siteVisit', title: 'Erstbegehung: EFH Kraus', start: '2026-11-18T09:30:00', end: '2026-11-18T10:30:00', allDay: false,
        bauakteId: akte, participants: [{ name: 'Anna Admin' }] },
      { eventId: 'e2', kind: 'siteVisit', title: 'Erstbegehung: andere Akte', start: '2026-11-18T09:30:00', end: '2026-11-18T10:30:00', allDay: false, bauakteId: 'b2' },
      { eventId: 'e3', kind: 'meeting', title: 'Baubesprechung', start: '2026-11-17T09:00:00', end: '2026-11-17T10:00:00', allDay: false, bauakteId: akte },
    ]);
    expect(list.map((a) => a.id)).toEqual(['e1', 'p']);
    expect(list[0]).toMatchObject({ day: '2026-11-18', start: '09:30', end: '10:30', who: 'Anna Admin', source: 'calendar' });
    expect(list[1]).toMatchObject({ start: '08:00', who: 'Kolonne Nord, Max Huber', source: 'assignment' });
  });

  it('nächster Termin ab heute, sonst der letzte vergangene', () => {
    const list = initialAppointments(akte, [assignment('alt', 'initialInspection', '2026-10-01'), assignment('neu', 'initialInspection', '2026-12-01')], []);
    expect(relevantAppointment(list, '2026-11-12')?.id).toBe('neu');
    expect(relevantAppointment(list, '2026-12-02')?.id).toBe('neu');
    expect(relevantAppointment(list.slice(0, 1), '2026-11-12')?.id).toBe('alt');
    expect(relevantAppointment([], '2026-11-12')).toBeNull();
  });

  it('Termintext mit Wochentag, Uhrzeit und Beteiligten', () => {
    const [a] = initialAppointments(akte, [{ ...assignment('p', 'initialInspection', '2026-11-19'), endTime: '10:00:00' }], []);
    expect(appointmentText(a)).toBe('Do., 19.11.2026, 08:00–10:00 · Kolonne Nord, Max Huber');
    expect(appointmentText({ ...a, start: '', end: '', who: '' })).toBe('Do., 19.11.2026');
    expect(appointmentText(null)).toBe('');
  });

  it('Status: erfasst vor Vermerk vor Termin', () => {
    const [a] = initialAppointments(akte, [assignment('p', 'initialInspection', '2026-11-19')], []);
    expect(initialStatus({ initialInspectionWaived: true }, [{ kind: 'initialInspection' }], a)).toBe('done');
    expect(initialStatus({ initialInspectionWaived: true }, [], a)).toBe('waived');
    expect(initialStatus({ initialInspectionWaived: false }, [{ kind: 'cmMeasurement' }], a)).toBe('planned');
    expect(initialStatus(null, [], null)).toBe('open');
    expect(initialStatusView.planned.text).toBe('Geplant');
    expect(initialStatusView.done.text).toBe('Durchgeführt');
    expect(initialStatusView.waived.text).toBe('Nicht erforderlich');
  });

  it('Dokumente der Erstbegehung: gespeicherte Dateien im Ordner „Erstbegehung“', () => {
    expect(isInitialInspectionDocument({ kind: 'document', folder: 'Erstbegehung', state: 'stored' })).toBe(true);
    expect(isInitialInspectionDocument({ kind: 'document', folder: 'Protokolle/Erstbegehung', state: 'stored' })).toBe(true);
    expect(isInitialInspectionDocument({ kind: 'document', folder: 'Erstbegehung', state: 'awaitingUpload' })).toBe(false);
    expect(isInitialInspectionDocument({ kind: 'photo', folder: 'Erstbegehung', state: 'stored' })).toBe(false);
    expect(isInitialInspectionDocument({ kind: 'document', folder: 'Abnahme', state: 'stored' })).toBe(false);
    expect(isInitialInspectionDocument({ kind: 'document', folder: null, state: 'stored' })).toBe(false);
  });
});

describe('Team im Betrieb als Karten', () => {
  const members = [
    { userId: 'u2', displayName: 'Bert Berger', email: null, role: 'member' as const, isActive: true },
    { userId: 'u1', displayName: 'Markus Leis', email: 'm.leis@example.org', role: 'responsible' as const, isActive: true },
    { userId: 'u3', displayName: 'Carla Cord', email: null, role: 'member' as const, isActive: false },
  ];
  const plan = [
    { day: '2026-11-01', crewName: 'Kolonne Süd', people: [{ id: 'u2' }] },
    { day: '2026-11-20', crewName: 'Kolonne Nord', people: [{ id: 'u2' }, { id: 'u1' }] },
  ];

  it('zuständige Person zuerst, Rolle, Kolonne aus dem nächsten Einsatz, E-Mail als Link', () => {
    const cards = teamCards(members, plan, '2026-11-12');
    expect(cards.map((c) => c.displayName)).toEqual(['Markus Leis', 'Bert Berger', 'Carla Cord']);
    expect(cards[0]).toMatchObject({ initials: 'ML', roleText: 'Zuständig', responsible: true, subtitle: 'Zuständig für die Bauakte', crewText: 'Kolonne Nord' });
    expect(cards[0].lines).toEqual([{ kind: 'E-Mail', icon: 'sap-icon://email', text: 'm.leis@example.org', href: 'mailto:m.leis@example.org', label: 'E-Mail' }]);
    expect(cards[1]).toMatchObject({ roleText: 'Mitarbeiter', subtitle: 'Kolonne Nord', crewText: '', lines: [] });
    expect(cards[2]).toMatchObject({ roleText: 'Gesperrt', subtitle: 'Mitarbeiter im Betrieb' });
    expect(teamCardText(cards[0])).toBe('Markus Leis (Zuständig)\nZuständig für die Bauakte\nE-Mail: m.leis@example.org');
  });

  it('Kolonne: nächster Einsatz, sonst letzter; Kürzel', () => {
    expect(crewOf('u2', plan, '2026-12-01')).toBe('Kolonne Nord');
    expect(crewOf('u2', plan.slice(0, 1), '2026-12-01')).toBe('Kolonne Süd');
    expect(crewOf('u9', plan, '2026-11-12')).toBe('');
    expect(personInitials('Anna')).toBe('AN');
    expect(personInitials('  ')).toBe('?');
  });
});
