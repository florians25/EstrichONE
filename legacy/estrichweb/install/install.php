<?php
/**
 * EstrichWeb – Erstinstallation / Admin-Einrichtung (Stand 2026-06-07)
 * ------------------------------------------------------------------
 * Browser:  https://DEINE-DOMAIN/install/install.php
 *
 *   1. DB-Verbindung aus api/config/local.php testen
 *   2. Struktur (schema.sql) + ALLE Migrationen (2026_*.sql) + Rollen/Rechte
 *      (seed.sql) + Performance-Indizes einspielen (fehlertolerant, idempotent)
 *   3. Ersten Mandanten + Admin-Benutzer (super_admin) anlegen
 *
 * WICHTIG: Nach erfolgreicher Installation den Ordner /install LOESCHEN.
 */
declare(strict_types=1);
error_reporting(E_ALL & ~E_DEPRECATED & ~E_NOTICE);

$root       = dirname(__DIR__);
$configFile = $root . '/api/config/local.php';
$dbDir      = $root . '/database';

function h(string $s): string { return htmlspecialchars($s, ENT_QUOTES, 'UTF-8'); }
function uuid32(): string { return bin2hex(random_bytes(16)); }

$errors = []; $messages = []; $done = false;

if (!file_exists($configFile)) {
  $errors[] = 'api/config/local.php fehlt. Bitte zuerst die DB-Zugangsdaten anlegen (siehe config.sample.php).';
}
$config = file_exists($configFile) ? require $configFile : null;

$pdo = null;
if ($config && empty($errors)) {
  try {
    $db = $config['db'];
    $dsn = "mysql:host={$db['host']};dbname={$db['name']};charset=" . ($db['charset'] ?? 'utf8mb4');
    $pdo = new PDO($dsn, $db['user'], $db['pass'], [
      PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
      PDO::ATTR_EMULATE_PREPARES => false,
    ]);
  } catch (Throwable $e) {
    $errors[] = 'DB-Verbindung fehlgeschlagen: ' . $e->getMessage();
  }
}

/**
 * Fuehrt eine SQL-Datei statementweise aus. DELIMITER-Bloecke (gespeicherte
 * Prozeduren) werden als ein Statement an den Server geschickt; einfache
 * Statements per ';' getrennt. Fehler werden ignoriert (idempotent/tolerant).
 */
function run_sql_file(PDO $pdo, string $path): void {
  if (!file_exists($path)) return;
  $raw = file_get_contents($path);
  // Zeilenkommentare entfernen
  $raw = preg_replace('/^\s*--.*$/m', '', $raw);

  // DELIMITER-Bloecke separat behandeln
  $chunks = preg_split('/\bDELIMITER\s+(\S+)/', $raw, -1, PREG_SPLIT_DELIM_CAPTURE);
  $delim = ';';
  $buffer = [];
  for ($i = 0; $i < count($chunks); $i++) {
    if ($i % 2 === 1) { $delim = trim($chunks[$i]); continue; }
    $segment = $chunks[$i];
    if ($delim === ';') {
      foreach (explode(';', $segment) as $stmt) {
        $stmt = trim($stmt);
        if ($stmt !== '') { try { $pdo->exec($stmt); } catch (Throwable $e) {} }
      }
    } else {
      foreach (explode($delim, $segment) as $stmt) {
        $stmt = trim($stmt);
        if ($stmt !== '') { try { $pdo->exec($stmt); } catch (Throwable $e) {} }
      }
      $delim = ';';
    }
  }
}

