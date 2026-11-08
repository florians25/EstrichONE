import { describe, expect, it } from 'vitest';
import { offloadFileRow, offloadView, storageHint, usedPercent, type OffloadOverview } from '../webapp/util/offload';

const base: OffloadOverview = {
  status: { level: 'ok', text: 'Alles in Ordnung.' }, paired: true,
  connection: { address: 'https://192.168.1.50:8445', serverName: 'Kombi – Storage', serverVersion: '0.1.4', caFingerprint: 'ab', fingerprintDisplay: 'AB',
    pairedAt: '2026-11-04T10:00:00Z', lastContactAt: '2026-11-04T10:00:00Z', lastError: null, reachable: true },
  server: { name: 'Kombi – Storage', version: '0.1.4', diskFreeBytes: 600, diskTotalBytes: 1000,
    offload: { objects: 3, bytes: 300, trashObjects: 1, trashBytes: 10, trashDays: 30, lastVerification: null } },
  settings: { autoWhenLow: true, autoByRules: false, thresholdPercent: 15, thresholdFreeGb: null, minAgeMonths: 6, onlyCompletedBauakten: false,
    includePhotos: true, includeVideos: true, includeDocuments: true, requireBackup: true, cacheLimitMb: 2048 },
  disk: { freeBytes: 100, totalBytes: 1000, freePercent: 10, low: true, bytesToFree: 100 },
  counts: { offloadedFiles: 4, offloadedBlobs: 3, offloadedBytes: 300, pendingBlobs: 1, pendingBytes: 50, missingBlobs: 0, candidateFiles: 2, candidateBytes: 80, cacheBytes: 0 },
  backupAvailable: true, lastCopy: { requestId: 'x', status: 'succeeded', message: 'In allen Zielen gesichert', at: '2026-11-04T10:05:00Z', files: 1 },
  job: { running: true, kind: 'offload', total: 4, processed: 1, failed: 0, bytes: 20, startedAt: null, finishedAt: null, message: null },
  lastRunAt: null, lastRunMessage: null,
};

describe('Speicher-Auslagerung (Anzeige)', () => {
  it('rechnet die Belegung', () => {
    expect(usedPercent(100, 1000)).toBe(90);
    expect(usedPercent(null, 1000)).toBeNull();
    expect(usedPercent(5, 0)).toBeNull();
  });

  it('zeigt knappen Platz, Fortschritt, Sicherungskopie und Wartende', () => {
    const v = offloadView(base);
    expect(v['serverUsed']).toBe(90);
    expect(v['serverState']).toBe('Warning');
    expect(String(v['thresholdText'])).toContain('Knapp unter 15 % frei');
    expect(String(v['thresholdText'])).toContain('zum Ziel fehlen');
    expect(v['jobPercent']).toBe(25);
    expect(String(v['jobText'])).toContain('Auslagern: 1 von 4');
    expect(v['backupState']).toBe('Success');
    expect(String(v['pending'])).toContain('Sicherungskopie ausstehend');
    expect(String(v['storageObjects'])).toContain('Papierkorb 1 (30 Tage)');
    expect(v['canRun']).toBe(false);
  });

  it('warnt ohne Sicherungskopie und ohne Backup-Dienst', () => {
    expect(offloadView({ ...base, settings: { ...base.settings, requireBackup: false } })['backupState']).toBe('Warning');
    expect(String(offloadView({ ...base, backupAvailable: false })['backup'])).toContain('Kein Backup-Dienst');
    expect(offloadView({ ...base, status: { level: 'critical', text: 'fehlt' } })['statusType']).toBe('Error');
  });

  it('beschreibt Dateien je Zustand', () => {
    const row = offloadFileRow({ fileId: '1', name: 'Plan.pdf', kind: 'document', bauakteId: 'b', bauakteTitle: 'EFH', sizeBytes: 2048, state: 'missing',
      uploadedAt: '2026-11-04T10:00:00Z', offloadedAt: null, backedUpAt: null, lastError: 'fehlt' });
    expect(row['stateState']).toBe('Error');
    expect(row['kindText']).toBe('Dokument');
    expect(row['canRecall']).toBe(true);
  });
});

describe('Hinweis beim Öffnen', () => {
  const respond = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
  it('meldet „wird vom Storage geladen“, Ausfall und Fehlen', async () => {
    const local = await storageHint('f1', (async () => respond(200, [])) as typeof fetch);
    expect(local).toBeNull();
    const ok = await storageHint('f1', (async (url: string) => String(url).includes('locations') ? respond(200, [{ fileId: 'f1', state: 'offloaded' }]) : new Response('x', { status: 206 })) as typeof fetch);
    expect(ok).toMatchObject({ type: 'Information' });
    expect(ok?.text).toContain('vom Storage');
    const down = await storageHint('f1', (async (url: string) => String(url).includes('locations') ? respond(200, [{ fileId: 'f1', state: 'offloaded' }])
      : respond(503, { detail: 'Storage „Keller“ nicht erreichbar' })) as typeof fetch);
    expect(down).toEqual({ type: 'Error', text: 'Storage „Keller“ nicht erreichbar' });
    const missing = await storageHint('f1', (async () => respond(200, [{ fileId: 'f1', state: 'missing' }])) as typeof fetch);
    expect(missing?.type).toBe('Error');
  });
});
