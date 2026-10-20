import { afterEach, describe, expect, it, vi } from 'vitest';
import { api, ApiError, onSessionEnded, problemMessage } from './client';

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

  it('erklärt Netzwerkfehler', () => {
    expect(problemMessage(new TypeError('Failed to fetch'))).toContain('nicht erreichbar');
  });
});
