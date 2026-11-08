import { describe, expect, it } from 'vitest';
import { updateHint, type UpdateStatus } from '../webapp/util/updateStatus';

const base: UpdateStatus = {
  installed: '0.1.0-preview.23', available: null, securityUpdate: false, latest: '0.1.0-preview.23', source: 'feed',
  command: 'sudo /opt/estrichone/update.sh', releaseUrl: null, auto: null,
};

describe('Hinweis Sicherheitsupdate', () => {
  it('zeigt nichts, wenn aktuell', () => {
    expect(updateHint(base)).toBeNull();
    expect(updateHint(null)).toBeNull();
  });

  it('meldet Sicherheitsupdates rot mit Befehl und Automatik', () => {
    const hint = updateHint({ ...base, available: '0.1.0-preview.25', securityUpdate: true, command: 'sudo /opt/estrichone/update.sh 0.1.0-preview.25',
      releaseUrl: 'https://github.com/x', auto: { mode: 'sicherheit', window: '02:00-05:00' } });
    expect(hint).toMatchObject({ type: 'Error', key: 'security:0.1.0-preview.25', url: 'https://github.com/x' });
    expect(hint!.text).toBe('Sicherheitsupdate verfügbar: EstrichONE 0.1.0-preview.25 (installiert 0.1.0-preview.23). Bitte zeitnah einspielen. Befehl auf dem Server: sudo /opt/estrichone/update.sh 0.1.0-preview.25. Wird automatisch im Zeitfenster 02:00-05:00 eingespielt.');
  });

  it('normale Updates als Information, Automatik „sicherheit“ spielt sie nicht ein', () => {
    const hint = updateHint({ ...base, available: '0.1.0-preview.24', command: 'sudo /opt/estrichone/update.sh 0.1.0-preview.24', auto: { mode: 'sicherheit', window: '02:00-05:00' } });
    expect(hint).toMatchObject({ type: 'Information', key: 'update:0.1.0-preview.24' });
    expect(hint!.text).not.toContain('automatisch');
    expect(updateHint({ ...base, available: '0.1.0-preview.24', auto: { mode: 'patch', window: '01:00-03:00' } })!.text).toContain('Zeitfenster 01:00-03:00');
  });

  it('warnt bei fehlgeschlagener Automatik', () => {
    expect(updateHint({ ...base, auto: { mode: 'patch', window: '02:00-05:00', result: 'failed', lastAt: '2026-11-03T02:10:00Z', message: 'Dienste nicht bereit' } }))
      .toMatchObject({ type: 'Warning', text: 'Automatisches Update fehlgeschlagen: Dienste nicht bereit.' });
  });
});
