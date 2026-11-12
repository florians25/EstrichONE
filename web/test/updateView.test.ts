import { describe, expect, it } from 'vitest';
import { clock, doneText, isUpdateState, secondsLeft, updateView, type UpdateState } from '../webapp/util/updateView';

const now = new Date('2026-11-12T10:00:00Z');
const at = (seconds: number) => new Date(now.getTime() + seconds * 1000).toISOString();
const state = (phase: UpdateState['phase'], from: number, to: number | null, extra: Partial<UpdateState> = {}): UpdateState => ({
  id: 'u1', phase, fromVersion: '1.0.15', targetVersion: '1.0.16', startedAt: at(-120), phaseStartedAt: at(from),
  phaseEndsAt: to === null ? null : at(to), ...extra,
});

describe('Update-Ablauf: Aufbereitung für Leiste und Wartungsbildschirm', () => {
  it('Countdown im Format m:ss, aufgerundet, nie negativ', () => {
    expect(clock(59.2)).toBe('1:00');
    expect(clock(59)).toBe('0:59');
    expect(clock(125)).toBe('2:05');
    expect(clock(-3)).toBe('0:00');
    expect(secondsLeft(state('announce', -1, 59), now)).toBe(59);
    expect(secondsLeft(state('update', 0, null), now)).toBeNull();
  });

  it('Ankündigung: Leiste mit Countdown, noch kein Wartungsbildschirm', () => {
    const view = updateView(state('announce', -1, 59), now);
    expect(view.banner).toBe(true);
    expect(view.page).toBe(false);
    expect(view.bannerText).toBe('Update auf Version 1.0.16 beginnt in 0:59 – bitte Arbeit speichern. Danach ist eine neue Anmeldung nötig.');
    expect(view.steps.map((s) => s.state)).toEqual(['now', 'todo', 'todo']);
    expect(view.percent).toBe(2);
  });

  it('Ankündigung abgelaufen: Wartungsbildschirm schon vor der Meldung des Servers', () => {
    const view = updateView(state('announce', -61, -1), now);
    expect(view.banner).toBe(false);
    expect(view.page).toBe(true);
    expect(view.title).toBe('Update wird eingespielt');
    expect(view.steps.map((s) => s.state)).toEqual(['done', 'now', 'todo']);
  });

  it('Update und Abschluss: Bildschirm mit Phase, Zielversion, Countdown und Fortschritt', () => {
    const update = updateView(state('update', -30, 30), now);
    expect(update.page).toBe(true);
    expect(update.title).toBe('Update wird eingespielt');
    expect(update.countdownLabel).toBe('Mindestens noch');
    expect(update.countdown).toBe('0:30');
    expect(update.percent).toBe(50);
    expect(update.version).toBe('1.0.16');
    const late = updateView(state('update', -90, -30), now);
    expect(late.countdown).toBe('');
    expect(late.countdownLabel).toBe('Läuft …');
    const finish = updateView(state('finish', -15, 45), now);
    expect(finish.title).toBe('Update wird abgeschlossen');
    expect(finish.countdownLabel).toBe('Freigabe in');
    expect(finish.countdown).toBe('0:45');
    expect(finish.steps.map((s) => s.state)).toEqual(['done', 'done', 'now']);
    expect(finish.steps[2]!.label).toBe('Abschluss');
  });

  it('Rücknahme und Fehlschlag: Meldung, gesperrt ohne Ende', () => {
    const rollback = updateView(state('rollback', -5, null), now);
    expect(rollback.title).toBe('Update wird zurückgenommen');
    expect(rollback.steps[2]!.label).toBe('Rücknahme');
    const failed = updateView(state('failed', 0, null, { message: 'Datenbank startet nicht.' }), now);
    expect(failed.failed).toBe(true);
    expect(failed.text).toBe('Datenbank startet nicht. Der Administrator ist informiert.');
    expect(failed.countdownLabel).toBe('Gesperrt, bis das Problem behoben ist');
    expect(updateView(state('failed', 0, 60), now).countdownLabel).toBe('Freigabe in');
  });

  it('erkennt gültige Stände und formuliert den Hinweis nach der Freigabe', () => {
    expect(isUpdateState(state('finish', 0, 1))).toBe(true);
    expect(isUpdateState({ id: 'x', phase: 'irgendwas' })).toBe(false);
    expect(isUpdateState(null)).toBe(false);
    expect(doneText('1.0.16', false)).toBe('EstrichONE wurde auf Version 1.0.16 aktualisiert. Bitte erneut anmelden.');
    expect(doneText('', false)).toBe('EstrichONE wurde aktualisiert. Bitte erneut anmelden.');
    expect(doneText('1.0.16', true)).toContain('bisherigen Stand');
  });
});
