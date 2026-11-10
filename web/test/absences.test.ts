import { describe, expect, it } from 'vitest';
import { accountTexts, cellBackground, colorDot, dayFraction, escapeHtml, num, range, requestRow, typeRules, type RequestView, type VacationAccount } from '../webapp/util/absences';

const account: VacationAccount = {
  year: 2027, entitlement: 22.5, carryOver: 5, taken: 3, planned: 4.5, remaining: 20, sickDays: 0, requested: 2, annualEntitlement: 30, adjustment: 0,
  carryOverExpiresOn: '2027-03-31', carryOverExpired: 0, carryOverExpiring: 2, remainingAfterRequests: 18, byType: [], note: null,
};

const request: RequestView = {
  id: 'r1', userId: 'u1', userName: 'Anna Kraus', from: '2027-06-07', to: '2027-06-09', kind: 'vacation', note: 'Familie', status: 'pending', workingDays: 2.5,
  requestedByName: 'Anna Kraus', decidedByName: null, decisionComment: null, warnings: [], remainingAfter: 17.5, canDecide: false, canCancel: true,
  typeId: 't1', typeName: 'Urlaub', typeColor: '#43A047', halfDayStart: true, halfDayEnd: false, substituteUserId: 'u2', substituteName: 'Ben Maier',
  replacesRequestId: null, canChange: false, countsAsVacation: true,
};

describe('Abwesenheiten', () => {
  it('Zeiträume mit halben Tagen', () => {
    expect(range('2027-06-07', '2027-06-07')).toBe('07.06.2027');
    expect(range('2027-06-07', '2027-06-07', true)).toBe('07.06.2027 (halber Tag)');
    expect(range('2027-06-07', '2027-06-09')).toBe('07.06. – 09.06.2027');
    expect(range('2027-12-28', '2028-01-02', false, true)).toBe('28.12.2027 – 02.01.2028 (letzter Tag halb)');
    expect(range('2027-06-07', '2027-06-09', true, true)).toBe('07.06. – 09.06.2027 (erster Tag halb, letzter Tag halb)');
  });

  it('Tagesanteil und Zellfarbe', () => {
    const entry = { from: '2027-06-07', to: '2027-06-09', halfDayStart: true, halfDayEnd: false };
    expect(dayFraction(entry, '2027-06-07')).toBe(0.5);
    expect(dayFraction(entry, '2027-06-08')).toBe(1);
    expect(cellBackground('#43A047', false, false)).toBe('#43A04780');
    expect(cellBackground('#43A047', true, true)).toContain('linear-gradient');
  });

  it('Konto: Verfall, Anteil, Rest nach Anträgen', () => {
    const t = accountTexts(account);
    expect(t.entitlement).toBe('22,5');
    expect(t.entitlementInfo).toBe('Jahresanspruch 30');
    expect(t.carryInfo).toBe('2 verfallen am 31.03.2027');
    expect(t.expiringText).toBe('2 Tage Resturlaub aus dem Vorjahr verfallen nach dem 31.03.2027, wenn sie bis dahin nicht genommen oder geplant sind.');
    expect(t.afterRequests).toBe('nach Anträgen 18');
    expect(t.adjustment).toBe('–');
    expect(accountTexts({ ...account, adjustment: 1.5, carryOverExpiring: 0 }).adjustment).toBe('+1,5');
    expect(accountTexts({ ...account, carryOverExpiring: 0 }).expiringText).toBe('');
  });

  it('Antragszeile: Status, Vertretung, Änderungsantrag, Bearbeiten nur offen und eigen', () => {
    const row = requestRow(request, true);
    expect(row.statusText).toBe('Beantragt');
    expect(row.workingDaysText).toBe('2,5');
    expect(row.detailText).toBe('Vertretung: Ben Maier · Familie');
    expect(row.canEdit).toBe(true);
    expect(requestRow(request, false).canEdit).toBe(false);
    expect(requestRow({ ...request, replacesRequestId: 'r0' }, true).changeText).toBe('Änderungsantrag');
    expect(requestRow({ ...request, status: 'cancelled', decisionComment: 'Durch Änderungsantrag ersetzt.' }, true).statusText).toBe('Ersetzt');
  });

  it('Regeln, Zahlen und sichere Ausgabe', () => {
    expect(typeRules({ requiresApproval: true, countsAsVacation: true, requestable: true, isPaid: true, isSickness: false })).toBe('genehmigungspflichtig, zählt als Urlaub');
    expect(typeRules({ requiresApproval: false, countsAsVacation: false, requestable: false, isPaid: false, isSickness: false })).toBe('ohne Genehmigung, nur Büro, unbezahlt');
    expect(typeRules({ requiresApproval: false, countsAsVacation: false, requestable: false, isPaid: true, isSickness: true })).toContain('vertraulich');
    expect(num(null)).toBe('–');
    expect(num(2.25)).toBe('2,3');
    expect(escapeHtml('<b>"A&B"</b>')).toBe('&lt;b&gt;&quot;A&amp;B&quot;&lt;/b&gt;');
    expect(colorDot('red;background:url(x)')).toContain('#9E9E9E');
    expect(colorDot('#1E88E5')).toContain('#1E88E5');
  });
});
