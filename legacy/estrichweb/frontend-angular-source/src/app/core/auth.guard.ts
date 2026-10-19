import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  // Gültiges Access-Token ODER vorhandenes Refresh-Token (Interceptor erneuert dann automatisch)
  if (auth.isAuthenticated() || auth.hasRefreshToken()) {
    // Erzwungener Passwortwechsel bei Erstanmeldung: alles außer der Wechsel-Seite sperren.
    if (auth.mustChangePassword && state.url !== '/passwort-aendern') {
      return router.createUrlTree(['/passwort-aendern']);
    }
    return true;
  }
  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

// Rechte-Guard: erlaubt Zugriff nur, wenn der Bereich in der Rolle enthalten ist.
export function permissionGuard(area: string): CanActivateFn {
  return () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    const snack = inject(MatSnackBar);
    if (auth.canAccessArea(area)) return true;
    snack.open('Sie haben für diese Aktion keine Berechtigung!', 'OK', { duration: 4000 });
    return router.createUrlTree(['/']);
  };
}
