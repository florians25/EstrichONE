import { describe, expect, it } from 'vitest';
import {
  certificateStatus, settingsSummary, severityState, warningCounts, warningKpi, warningRow, warningSummary,
  type AbsenceWarning, type AbsenceWarningSettings,
} from '../webapp/util/absenceWarnings';

const impact: AbsenceWarning = {
  key: 'plan:a1:abc', kind: 'planning_impact', severity: 'error', userId: 'u1', userName: 'Anna Kraus', employeeId: null, date: '2026-11-19',
  title: 'Anna Kraus abwesend 18.11. – 20.11.2026', text: '1 geplanter Einsatz betroffen, unterbesetzt: Kolonne Süd.', absenceId: 'a1', caseId: null,
  link: '/planung?ansicht=tag&datum=2026-11-19',
  assignments: [{ id: 'x', day: '2026-11-19', bauakteId: 'b', bauakteTitle: 'Halle Süd', activity: 'Estrich', crewId: 'c', crewName: 'Kolonne Süd', present: 1,
    minimumStaff: 2, understaffed: true, text: 'Do 19.11.: Halle Süd – Estrich, Kolonne Süd · unterbesetzt (1 von mindestens 2)' }],
};
const au: AbsenceWarning = { ...impact, key: 'au:a1', kind: 'certificate_missing', severity: 'warning', title: 'Anna Kraus: AU-Bescheinigung fehlt',
  text: 'Nachweis erwartet ab dem 18.11.2026', link: '/personal/urlaub?reiter=krank', assignments: [] };
const bem: AbsenceWarning = { ...au, key: 'bem:u1:c1', kind: 'bem', severity: 'info', absenceId: null };

const settings: AbsenceWarningSettings = {
  enabled: true, planningImpact: true, planningImpactDays: 14, certificate: true, certificateDueDay: 4, continuedPay: true, bem: true, vacationOverlap: true,
  frequentShortThreshold: 0, adjacent: false, notifyHr: true, notifyPlanners: true, notifyCrewLeaders: false,
};

describe('Abwesenheitswarnungen', () => {
  it('Zeile mit Schwere, Art, betroffenen Einsätzen und Aktionen', () => {
    const row = warningRow(impact);
    expect(row.state).toBe('Error');
    expect(row.severityLabel).toBe('Dringend');
    expect(row.kindLabel).toBe('Einsätze betroffen');
    expect(row.dateText).toBe('19.11.2026');
    expect(row.detailText).toContain('• Do 19.11.: Halle Süd');
    expect(row.canPlan).toBe(true);
    expect(row.canRetrieve).toBe(false);
    expect(warningRow(au).canRetrieve).toBe(true);
    expect(severityState('info')).toBe('Information');
  });

  it('Zähler, MessageStrip und Kennzahl', () => {
    expect(warningCounts([impact, au, bem])).toEqual({ total: 3, errors: 1, warnings: 1, infos: 1, planning: 1 });
    expect(warningSummary({ enabled: true, items: [impact, au, bem] })).toEqual({
      text: '1 dringend, 1 Warnung, 1 Hinweis · 1 Abwesenheit betrifft geplante Einsätze.', type: 'Error' });
    expect(warningSummary({ enabled: true, items: [] }).type).toBe('Success');
    expect(warningSummary({ enabled: false, items: [] }).text).toContain('abgeschaltet');
    expect(warningKpi({ enabled: true, healthDetails: false, items: [impact], acknowledged: 0 }, false))
      .toEqual({ value: '1', infoState: 'Error', to: '/planung?ansicht=tag&datum=2026-11-19' });
    expect(warningKpi({ enabled: true, healthDetails: true, items: [au], acknowledged: 0 }, true)?.to).toBe('/personal/urlaub?reiter=krank');
    expect(warningKpi({ enabled: false, healthDetails: true, items: [], acknowledged: 0 }, true)).toBeNull();
  });

  it('AU-Status je Krankmeldung', () => {
    expect(certificateStatus('attached', null)).toEqual({ text: 'vorhanden', state: 'Success' });
    expect(certificateStatus('retrieved', null).text).toBe('eAU abgerufen');
    expect(certificateStatus('missing', '2026-11-18')).toEqual({ text: 'fehlt', state: 'Warning' });
    expect(certificateStatus('later', '2026-11-21').text).toBe('ab 21.11.2026');
    expect(certificateStatus('notRequired', null).text).toBe('nicht erforderlich');
    expect(certificateStatus(undefined, null).text).toBe('fehlt');
  });

  it('Kurzbeschreibung der Einstellungen', () => {
    expect(settingsSummary(settings)).toBe('Einsätze (14 Tage Vorschau), AU ab 4. Kalendertag, Entgeltfortzahlung, BEM, Urlaubsüberschneidung · Benachrichtigung: Personal, Disposition');
    expect(settingsSummary({ ...settings, frequentShortThreshold: 4, adjacent: true, notifyHr: false, notifyPlanners: false })).toContain('Häufung ab 4, angrenzende freie Tage · Benachrichtigung: niemand');
    expect(settingsSummary({ ...settings, enabled: false })).toBe('Abgeschaltet.');
  });
});
