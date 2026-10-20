import { HttpErrorResponse } from '@angular/common/http';
import { Problem } from './api.models';

/** Verständliche Meldung aus einer Fehlerantwort (05, Abschnitt 6: Was ist passiert, was kann ich tun?). */
export function problemMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) {
      return 'Der Server ist nicht erreichbar. Bitte die Verbindung prüfen und erneut versuchen.';
    }
    const problem = error.error as Problem | null;
    if (problem?.errors) {
      const first = Object.values(problem.errors).flat()[0];
      if (first) {
        return first;
      }
    }
    return problem?.detail ?? problem?.title ?? `Die Anfrage ist fehlgeschlagen (Status ${error.status}).`;
  }
  return 'Ein unerwarteter Fehler ist aufgetreten.';
}

export function problemCode(error: unknown): string | undefined {
  return error instanceof HttpErrorResponse ? (error.error as Problem | null)?.code : undefined;
}
