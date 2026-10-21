import { afterEach, describe, expect, it, vi } from 'vitest';
import { api, ApiError, NetworkError, onSessionEnded, problemMessage, setRetryDelay } from '../webapp/util/api';
import { uploadError } from '../webapp/util/upload';

setRetryDelay(1);
document.cookie = 'XSRF-TOKEN=test-token';

const problem = (status: number, body: object) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/problem+json' } });

afterEach(() => {
  vi.unstubAllGlobals();
  onSessionEnded(() => undefined);
});

describe('api', () => {
  it('liefert die erste Validierungsmeldung als verständlichen Fehler', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(problem(400, { title: 'Ungültig', errors: { name: ['Bitte den Namen angeben.'] } })));
    const error = await api('contacts').catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect(problemMessage(error)).toBe('Bitte den Namen angeben.');
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
