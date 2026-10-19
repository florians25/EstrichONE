<?php
/**
 * EstrichWeb – Altlasten endgueltig entfernen
 * ----------------------------------------------------------------------------
 * Bis zur Umstellung wurden geloeschte Datensaetze nur mit `deleted_at`
 * markiert und blieben in der Datenbank stehen. Dieses Werkzeug entfernt sie
 * endgueltig – samt der zugehoerigen Dateien in der Ablage.
 *
 * AUFRUF (auf dem Server, im Verzeichnis api/tools/):
 *
 *     php purge_deleted.php            zeigt nur an, was entfernt WUERDE
 *     php purge_deleted.php --force    entfernt tatsaechlich
 *
 * Ohne --force wird NICHTS geaendert. Bitte zuerst ohne, dann mit.
 *
 * WICHTIG: Vor dem Lauf mit --force eine Datensicherung anlegen. Was hier
 * entfernt wird, ist nicht wiederherstellbar.
 */

declare(strict_types=1);

$root = dirname(__DIR__);
$configFile = $root . '/config/local.php';
if (!file_exists($configFile)) {
    fwrite(STDERR, "Konfiguration nicht gefunden: $configFile\n");
    exit(1);
}
$config = require $configFile;
$force = in_array('--force', $argv, true);

$dsn = sprintf('mysql:host=%s;port=%s;dbname=%s;charset=utf8mb4',
    $config['db']['host'] ?? 'localhost',
    (string)($config['db']['port'] ?? 3306),
    $config['db']['name'] ?? '');
try {
    $pdo = new PDO($dsn, $config['db']['user'] ?? '', $config['db']['pass'] ?? '', [
        PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
        PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
    ]);
} catch (Throwable $e) {
    fwrite(STDERR, 'Datenbankverbindung fehlgeschlagen: ' . $e->getMessage() . "\n");
    exit(1);
}

$storageRoot = rtrim((string)($config['app']['storage_path'] ?? ''), '/');
$fileColumns = ['storage_path', 'original_path', 'compressed_path', 'thumbnail_path',
                'generated_pdf_path', 'signature_path', 'file_path', 'path'];

/** Nur Dateien unterhalb der Ablage entfernen – niemals ausserhalb. */
function removeFile(string $path, string $root): bool {
    $path = trim($path);
    if ($path === '' || $root === '' || !is_file($path)) return false;
    $real = realpath($path);
    $base = realpath($root);
    if ($real === false || $base === false) return false;
    if (strpos($real, $base . DIRECTORY_SEPARATOR) !== 0) return false;
    return @unlink($real);
}

$tables = $pdo->query('SHOW TABLES')->fetchAll(PDO::FETCH_COLUMN);
$totalRows = 0; $totalFiles = 0;

echo $force ? "== ENTFERNEN ==\n" : "== VORSCHAU (nichts wird geaendert) ==\n";

foreach ($tables as $table) {
    $cols = $pdo->query('SHOW COLUMNS FROM `' . $table . '`')->fetchAll(PDO::FETCH_COLUMN);
    if (!in_array('deleted_at', $cols, true)) continue;

    $count = (int)$pdo->query("SELECT COUNT(*) FROM `$table` WHERE deleted_at IS NOT NULL")->fetchColumn();
    if ($count === 0) continue;

    $present = array_values(array_intersect($fileColumns, $cols));
    $files = 0;

    if ($present && $storageRoot !== '') {
        $select = implode(',', array_map(fn($c) => '`' . $c . '`', $present));
        $rows = $pdo->query("SELECT $select FROM `$table` WHERE deleted_at IS NOT NULL")->fetchAll();
        foreach ($rows as $row) {
            foreach ($present as $c) {
                $path = (string)($row[$c] ?? '');
                if ($path === '') continue;
                if ($force) { if (removeFile($path, $storageRoot)) $files++; }
                elseif (is_file($path)) { $files++; }
            }
        }
    }

    printf("%-28s %6d Zeilen%s\n", $table, $count, $files ? ", $files Dateien" : '');
    $totalRows += $count; $totalFiles += $files;

    if ($force) {
        try {
            $pdo->exec("DELETE FROM `$table` WHERE deleted_at IS NOT NULL");
        } catch (Throwable $e) {
            fwrite(STDERR, "  ! $table: " . $e->getMessage() . "\n");
        }
    }
}

echo str_repeat('-', 46) . "\n";
printf("Summe: %d Zeilen, %d Dateien\n", $totalRows, $totalFiles);
if (!$force) {
    echo "\nZum tatsaechlichen Entfernen erneut aufrufen mit:  php purge_deleted.php --force\n";
    echo "Bitte vorher eine Datensicherung anlegen.\n";
} else {
    echo "Fertig. Bitte anschliessend OPTIMIZE TABLE ausfuehren, um den Platz freizugeben.\n";
}
