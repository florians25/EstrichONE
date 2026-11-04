import { describe, expect, it } from 'vitest';
import { archiveProgress, completeArchiveOption, kombiAddresses, kombiCommand, kombiSummary, sameEndpoint } from '../webapp/util/kombiserver';

describe('Kombiserver (Backup-Ziel + Archiv auf einem Rechner)', () => {
  it('leitet aus einer Adresse beide Dienste mit ihren Ports ab', () => {
    expect(kombiAddresses('192.168.1.50')).toEqual({ backup: '192.168.1.50:8444', archive: '192.168.1.50:8443' });
    expect(kombiAddresses(' https://kombi.firma.de:9999/ ')).toEqual({ backup: 'kombi.firma.de:8444', archive: 'kombi.firma.de:8443' });
    expect(kombiAddresses('[fd00::5]', 9444, 9443)).toEqual({ backup: '[fd00::5]:9444', archive: '[fd00::5]:9443' });
    expect(() => kombiAddresses('')).toThrow(/Adresse/);
    expect(() => kombiAddresses('kombi firma')).toThrow(/Adresse/);
    expect(() => kombiAddresses('kombi.local', 8443, 8443)).toThrow(/verschiedenen Ports/);
    expect(() => kombiAddresses('kombi.local', 0, 8443)).toThrow(/Ungültiger Port/);
  });

  it('erkennt bereits gekoppelte Dienste und fasst das Ergebnis zusammen', () => {
    expect(sameEndpoint('https://192.168.1.50:8443', '192.168.1.50:8443')).toBe(true);
    expect(sameEndpoint('https://192.168.1.50:8444/', '192.168.1.50:8443')).toBe(false);
    expect(sameEndpoint(null, '192.168.1.50:8443')).toBe(false);
    expect(kombiSummary([{ service: 'backup', state: 'paired', text: 'gekoppelt' }, { service: 'archive', state: 'already', text: 'bereits gekoppelt' }]))
      .toMatchObject({ ok: true, partial: false, text: 'Backup-Ziel: gekoppelt\nArchivserver: bereits gekoppelt' });
    expect(kombiSummary([{ service: 'backup', state: 'paired', text: 'x' }, { service: 'archive', state: 'failed', text: 'Code falsch' }]))
      .toMatchObject({ ok: false, partial: true });
    expect(kombiSummary([{ service: 'backup', state: 'failed', text: 'x' }, { service: 'archive', state: 'failed', text: 'y' }]))
      .toMatchObject({ ok: false, partial: false });
  });

  it('nennt den Installer-Modus backup-archiv mit eingebundenem Verzeichnis', () => {
    expect(kombiCommand('0.1.3')).toBe('sudo docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -v /opt/estrichone-kombi:/opt/estrichone-kombi '
      + 'ghcr.io/florians25/estrichone-installer:0.1.3 backup-archiv');
  });
});

describe('Abschließen & Archivieren', () => {
  it('nur mit eingerichtetem Archivserver; nicht erreichbar = deaktiviert mit Hinweis', () => {
    expect(completeArchiveOption(null, true)).toMatchObject({ visible: false });
    expect(completeArchiveOption({ archive: { configured: false } }, true)).toMatchObject({ visible: false });
    expect(completeArchiveOption({ archive: { configured: true, reachable: true } }, false)).toMatchObject({ visible: false });
    expect(completeArchiveOption({ archive: { configured: true, reachable: true } }, true)).toMatchObject({ visible: true, enabled: true });
    const down = completeArchiveOption({ archive: { configured: true, reachable: false } }, true);
    expect(down).toMatchObject({ visible: true, enabled: false });
    expect(down.tooltip).toContain('nicht erreichbar');
  });

  it('zeigt den Fortschritt der Archivierung', () => {
    expect(archiveProgress([])).toMatchObject({ total: 0, done: false, text: 'Keine Dokumente zu archivieren.' });
    expect(archiveProgress([{ status: 'archived' }, { status: 'pending' }])).toMatchObject({ archived: 1, done: false, text: '1 von 2 Dokumenten archiviert …' });
    expect(archiveProgress([{ status: 'archived' }, { status: 'archived' }])).toMatchObject({ done: true, text: 'Alle 2 Dokumente archiviert.' });
    expect(archiveProgress([{ status: 'archived' }, { status: 'failed' }]).text).toContain('1 fehlgeschlagen');
  });
});
