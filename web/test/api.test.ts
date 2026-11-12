import { afterEach, describe, expect, it, vi } from 'vitest';
import { api, ApiError, NetworkError, onPasswordChangeRequired, onSessionEnded, problemMessage, setRetryDelay } from '../webapp/util/api';
import { uploadError } from '../webapp/util/upload';

setRetryDelay(1);
document.cookie = 'XSRF-TOKEN=test-token';

const problem = (status: number, body: object) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/problem+json' } });

afterEach(() => {
  vi.unstubAllGlobals();
  onSessionEnded(() => undefined);
  onPasswordChangeRequired(() => undefined);
});

describe('api', () => {
  it('liefert die erste Validierungsmeldung als verständlichen Fehler', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(problem(400, { title: 'Ungültig', errors: { name: ['Bitte den Namen angeben.'] } })));
    const error = await api('contacts').catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect(problemMessage(error)).toBe('Bitte den Namen angeben.');
  });

  it('ersetzt englische Standardtitel des Servers durch deutsche Meldungen', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(problem(404, { title: 'Not Found', status: 404 })));
    const error = await api('tickets/x').catch((e: unknown) => e);
    expect(problemMessage(error)).toMatch(/^Nicht gefunden/);
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(problem(409, { title: 'Bauakte gesperrt' })));
    expect(problemMessage(await api('bauakten/x').catch((e: unknown) => e))).toBe('Bauakte gesperrt');
  });

  it('zeigt bei Fehlerseiten vorgeschalteter Proxys (Cloudflare, Caddy) eine deutsche Meldung statt des englischen Textes', async () => {
    const cloudflare = 'The origin web server returned an invalid or incomplete response to Cloudflare. This typically indicates the origin is overloaded or misconfigured.';
    // Cloudflare liefert bei Accept: application/json eigenes JSON (ohne EstrichONE-Code) …
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ title: 'Bad gateway', status: 502, detail: cloudflare }),
      { status: 502, headers: { 'Content-Type': 'application/json' } })));
    let message = problemMessage(await api('settings/integrations/teams/users/match', { method: 'POST' }).catch((e: unknown) => e));
    expect(message).toMatch(/^Der Server hat nicht rechtzeitig oder nur unvollständig geantwortet – bitte erneut versuchen/);
    expect(message).toContain('Server-Log');
    expect(message).not.toContain('Cloudflare');
    // … oder HTML bzw. Text (504, 520–524).
    for (const status of [504, 520, 524]) {
      vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(`<html>${cloudflare}</html>`, { status, headers: { 'Content-Type': 'text/html' } })));
      message = problemMessage(await api('x', { method: 'POST' }).catch((e: unknown) => e));
      expect(message).toMatch(/^Der Server hat nicht rechtzeitig/);
    }
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('error code: 502', { status: 502, headers: { 'Content-Type': 'text/plain' } })));
    expect(problemMessage(await api('x', { method: 'POST' }).catch((e: unknown) => e))).toMatch(/^Der Server hat nicht rechtzeitig/);
  });

  it('behält die Meldungen eigener Probleme (mit Code) auch bei 5xx', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(problem(503, {
      title: 'Microsoft 365 antwortet nicht rechtzeitig', code: 'TEAMS_TIMEOUT', detail: 'Microsoft 365 antwortet nicht rechtzeitig (graph.microsoft.com).',
    })));
    const error = await api('settings/integrations/teams/users/match', { method: 'POST' }).catch((e: unknown) => e);
    expect((error as ApiError).code).toBe('TEAMS_TIMEOUT');
    expect(problemMessage(error)).toBe('Microsoft 365 antwortet nicht rechtzeitig (graph.microsoft.com).');
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(problem(424, { code: 'TEAMS_FORBIDDEN', detail: 'Microsoft verweigert den Zugriff – es fehlt die Berechtigung „User.Read.All“.' })));
    expect(problemMessage(await api('x', { method: 'POST' }).catch((e: unknown) => e))).toContain('User.Read.All');
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(problem(500, { title: 'Interner Serverfehler', code: 'INTERNAL_ERROR', detail: 'Bitte die traceId melden.' })));
    expect(problemMessage(await api('x', { method: 'POST' }).catch((e: unknown) => e))).toBe('Bitte die traceId melden.');
  });

  it('Hochladen: Proxy-Fehlerseite ergibt die deutsche Meldung', () => {
    expect(uploadError(502, { detail: 'The origin web server returned an invalid response.' }).message).not.toContain('origin');
    expect(uploadError(422, { detail: 'Dateityp nicht erlaubt.', code: 'VALIDATION_FAILED' }).message).toBe('Dateityp nicht erlaubt.');
  });

  it('führt bei PASSWORD_CHANGE_REQUIRED zum Passwortwechsel, ohne die Sitzung zu beenden', async () => {
    const required = vi.fn();
    const ended = vi.fn();
    onPasswordChangeRequired(required);
    onSessionEnded(ended);
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(problem(403, { code: 'PASSWORD_CHANGE_REQUIRED', detail: 'Bitte zuerst ein eigenes Passwort festlegen.' })));

    const error = await api('bauakten').catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).code).toBe('PASSWORD_CHANGE_REQUIRED');
    expect(required).toHaveBeenCalledTimes(1);
    expect(ended).not.toHaveBeenCalled();

    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(problem(403, { code: 'PERMISSION_DENIED' })));
    await api('bauakten').catch(() => undefined);
    expect(required).toHaveBeenCalledTimes(1);
  });

  it('meldet eine beendete Sitzung, außer bei der stillen Prüfung beim Start', async () => {
    const ended = vi.fn();
    onSessionEnded(ended);
    vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(problem(401, { code: 'UNAUTHENTICATED' }))));

    await expect(api('auth/me', { quiet: true })).rejects.toBeInstanceOf(ApiError);
    expect(ended).not.toHaveBeenCalled();

    await expect(api('bauakten')).rejects.toBeInstanceOf(ApiError);
    expect(ended).toHaveBeenCalledWith('expired');
  });

  it('erneuert ein abgelaufenes Zugriffstoken einmal und wiederholt die Anfrage', async () => {
    const fetch = vi.fn()
      .mockResolvedValueOnce(problem(401, { code: 'ACCESS_TOKEN_EXPIRED' }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ok: true }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    vi.stubGlobal('fetch', fetch);
    await expect(api<{ ok: boolean }>('bauakten')).resolves.toEqual({ ok: true });
    expect(String(fetch.mock.calls[1][0])).toContain('/api/v1/auth/web/refresh');
  });

  it('erneuert auch, wenn das Zugriffs-Cookie schon abgelaufen ist (Code UNAUTHORIZED)', async () => {
    const fetch = vi.fn()
      .mockResolvedValueOnce(problem(401, { code: 'UNAUTHORIZED' }))
      .mockResolvedValueOnce(new Response(null, { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify([1]), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    vi.stubGlobal('fetch', fetch);
    await expect(api<number[]>('bauakten')).resolves.toEqual([1]);
  });

  it('wiederholt die Anfrage nach erneuter Anmeldung im Dialog (keine verlorenen Eingaben)', async () => {
    onSessionEnded(() => Promise.resolve(true));
    const fetch = vi.fn()
      .mockResolvedValueOnce(problem(401, { code: 'UNAUTHORIZED' }))
      .mockResolvedValueOnce(problem(401, { code: 'SESSION_EXPIRED' }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ saved: true }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    vi.stubGlobal('fetch', fetch);
    await expect(api('notes', { method: 'POST', body: {} })).resolves.toEqual({ saved: true });
    expect(fetch).toHaveBeenCalledTimes(3);
  });

  it('wiederholt lesende Anfragen nach kurzen Aussetzern, schreibende nicht', async () => {
    const ok = () => new Response(JSON.stringify({ ok: 1 }), { status: 200, headers: { 'Content-Type': 'application/json' } });
    vi.stubGlobal('fetch', vi.fn().mockRejectedValueOnce(new TypeError('Failed to fetch')).mockResolvedValueOnce(new Response('', { status: 502 })).mockResolvedValueOnce(ok()));
    await expect(api('bauakten')).resolves.toEqual({ ok: 1 });
    vi.stubGlobal('fetch', vi.fn().mockRejectedValueOnce(new TypeError('Failed to fetch')).mockResolvedValueOnce(ok()));
    await expect(api('notes', { method: 'POST', body: {} })).rejects.toBeInstanceOf(NetworkError);
  });

  it('erklärt Netzwerkfehler und verwechselt Programmfehler nicht damit', () => {
    expect(problemMessage(new NetworkError())).toContain('nicht erreichbar');
    expect(problemMessage(new TypeError("Cannot read properties of undefined (reading 'x')"))).toContain('unerwarteter Fehler');
  });

  it('nennt beim Hochladen die Ursache statt „nicht erreichbar“', () => {
    expect(uploadError(413, {}).message).toContain('client_max_body_size');
    expect(uploadError(0, {}).message).toContain('unterbrochen');
    expect(uploadError(0, { code: 'TIMEOUT' }).message).toContain('Zeitüberschreitung');
    expect(uploadError(422, { detail: 'Dateityp nicht erlaubt.' }).message).toBe('Dateityp nicht erlaubt.');
  });
});
