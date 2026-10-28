import { describe, expect, it } from 'vitest';
import { activeStatus, peerRow, type PeerUpdateDto } from '../webapp/util/peerUpdates';

const base: PeerUpdateDto = {
  key: 'target-1', kind: 'backup-target', name: 'NAS Keller', installedVersion: '0.1.0-preview.22', availableVersion: '0.1.0-preview.23',
  updateAvailable: true, updaterAvailable: true, updaterVersion: '0.1.0-preview.22', reachable: true, lastCheckedAt: null, status: 'none',
  targetVersion: null, previousVersion: null, message: null, requestedAt: null, finishedAt: null, autoUpdate: false,
  localCommand: 'sudo docker run … backup-ziel',
};

describe('Fernaktualisierung der Zusatzserver', () => {
  it('bietet Aktualisieren nur mit Updater an, sonst den Befehl für den Rechner', () => {
    expect(peerRow(base)).toMatchObject({ canUpdate: true, needsLocal: false, state: 'Warning', stateText: 'Version 0.1.0-preview.23 verfügbar' });
    expect(peerRow({ ...base, updaterAvailable: false })).toMatchObject({ canUpdate: false, needsLocal: true });
    expect(peerRow({ ...base, updateAvailable: false, installedVersion: '0.1.0-preview.23' })).toMatchObject({ canUpdate: false, state: 'Success', stateText: 'Aktuell' });
  });

  it('zeigt laufende Aufträge, Rücksprung und Fehler', () => {
    expect(activeStatus('delivered')).toBe(true);
    expect(peerRow({ ...base, status: 'running', targetVersion: '0.1.0-preview.23' })).toMatchObject({ active: true, canUpdate: false, state: 'Information' });
    expect(peerRow({ ...base, kind: 'backup-mirror', status: 'requested', targetVersion: '0.1.0-preview.23' }).stateText).toContain('Wartet auf Abholung');
    expect(peerRow({ ...base, status: 'rolled_back', finishedAt: '2026-10-28T10:00:00Z', message: 'nicht gesund' })).toMatchObject({ state: 'Warning' });
    expect(peerRow({ ...base, status: 'failed', finishedAt: '2026-10-28T10:00:00Z', message: 'Nicht erreichbar' }).stateText).toContain('Nicht erreichbar');
  });
});
