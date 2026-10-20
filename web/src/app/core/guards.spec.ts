import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { AuthService } from './auth.service';
import { permissionGuard, signedInGuard } from './guards';
import { provideHttpClient } from '@angular/common/http';

describe('Guards', () => {
  let auth: AuthService;
  let router: Router;
  const state = (url: string) => ({ url }) as RouterStateSnapshot;
  const route = {} as ActivatedRouteSnapshot;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient()] });
    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
  });

  const run = (guard: typeof signedInGuard, url = '/bauakten') =>
    TestBed.runInInjectionContext(() => guard(route, state(url)));

  it('leitet ohne Anmeldung zur Anmeldung mit Rücksprungziel', () => {
    const result = run(signedInGuard, '/verwaltung/benutzer') as UrlTree;
    expect(router.serializeUrl(result)).toBe('/anmelden?ziel=%2Fverwaltung%2Fbenutzer');
  });

  it('erzwingt nach einem Einmalpasswort zuerst den Passwortwechsel', () => {
    auth.user.set({ id: '1', displayName: 'Max', userName: 'm', email: null });
    auth.mustChangePassword.set(true);
    expect(router.serializeUrl(run(signedInGuard) as UrlTree)).toBe('/konto/passwort');
    expect(run(signedInGuard, '/konto/passwort')).toBe(true);
  });

  it('zeigt Verwaltungsseiten nur mit Recht', () => {
    auth.permissions.set(new Set(['bauakten.read']));
    expect(run(permissionGuard('users.manage'))).toBeInstanceOf(UrlTree);
    auth.permissions.set(new Set(['users.manage']));
    expect(run(permissionGuard('users.manage'))).toBe(true);
  });
});
