import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { vi } from 'vitest';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('authInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let auth: AuthService;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
  });

  afterEach(() => backend.verify());

  it('erneuert bei abgelaufenem Zugriffstoken genau einmal und wiederholt alle Anfragen', async () => {
    const first = firstValueFrom(http.get('/api/v1/bauakten'));
    const second = firstValueFrom(http.get('/api/v1/devices'));

    backend.expectOne('/api/v1/bauakten').flush({ code: 'ACCESS_TOKEN_EXPIRED' }, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/api/v1/devices').flush({ code: 'ACCESS_TOKEN_EXPIRED' }, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/api/v1/auth/web/refresh').flush({ user: { id: '1' }, mustChangePassword: false });
    backend.expectOne('/api/v1/bauakten').flush({ items: [] });
    backend.expectOne('/api/v1/devices').flush([]);

    await expect(first).resolves.toEqual({ items: [] });
    await expect(second).resolves.toEqual([]);
  });

  it('meldet bei beendeter Sitzung ab und führt zur Anmeldung', async () => {
    auth.user.set({ id: '1', displayName: 'Max', userName: 'm', email: null });
    const request = firstValueFrom(http.get('/api/v1/bauakten'));

    backend.expectOne('/api/v1/bauakten').flush({ code: 'USER_DISABLED' }, { status: 401, statusText: 'Unauthorized' });

    await expect(request).rejects.toBeTruthy();
    expect(auth.user()).toBeNull();
    expect(router.navigate).toHaveBeenCalledWith(['/anmelden'], { queryParams: { grund: 'gesperrt' } });
  });

  it('scheitert die Erneuerung, geht es zur Anmeldung', async () => {
    const request = firstValueFrom(http.get('/api/v1/bauakten'));

    backend.expectOne('/api/v1/bauakten').flush({ code: 'ACCESS_TOKEN_EXPIRED' }, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/api/v1/auth/web/refresh').flush({ code: 'SESSION_EXPIRED' }, { status: 401, statusText: 'Unauthorized' });

    await expect(request).rejects.toBeTruthy();
    expect(router.navigate).toHaveBeenCalledWith(['/anmelden'], { queryParams: { grund: 'abgelaufen' } });
  });

  it('lässt andere Fehler unverändert durch', async () => {
    const request = firstValueFrom(http.get('/api/v1/users'));
    backend.expectOne('/api/v1/users').flush({ code: 'PERMISSION_DENIED' }, { status: 403, statusText: 'Forbidden' });
    await expect(request).rejects.toMatchObject({ status: 403 });
    expect(router.navigate).not.toHaveBeenCalled();
  });
});
