import { describe, expect, it } from 'vitest';
import { cockpitDays, hours, minutes, toSave, toTime, urgencyOf, type PlanAssignment } from '../webapp/util/cockpitModels';

describe('Disponenten-Cockpit', () => {
  it('Tage je Ansicht', () => {
    expect(cockpitDays('day', '2026-10-27')).toEqual(['2026-10-27']);
    const week = cockpitDays('week', '2026-10-28');
    expect(week[0]).toBe('2026-10-26');
    expect(week).toHaveLength(7);
    expect(cockpitDays('twoWeeks', '2026-10-27')).toHaveLength(14);
    const month = cockpitDays('month', '2026-02-10');
    expect(month[0]).toBe('2026-02-01');
    expect(month).toHaveLength(28);
  });

  it('Dauer wie der Server: ganztägig 8 h, ohne Ende bis 16 Uhr', () => {
    expect(minutes(null, null)).toBe(480);
    expect(minutes('07:00:00', null)).toBe(480);
    expect(minutes('13:00:00', null)).toBe(180);
    expect(minutes('07:00:00', '11:30:00')).toBe(270);
    expect(toTime(390)).toBe('06:30:00');
    expect(hours(330)).toBe('5,5');
    expect(hours(480)).toBe('8');
    expect(hours(20)).toBe('0,3');
  });

  it('Dringlichkeit im Vorrat', () => {
    expect(urgencyOf({ plannedStart: '2026-10-20' }, '2026-10-27')).toBe('overdue');
    expect(urgencyOf({ plannedStart: '2026-11-02' }, '2026-10-27')).toBe('soon');
    expect(urgencyOf({ plannedStart: '2026-12-01' }, '2026-10-27')).toBe('later');
    expect(urgencyOf({ plannedStart: null }, '2026-10-27')).toBe('none');
  });

  it('Speicherform: Kolonnenmitglieder des Tages zählen nicht als zusätzliche Mitarbeiter', () => {
    const a = {
      id: '1', day: '2026-10-27', startTime: '07:00:00', endTime: null, activity: 'Estrich', bauakteId: 'b', bauakteNumber: null, bauakteTitle: 'EFH',
      bauakteAddress: null, crewId: 'c', crewName: 'Nord', crewColor: null, people: [{ id: 'u1', name: 'A' }, { id: 'u2', name: 'B' }],
      resources: [{ id: 'r', name: 'Sprinter', kind: 'vehicle' }], notes: null, warnings: [], version: 1, kind: 'screed', bauakteStatus: null,
    } as PlanAssignment;
    const save = toSave(a, ['u1']);
    expect(save.userIds).toEqual(['u2']);
    expect(save.resourceIds).toEqual(['r']);
    expect(toSave({ ...a, crewId: null }, []).userIds).toEqual(['u1', 'u2']);
  });
});
