import { describe, expect, it } from 'vitest';
import { initialInspectionState, waiverText } from '../webapp/util/inspectionWaiver';

describe('Erstbegehung nicht erforderlich', () => {
  const waived = {
    initialInspectionWaived: true, initialInspectionWaivedByName: 'Max Huber',
    initialInspectionWaivedAt: '2026-11-10T09:15:00Z', initialInspectionWaivedReason: 'Bestandsgebäude',
  };

  it('erfasste Erstbegehung hat Vorrang, sonst Vermerk oder offen', () => {
    expect(initialInspectionState(waived, [{ kind: 'initialInspection' }])).toBe('done');
    expect(initialInspectionState(waived, [{ kind: 'cmMeasurement' }])).toBe('waived');
    expect(initialInspectionState({ initialInspectionWaived: false }, [])).toBe('open');
    expect(initialInspectionState(null, [])).toBe('open');
  });

  it('zeigt Name, Datum und Begründung; leere Angaben entfallen', () => {
    expect(waiverText(waived)).toBe('Keine Erstbegehung erforderlich (Max Huber, 10.11.2026, Bestandsgebäude)');
    expect(waiverText({ ...waived, initialInspectionWaivedReason: null })).toBe('Keine Erstbegehung erforderlich (Max Huber, 10.11.2026)');
    expect(waiverText({ initialInspectionWaived: true })).toBe('Keine Erstbegehung erforderlich');
    expect(waiverText({ initialInspectionWaived: false })).toBe('');
  });
});
