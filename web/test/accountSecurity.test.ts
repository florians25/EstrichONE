import { describe, expect, it } from 'vitest';
import { groupSecret, sessionLabel } from '../webapp/util/accountSecurity';

describe('Anmeldung und Sicherheit', () => {
  it('Schlüssel in Vierergruppen zum Abtippen', () => {
    expect(groupSecret('JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP')).toBe('JBSW Y3DP EHPK 3PXP JBSW Y3DP EHPK 3PXP');
  });

  it('Sitzungen verständlich benannt', () => {
    expect(sessionLabel({ client: 'web', clientName: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0 Safari/537.36 Edg/141.0' }))
      .toBe('Browser (Edge auf Windows)');
    expect(sessionLabel({ client: 'web', clientName: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 14_5) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15' }))
      .toBe('Browser (Safari auf macOS)');
    expect(sessionLabel({ client: 'ios', clientName: 'iPhone von Max' })).toBe('iPhone/iPad-App · iPhone von Max');
    expect(sessionLabel({ client: 'desktop', clientName: null })).toBe('Windows-App');
  });
});

describe('Sitzungen auf Mobilgeräten', () => {
  it('iPhone-Browser nicht als macOS', () => {
    expect(sessionLabel({ client: 'web', clientName: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1' }))
      .toBe('Browser (Safari auf iOS)');
  });
});