if ($_SERVER['REQUEST_METHOD'] === 'POST' && $pdo && empty($errors)) {
  $company = trim($_POST['company'] ?? '');
  $name    = trim($_POST['admin_name'] ?? '');
  $email   = strtolower(trim($_POST['admin_email'] ?? ''));
  $pass    = (string)($_POST['admin_password'] ?? '');
  if ($company === '' || $name === '' || $email === '' || strlen($pass) < 8) {
    $errors[] = 'Bitte alle Felder ausfuellen (Passwort mind. 8 Zeichen).';
  }

  if (empty($errors)) {
    try {
      // 1) Struktur
      run_sql_file($pdo, $dbDir . '/schema.sql');
      // 2) Alle Migrationen in chronologischer Reihenfolge
      $migrations = glob($dbDir . '/2026_*.sql') ?: [];
      sort($migrations);
      foreach ($migrations as $m) {
        // die grossen Komplett-Dumps ueberspringen (separat)
        if (strpos(basename($m), 'complete') !== false) continue;
        run_sql_file($pdo, $m);
      }
      // 3) Rollen & Rechte
      run_sql_file($pdo, $dbDir . '/seed.sql');
      // 4) Performance-Indizes
      run_sql_file($pdo, $dbDir . '/2026_06_07_performance_indexes.sql');

      // 5) Mandant + Admin
      $existing = (int)$pdo->query("SELECT COUNT(*) FROM users")->fetchColumn();
      if ($existing > 0) {
        $errors[] = 'Es existiert bereits mindestens ein Benutzer. Der Installer bricht zur Sicherheit ab.';
      } else {
        $now = (new DateTime())->format('Y-m-d H:i:s');
        $tenantId = uuid32();
        $slug = preg_replace('/[^a-z0-9]+/', '-', strtolower($company)) ?: 'estrichweb';
        $pdo->prepare("INSERT INTO tenants (id,slug,name,email,plan,created_at,updated_at) VALUES (?,?,?,?, 'enterprise', ?, ?)")
            ->execute([$tenantId, substr($slug, 0, 80), $company, $email, $now, $now]);

        $userId = uuid32();
        $hash = password_hash($pass, PASSWORD_DEFAULT);
        // role_hint optional; Tabelle hat die Spalte in aktuellen Schemas
        try {
          $pdo->prepare("INSERT INTO users (id,tenant_id,name,email,password_hash,is_active,role_hint,created_at,updated_at) VALUES (?,?,?,?,?,1,'Administrator',?,?)")
              ->execute([$userId, $tenantId, $name, $email, $hash, $now, $now]);
        } catch (Throwable $e) {
          $pdo->prepare("INSERT INTO users (id,tenant_id,name,email,password_hash,is_active,created_at,updated_at) VALUES (?,?,?,?,?,1,?,?)")
              ->execute([$userId, $tenantId, $name, $email, $hash, $now, $now]);
        }

        // hoechste Admin-Rolle waehlen (oder anlegen) und GARANTIERT mit '*' verknuepfen
        $roleId = $pdo->query("SELECT id FROM roles WHERE code IN ('super_admin','firmenadmin') ORDER BY FIELD(code,'super_admin','firmenadmin') LIMIT 1")->fetchColumn();
        if (!$roleId) {
          $roleId = uuid32();
          $pdo->prepare("INSERT INTO roles (id,tenant_id,name,code,description,is_system,created_at,updated_at) VALUES (?,?, 'Administrator','super_admin','Vollzugriff',1,?,?)")
              ->execute([$roleId, $tenantId, $now, $now]);
        }
        // '*'-Recht sicherstellen und mit der Admin-Rolle verknuepfen
        $permId = $pdo->query("SELECT id FROM permissions WHERE code='*' LIMIT 1")->fetchColumn();
        if (!$permId) {
          $permId = md5('*');
          $pdo->prepare("INSERT INTO permissions (id,code,label,scope,created_at) VALUES (?, '*','Vollzugriff (alle Bereiche)','tenant',?)")
              ->execute([$permId, $now]);
        }
        $pdo->prepare("INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES (?,?)")->execute([$roleId, $permId]);
        // Rolle dem Admin zuweisen
        $pdo->prepare("INSERT IGNORE INTO user_roles (user_id,role_id) VALUES (?,?)")->execute([$userId, $roleId]);
        $done = true;
        $messages[] = 'Installation abgeschlossen. Mandant und Administrator wurden angelegt.';
      }
    } catch (Throwable $e) {
      $errors[] = 'Installationsfehler: ' . $e->getMessage();
    }
  }
}
?><!doctype html>
<html lang="de"><head>
<meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>EstrichWeb – Installation</title>
<style>
  :root{--blue:#1f3a8a;--line:#e2e8f0;--bg:#f1f5f9;--text:#0f172a;}
  *{box-sizing:border-box;}
  body{font-family:system-ui,Segoe UI,Roboto,sans-serif;background:var(--bg);color:var(--text);margin:0;padding:32px;}
  .card{max-width:560px;margin:0 auto;background:#fff;border:1px solid var(--line);border-radius:16px;padding:28px;box-shadow:0 10px 30px rgba(2,6,23,.06);}
  h1{margin:0 0 4px;font-size:1.5rem;} .sub{color:#64748b;margin:0 0 20px;}
  label{display:block;font-weight:700;font-size:.85rem;margin:14px 0 6px;}
  input{width:100%;padding:11px 12px;border:1px solid var(--line);border-radius:10px;font-size:1rem;}
  button{margin-top:22px;width:100%;padding:13px;border:0;border-radius:10px;background:var(--blue);color:#fff;font-weight:800;font-size:1rem;cursor:pointer;}
  .msg{padding:12px 14px;border-radius:10px;margin:8px 0;font-size:.92rem;}
  .ok{background:#ecfdf5;color:#065f46;border:1px solid #a7f3d0;}
  .err{background:#fef2f2;color:#991b1b;border:1px solid #fecaca;}
  .note{background:#fff7ed;color:#7c2d12;border:1px solid #fed7aa;margin-top:18px;}
  code{background:#f1f5f9;padding:2px 6px;border-radius:6px;}
</style></head>
<body><div class="card">
  <h1>EstrichWeb – Installation</h1>
  <p class="sub">Datenbank einrichten und ersten Administrator anlegen.</p>

  <?php foreach ($messages as $m): ?><div class="msg ok"><?= h($m) ?></div><?php endforeach; ?>
  <?php foreach ($errors as $e): ?><div class="msg err"><?= h($e) ?></div><?php endforeach; ?>

  <?php if ($done): ?>
    <div class="msg ok"><b>Fertig.</b> Du kannst dich jetzt mit E-Mail und Passwort anmelden.</div>
    <div class="msg note"><b>Sicherheit:</b> Bitte lösche jetzt den Ordner <code>/install</code> vom Server.</div>
  <?php elseif ($pdo && empty($errors)): ?>
    <form method="post" autocomplete="off">
      <label>Firma / Mandant</label>
      <input name="company" required value="<?= h($_POST['company'] ?? '') ?>" placeholder="z. B. Mustermann Estrich GmbH">
      <label>Name des Administrators</label>
      <input name="admin_name" required value="<?= h($_POST['admin_name'] ?? '') ?>" placeholder="Vor- und Nachname">
      <label>E-Mail (Login)</label>
      <input name="admin_email" type="email" required value="<?= h($_POST['admin_email'] ?? '') ?>" placeholder="admin@firma.de">
      <label>Passwort (mind. 8 Zeichen)</label>
      <input name="admin_password" type="password" required minlength="8" placeholder="••••••••">
      <button type="submit">Installieren &amp; Admin anlegen</button>
    </form>
  <?php endif; ?>
</div></body></html>
