import { describe, expect, it } from 'vitest';
import {
  allowedDecisions, consumption, defaultRange, fuelKpis, fuelQuery, fuelStatus, km, liters, monthText, settleText, statisticRows, warningSummary,
  type FuelReceiptResponse,
} from '../webapp/util/fuel';
import { navigation, routes } from '../webapp/util/routes';
import { filterPermissions, modulesOfPath, pathEnabled } from '../webapp/util/modules';

const r = (over: Partial<FuelReceiptResponse>): FuelReceiptResponse => ({
  id: 'x', number: 'TB-2026-0001', vehicleId: 'v', vehicleName: 'Sprinter 3', vehicleIdentifier: 'LA-EB 300', userId: 'u', userName: 'Max Huber',
  date: '2026-11-18', odometerKm: 48210, liters: 61.37, amountGross: 104.27, pricePerLiter: 1.699, fuelType: 'diesel', paymentMethod: 'fuelCard',
  station: null, notes: null, imageFileId: 'f', imageStored: true, status: 'new', statusText: 'Neu', rejectionReason: null, checkedByName: null,
  checkedAt: null, settledByName: null, settledAt: null, distanceKm: null, consumptionPer100Km: null, warnings: [], source: 'ios', canEdit: true,
  createdAt: '2026-11-18T10:00:00Z', version: 1, ...over,
});

describe('Tankbelege', () => {
  it('Status: erstattet bei privat ausgelegt, sonst gebucht', () => {
    expect(fuelStatus('settled', 'private').text).toBe('Erstattet');
    expect(fuelStatus('settled', 'fuelCard').text).toBe('Gebucht');
    expect(fuelStatus('rejected', 'private')).toEqual({ text: 'Abgelehnt', tone: 'Error' });
    expect(settleText([r({ paymentMethod: 'private' })])).toBe('Als erstattet markieren');
    expect(settleText([r({}), r({ paymentMethod: 'companyCard' })])).toBe('Als gebucht markieren');
    expect(settleText([r({}), r({ paymentMethod: 'private' })])).toBe('Als erstattet bzw. gebucht markieren');
  });

  it('Entscheidungen nur, wenn sie für alle gewählten Belege passen', () => {
    expect(allowedDecisions([])).toEqual([]);
    expect(allowedDecisions([r({ status: 'new' })])).toEqual(['check', 'reject']);
    expect(allowedDecisions([r({ status: 'checked' })])).toEqual(['reject', 'settle', 'reopen']);
    expect(allowedDecisions([r({ status: 'new' }), r({ status: 'checked' })])).toEqual(['reject']);
    expect(allowedDecisions([r({ status: 'settled' })])).toEqual([]);
  });

  it('Kennzahlen: offene Erstattungen, Kosten ohne abgelehnte, Liter ohne AdBlue', () => {
    const k = fuelKpis([
      r({ paymentMethod: 'private', amountGross: 50.5 }), r({ paymentMethod: 'private', status: 'checked', amountGross: 20 }),
      r({ status: 'rejected', amountGross: 999 }), r({ fuelType: 'adBlue', liters: 10, amountGross: 9, status: 'checked' }),
      r({ warnings: [{ code: 'duplicate', text: 'doppelt' }] }), r({ warnings: [{ code: 'missing_image', text: 'ohne Foto' }] }),
    ]);
    expect(k.refundOpen).toBe(2);
    expect(k.refundAmount).toBe(70.5);
    expect(k.cost).toBe(Math.round((50.5 + 20 + 9 + 104.27 * 2) * 100) / 100);
    expect(k.liters).toBe(Math.round(61.37 * 4 * 100) / 100);
    expect(k.flagged).toBe(1);
    expect(k.open).toBe(3);
  });

  it('Filter und Zeitraum', () => {
    expect(defaultRange('2026-11-20')).toEqual({ from: '2026-10-01', to: '2026-11-20' });
    expect(defaultRange('2026-01-05')).toEqual({ from: '2025-12-01', to: '2026-01-05' });
    expect(fuelQuery({ from: '2026-10-01', to: null, vehicleId: '', userId: 'u', status: 'open', paymentMethod: '', warningsOnly: false }))
      .toEqual({ from: '2026-10-01', to: undefined, vehicleId: undefined, userId: 'u', status: 'open', paymentMethod: undefined, warningsOnly: undefined });
  });

  it('Anzeige: Liter, Kilometer, Verbrauch, Monate, Hinweise', () => {
    expect(liters(61.37)).toBe('61,37 l');
    expect(km(48210)).toBe('48.210 km');
    expect(consumption(11)).toBe('11,0 l/100 km');
    expect(consumption(null)).toBe('keine Angabe');
    expect(monthText('2026-03')).toBe('März 2026');
    expect(warningSummary([{ code: 'missing_image', text: '' }])).toEqual({ text: 'Ohne Foto', tone: 'Warning' });
    expect(warningSummary([{ code: 'consumption_unusual', text: '' }, { code: 'duplicate', text: '' }])).toEqual({ text: '2 Hinweise', tone: 'Error' });
    const rows = statisticRows([{ vehicleId: 'v', vehicleName: 'Sprinter 3', identifier: 'LA-EB 300', receipts: 2, liters: 100, adBlueLiters: 0, cost: 170,
      distanceKm: 800, averageConsumption: 12.5, lastOdometerKm: 1000, months: [
        { month: '2026-10', receipts: 1, liters: 50, adBlueLiters: 0, cost: 85, distanceKm: 400, averageConsumption: 12.5 },
        { month: '2026-11', receipts: 1, liters: 50, adBlueLiters: 0, cost: 85, distanceKm: 400, averageConsumption: 12.5 }] }]);
    expect(rows.map((x) => x.monthText)).toEqual(['November 2026', 'Oktober 2026']);
    expect(rows[0].vehicle).toBe('Sprinter 3 (LA-EB 300)');
  });

  it('Seite im Büro, abschaltbar über das Modul „Tankbelege“', () => {
    const office = navigation.find((e) => e.key === 'groupOffice');
    expect(office?.children?.map((e) => e.key)).toContain('fuelReceipts');
    expect(routes.find((x) => x.name === 'fuelReceipts')).toMatchObject({ pattern: '/tankbelege', permission: 'fuel.record' });
    expect(modulesOfPath('/tankbelege?fahrzeug=1')).toEqual(['fuel']);
    expect(pathEnabled({ fuel: false }, '/tankbelege')).toBe(false);
    expect(filterPermissions({ fuel: false }, ['fuel.record', 'fuel.manage', 'planning.read'])).toEqual(['planning.read']);
  });
});
