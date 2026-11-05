import { describe, expect, it } from 'vitest';
import { startInputHints } from '../webapp/util/inputHints';
import { navKeyFor } from '../webapp/util/routes';

const tick = () => new Promise((resolve) => setTimeout(resolve, 0));

describe('Browser-Hinweise für Eingabefelder', () => {
  it('überträgt autocomplete und Rolle, auch nach Neuzeichnen', async () => {
    document.body.innerHTML = '';
    startInputHints();
    const host = document.createElement('div');
    host.setAttribute('data-autocomplete', 'current-password');
    host.innerHTML = '<input type="password" autocomplete="off">';
    const strip = document.createElement('div');
    strip.setAttribute('data-role', 'alert');
    strip.setAttribute('role', 'note');
    document.body.append(host, strip);
    await tick();
    expect(host.querySelector('input')?.getAttribute('autocomplete')).toBe('current-password');
    expect(strip.getAttribute('role')).toBe('alert');

    // UI5 zeichnet das innere Feld neu und setzt wieder autocomplete="off".
    host.innerHTML = '<input type="password" autocomplete="off">';
    await tick();
    expect(host.querySelector('input')?.getAttribute('autocomplete')).toBe('current-password');
    host.querySelector('input')?.setAttribute('autocomplete', 'off');
    await tick();
    expect(host.querySelector('input')?.getAttribute('autocomplete')).toBe('current-password');
  });
});

describe('Navigation', () => {
  it('Altakten als eigener Eintrag unter /bauakten?status=old', () => {
    expect(navKeyFor('bauakten', new URLSearchParams('status=old'))).toBe('oldFiles');
    expect(navKeyFor('bauakten', new URLSearchParams('status=all'))).toBe('bauakten');
  });
});
