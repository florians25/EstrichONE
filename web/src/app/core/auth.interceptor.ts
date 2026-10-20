import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, finalize, shareReplay, switchMap, throwError } from 'rxjs';
import { Problem } from './api.models';
import { AuthService } from './auth.service';

let refreshing: Observable<unknown> | null = null;

/**
 * Abgelaufenes Zugriffstoken (10 Minuten): einmal erneuern – auch bei parallelen Anfragen nur einmal – und die
 * Anfrage wiederholen. Endet die Sitzung, geht es zur Anmeldung.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const isAuthCall = request.url.includes('/api/v1/auth/web/');

  return next(request).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401 || isAuthCall) {
        return throwError(() => error);
      }
      const code = (error.error as Problem | null)?.code;
      if (code !== 'ACCESS_TOKEN_EXPIRED') {
        auth.clear();
        void router.navigate(['/anmelden'], { queryParams: { grund: code === 'USER_DISABLED' ? 'gesperrt' : 'abgelaufen' } });
        return throwError(() => error);
      }
      refreshing ??= auth.refresh().pipe(
        finalize(() => (refreshing = null)),
        shareReplay(1),
      );
      return refreshing.pipe(
        switchMap(() => next(request)),
        catchError((refreshError: unknown) => {
          auth.clear();
          void router.navigate(['/anmelden'], { queryParams: { grund: 'abgelaufen' } });
          return throwError(() => refreshError);
        }),
      );
    }),
  );
};
