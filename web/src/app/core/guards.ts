import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/** Nur angemeldet; nach einem Einmalpasswort zuerst das eigene Passwort festlegen. */
export const signedInGuard: CanActivateFn = (_, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isSignedIn()) {
    return router.createUrlTree(['/anmelden'], { queryParams: { ziel: state.url } });
  }
  if (auth.mustChangePassword() && !state.url.startsWith('/konto/passwort')) {
    return router.createUrlTree(['/konto/passwort']);
  }
  return true;
};

/** Seite nur mit Recht anzeigen; die API prüft unabhängig davon jede Anfrage selbst. */
export function permissionGuard(permission: string): CanActivateFn {
  return () => {
    const auth = inject(AuthService);
    return auth.has(permission) || inject(Router).createUrlTree(['/']);
  };
}
