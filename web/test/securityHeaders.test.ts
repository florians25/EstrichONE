import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// Sicherheitsheader der Weboberfläche (deploy/Caddyfile) und security.txt (RFC 9116) – docs/sicherheit.md, Abschnitt App.
const caddyfile = readFileSync(resolve(process.cwd(), '../deploy/Caddyfile'), 'utf8');
const header = (name: string) => caddyfile.match(new RegExp(`${name} "([^"]+)"`))?.[1] ?? '';

describe('Sicherheitsheader', () => {
  it('CSP ohne unsafe-eval und Inline-Skripte, ohne Einbetten in fremde Seiten', () => {
    const csp = header('Content-Security-Policy');
    expect(csp).toMatch(/^default-src 'self'/);
    expect(csp).toContain("script-src 'self';");
    expect(csp).not.toContain('unsafe-eval');
    expect(csp).not.toMatch(/script-src[^;]*unsafe-inline/);
    expect(csp).toContain("frame-ancestors 'none'");
    expect(csp).toContain("object-src 'none'");
    expect(csp).toContain("base-uri 'self'");
  });

  it('Permissions-Policy nur für Kamera, Mikrofon, Standort (eigene Seite)', () => {
    const policy = header('Permissions-Policy');
    expect(policy).toContain('camera=(self)');
    expect(policy).toContain('payment=()');
    expect(policy).toContain('usb=()');
    expect(header('X-Frame-Options')).toBe('DENY');
    expect(header('Cross-Origin-Opener-Policy')).toBe('same-origin');
  });

  it('security.txt mit Kontakt und gültigem Ablaufdatum', () => {
    const text = readFileSync(resolve(process.cwd(), 'public/.well-known/security.txt'), 'utf8');
    expect(text).toMatch(/^Contact: https:\/\//m);
    const expires = Date.parse(text.match(/^Expires: (.+)$/m)?.[1] ?? '');
    expect(expires).toBeGreaterThan(Date.now());
    expect(expires).toBeLessThan(Date.now() + 400 * 24 * 3600 * 1000); // höchstens ein Jahr im Voraus (RFC 9116)
  });
});
