<?php
namespace App\Services;
use App\Core\Database;
// Audit-Protokollierung wurde deaktiviert (Speicherersparnis auf dem Webspace).
// Die Klasse bleibt als No-Op erhalten, damit bestehende Aufrufe nicht brechen.
final class Audit {
  public function __construct(private Database $db) {}
  public function log(?string $tenantId, ?string $userId, string $entityType, ?string $entityId, string $action, $before=null, $after=null): void {
    // bewusst leer – es werden keine Audit-Datensätze mehr geschrieben.
  }
}
