import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MAX_TOASTS, showToast, toastDuration, toastType } from '../webapp/util/toast';

describe('Art der Kurzmeldung aus dem Text', () => {
  it('Erfolg, Hinweis, Warnung, Fehler', () => {
    expect(toastType('Gespeichert')).toBe('success');
    expect(toastType('Kunde angelegt (Kd.-Nr. 600001)')).toBe('success');
    expect(toastType('Vermerkt: Keine Erstbegehung erforderlich')).toBe('success');
    expect(toastType('PDF wird erstellt …')).toBe('info');
    expect(toastType('Rückgängig: Einsatz verschoben')).toBe('info');
    expect(toastType('Bitte zuerst Kunden in der Liste auswählen (Häkchen).')).toBe('warning');
    expect(toastType('Diesen Termin dürfen Sie nicht ändern.')).toBe('warning');
    expect(toastType('Kopieren ist in diesem Browser nicht möglich.')).toBe('warning');
    expect(toastType('Verschoben. Konflikt: Kolonne doppelt belegt')).toBe('warning');
    expect(toastType('Fehler beim Hochladen')).toBe('error');
  });

  it('Dauer nach Länge, mindestens 3,5 s, höchstens 8 s, Warnungen länger', () => {
    expect(toastDuration('Kopiert')).toBe(3500);
    expect(toastDuration('Kopiert', 'warning')).toBe(5000);
    expect(toastDuration('x'.repeat(300))).toBe(8000);
  });
});

describe('Kurzmeldung im DOM', () => {
  beforeEach(() => { vi.useFakeTimers(); document.body.innerHTML = ''; });
  afterEach(() => { vi.useRealTimers(); });

  it('Region mit aria-live, Symbol, Text, Rolle; blendet nach der Dauer aus, pausiert bei Zeiger', () => {
    const toast = showToast('Gespeichert', { duration: 1000 });
    const host = document.getElementById('eoToasts')!;
    expect(host.getAttribute('aria-live')).toBe('polite');
    expect(toast.classList.contains('eoToast-success')).toBe(true);
    expect(toast.getAttribute('role')).toBe('status');
    expect(toast.textContent).toContain('Gespeichert');
    expect(toast.querySelector('svg')).not.toBeNull();
    toast.dispatchEvent(new Event('mouseenter'));
    vi.advanceTimersByTime(5000);
    expect(toast.classList.contains('eoToastLeave')).toBe(false);
    toast.dispatchEvent(new Event('mouseleave'));
    vi.advanceTimersByTime(1000);
    expect(toast.classList.contains('eoToastLeave')).toBe(true);
    vi.advanceTimersByTime(500);
    expect(host.contains(toast)).toBe(false);
  });

  it('Klick schließt, Aktion löst aus und schließt, Warnung als alert', () => {
    const press = vi.fn();
    const toast = showToast('Einsatz verschoben', { action: { text: 'Rückgängig', press } });
    toast.querySelector<HTMLButtonElement>('.eoToastAction')!.click();
    expect(press).toHaveBeenCalledOnce();
    expect(toast.classList.contains('eoToastLeave')).toBe(true);
    const other = showToast('Bitte einen Empfänger eingeben.');
    expect(other.getAttribute('role')).toBe('alert');
    other.click();
    expect(other.classList.contains('eoToastLeave')).toBe(true);
  });

  it('stapelt höchstens MAX_TOASTS Meldungen, ältere weichen', () => {
    const all = Array.from({ length: MAX_TOASTS + 2 }, (_, i) => showToast(`Meldung ${i}`));
    expect(all.slice(0, 2).every((t) => t.classList.contains('eoToastLeave'))).toBe(true);
    expect(all.slice(2).every((t) => !t.classList.contains('eoToastLeave'))).toBe(true);
  });
});
