import { describe, expect, it } from 'vitest';
import { addressStrip } from '../webapp/util/health';

describe('Adresshinweis für Administratoren', () => {
  it('meldet eine abweichende Domain mit dem passenden Installer-Aufruf', () => {
    const strip = addressStrip({ configuredUrl: 'https://estrichone.deinefirma.de', requestUrl: 'https://app.estrichone.de', matches: false, https: true }, 'https://app.estrichone.de');
    expect(strip?.type).toBe('Warning');
    expect(strip?.text).toContain('Konfigurierte Adresse weicht ab');
    expect(strip?.text).toContain('https://estrichone.deinefirma.de');
    expect(strip?.text).toContain('-e ESTRICHONE_DOMAIN=app.estrichone.de');
  });

  it('warnt bei unverschlüsseltem Aufruf über IP und Port, nicht bei localhost', () => {
    const check = { configuredUrl: 'https://app.estrichone.de', requestUrl: 'http://192.168.1.10:8080', matches: true, https: false };
    expect(addressStrip(check, 'http://192.168.1.10:8080')?.text).toContain('Unverschlüsselte Verbindung');
    expect(addressStrip(check, 'http://localhost:8080')).toBeNull();
  });

  it('schweigt, wenn alles passt', () => {
    expect(addressStrip({ configuredUrl: 'https://app.estrichone.de', requestUrl: 'https://app.estrichone.de', matches: true, https: true }, 'https://app.estrichone.de')).toBeNull();
    expect(addressStrip(null, 'https://app.estrichone.de')).toBeNull();
  });
});
