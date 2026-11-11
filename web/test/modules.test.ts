import { describe, expect, it } from 'vitest';
import {
  dependencyHint, dependentsToDisable, disableQuestion, disabledModuleOfPath, filterPermissions, modulesOfPath, pathEnabled, requirementsToEnable,
  type ModuleInfo,
} from '../webapp/util/modules';
import { adminSections } from '../webapp/util/adminCatalog';

const info = (key: string, title: string, requires: string[] = [], requiredBy: string[] = [], enabled = true): ModuleInfo =>
  ({ key, title, area: 'x', description: '', core: false, enabled, requires, requiredBy });

const catalog = [
  info('planning', 'Disposition und Kolonnenplanung', [], ['machines']),
  info('machines', 'Maschinen und Fahrzeuge', ['planning']),
  info('staff', 'Personal', [], ['absence']),
  info('absence', 'Urlaub und Abwesenheit', ['staff']),
  info('materials', 'Material und Lieferanten', [], ['orders']),
  info('orders', 'Bestellungen', ['materials']),
];

describe('Module: Seiten', () => {
  it('ordnet Pfade dem Modul zu, längster Pfad und passende Reiter gewinnen', () => {
    expect(modulesOfPath('/planung/geraete')).toEqual(['machines']);
    expect(modulesOfPath('/planung/kolonnen?reiter=geraete')).toEqual(['machines']);
    expect(modulesOfPath('/planung/kolonnen')).toEqual(['planning']);
    expect(modulesOfPath('/planung/cockpit?ansicht=day')).toEqual(['planning']);
    expect(modulesOfPath('/personal/urlaub?reiter=plan')).toEqual(['absence']);
    expect(modulesOfPath('/personal/0190')).toEqual(['staff']);
    expect(modulesOfPath('/bauakten/karte')).toEqual(['map']);
    expect(modulesOfPath('/bauakten/123')).toEqual([]);
    expect(modulesOfPath('/dokumente?reiter=freigaben')).toEqual(['approvals']);
    expect(modulesOfPath('/dokumente')).toEqual([]);
    expect(modulesOfPath('/materialien')).toEqual([]); // nur ganze Pfadsegmente
  });

  it('blendet Ziele abgeschalteter Module aus', () => {
    const states = { calls: false, machines: false };
    expect(pathEnabled(states, '/anrufe')).toBe(false);
    expect(pathEnabled(states, '/anfragen')).toBe(false);
    expect(pathEnabled(states, '/planung/geraete')).toBe(false);
    expect(pathEnabled(states, '/planung')).toBe(true);
    expect(pathEnabled({}, '/anrufe')).toBe(true); // fehlende Schalter gelten als an
    expect(disabledModuleOfPath(states, '/anrufe?id=1')).toBe('calls');
    expect(disabledModuleOfPath(states, '/start')).toBeNull();
  });

  it('nimmt Rechte abgeschalteter Module aus der Oberfläche', () => {
    const all = ['planning.read', 'hr.read', 'absence.request', 'materials.read', 'work.read', 'work.edit', 'bauakten.read', 'chat.use'];
    expect(filterPermissions({ planning: false, chat: false }, all)).toEqual(['hr.read', 'absence.request', 'materials.read', 'work.read', 'work.edit', 'bauakten.read']);
    // Aufgaben aus, Mängel an: work.* bleibt.
    expect(filterPermissions({ tasks: false }, all)).toContain('work.read');
    expect(filterPermissions({ tasks: false, defects: false }, all)).not.toContain('work.read');
  });

  it('Verwaltungsübersicht ohne Bereiche abgeschalteter Module', () => {
    const titles = adminSections(() => true, '', (path) => pathEnabled({ dossiers: false, helpdesk: false }, path)).flatMap((g) => g.areas.map((a) => a.key));
    expect(titles).not.toContain('dossiers');
    expect(titles).not.toContain('helpdesk');
    expect(titles).toContain('modules');
  });
});

describe('Module: Abhängigkeiten', () => {
  it('Ausschalten nimmt abhängige mit, Einschalten die Voraussetzungen', () => {
    expect(dependentsToDisable(catalog, 'staff').map((m) => m.key)).toEqual(['absence']);
    expect(dependentsToDisable(catalog, 'orders')).toEqual([]);
    const off = catalog.map((m) => (m.key === 'staff' || m.key === 'absence' ? { ...m, enabled: false } : m));
    expect(requirementsToEnable(off, 'absence').map((m) => m.key)).toEqual(['staff']);
    expect(requirementsToEnable(catalog, 'absence')).toEqual([]);
  });

  it('erklärt Abhängigkeiten und fragt verständlich nach', () => {
    const staff = catalog[2];
    expect(dependencyHint(catalog, catalog[3])).toBe('Benötigt: Personal');
    expect(dependencyHint(catalog, staff)).toBe('Wird benötigt von: Urlaub und Abwesenheit');
    const question = disableQuestion(staff, [catalog[3]]);
    expect(question).toContain('„Personal“ ausschalten?');
    expect(question).toContain('„Urlaub und Abwesenheit“ ausgeschaltet');
    expect(question).toContain('Daten bleiben erhalten');
  });
});
