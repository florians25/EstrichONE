import { HttpErrorResponse } from '@angular/common/http';
import { problemCode, problemMessage } from './problem';

describe('problemMessage', () => {
  it('nimmt die Detailmeldung des Servers', () => {
    const error = new HttpErrorResponse({ status: 401, error: { title: 'Fehler', detail: 'Anmeldename oder Passwort ist nicht korrekt.', code: 'INVALID_CREDENTIALS' } });
    expect(problemMessage(error)).toBe('Anmeldename oder Passwort ist nicht korrekt.');
    expect(problemCode(error)).toBe('INVALID_CREDENTIALS');
  });

  it('nennt den ersten Validierungsfehler', () => {
    const error = new HttpErrorResponse({ status: 422, error: { title: 'Ungültig', errors: { userName: ['Der Anmeldename fehlt.'] } } });
    expect(problemMessage(error)).toBe('Der Anmeldename fehlt.');
  });

  it('erklärt fehlende Verbindung', () => {
    expect(problemMessage(new HttpErrorResponse({ status: 0 }))).toContain('nicht erreichbar');
  });
});
