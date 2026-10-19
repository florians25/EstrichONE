<?php
declare(strict_types=1);

spl_autoload_register(function($class){
  $prefix='App\\'; $base=__DIR__.'/';
  if (str_starts_with($class,$prefix)) { $rel=str_replace('\\','/',substr($class,strlen($prefix))).'.php'; if (file_exists($base.$rel)) require_once $base.$rel; }
});

use App\Core\{Database,Request,Response,Router,Jwt};
use App\Services\Audit;

header('Access-Control-Allow-Origin: *');
header('Access-Control-Allow-Headers: Authorization, X-Authorization, X-EW-Authorization, Content-Type, X-Tenant-Id, X-Tenant-Slug, X-Requested-With');
header('Access-Control-Allow-Methods: GET, POST, PUT, PATCH, DELETE, OPTIONS');
header('Referrer-Policy: same-origin');
header('X-Frame-Options: SAMEORIGIN');
header('X-Content-Type-Options: nosniff');
header('Permissions-Policy: camera=(self), geolocation=(self), microphone=()');
if (!empty($_SERVER['HTTPS'])) header('Strict-Transport-Security: max-age=31536000; includeSubDomains');
if (($_SERVER['REQUEST_METHOD'] ?? '') === 'OPTIONS') { http_response_code(204); exit; }

// ---------------------------------------------------------------------------
// Kundenportal
//
// Das Kundenportal ist derzeit auf Kundenwunsch deaktiviert. Der Code bleibt
// vollstaendig erhalten; zum Wiedereinschalten genuegt es, die Konstante auf
// true zu setzen (bzw. in api/config/local.php 'portal' => ['enabled' => true]).
// Solange sie false ist, antworten alle Portal-Endpunkte mit einem klaren
// Hinweis statt Daten auszuliefern.
// ---------------------------------------------------------------------------
const PORTAL_ENABLED_DEFAULT = false;
function portal_enabled(array $config): bool {
  if (isset($config['portal']['enabled'])) return (bool)$config['portal']['enabled'];
  return PORTAL_ENABLED_DEFAULT;
}
function require_portal_enabled(array $config): void {
  if (!portal_enabled($config)) Response::error('Das Kundenportal ist derzeit deaktiviert.', 404);
}

function uuid(): string { return bin2hex(random_bytes(16)); }
function safe_identifier(string $value): string { if (!preg_match('/^[a-zA-Z0-9_]+$/',$value)) Response::error('Ungültiger Bezeichner',400); return $value; }
function assert_required(array $body, array $fields): void { foreach ($fields as $f) if (!isset($body[$f]) || $body[$f] === '') Response::error('Pflichtfeld fehlt: '.$f,422); }
function field_value(array $body, array $field) {
  $key=$field['key']; $value=$body[$key] ?? null;
  if ($value === '') return null;
  if (($field['type'] ?? '') === 'boolean') return filter_var($value, FILTER_VALIDATE_BOOLEAN) ? 1 : 0;
  if (($field['type'] ?? '') === 'json' && is_array($value)) return json_encode($value, JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES);
  return $value;
}
function module_map(array $modules): array { $map=[]; foreach($modules as $m) $map[$m['key']]=$m; return $map; }
function require_user(Database $db, array $config, Request $req): array {
  $tokenForAuth = $req->bearer() ?: (string)($req->query['access_token'] ?? '');
  $payload = Jwt::verify($tokenForAuth, $config['app']['jwt_secret']);
  if (!$payload) Response::error('Nicht angemeldet oder Token abgelaufen',401);
  $user = $db->one("SELECT u.*, t.slug tenant_slug, t.name tenant_name FROM users u JOIN tenants t ON t.id=u.tenant_id WHERE u.id=? AND u.deleted_at IS NULL",[$payload['sub'] ?? '']);
  if (!$user || (int)$user['is_active'] !== 1) Response::error('Benutzer gesperrt oder nicht gefunden',401);
  $perms = $db->all("SELECT DISTINCT p.code FROM permissions p JOIN role_permissions rp ON rp.permission_id=p.id JOIN user_roles ur ON ur.role_id=rp.role_id WHERE ur.user_id=?",[$user['id']]);
  $user['permissions']=array_map(fn($r)=>$r['code'],$perms);
  $roles = $db->all("SELECT DISTINCT r.code, r.name FROM roles r JOIN user_roles ur ON ur.role_id=r.id WHERE ur.user_id=? AND r.deleted_at IS NULL",[$user['id']]);
  $user['role_codes']=array_values(array_filter(array_map(fn($r)=>$r['code'] ?? null,$roles)));
  $user['role_names']=array_values(array_filter(array_map(fn($r)=>$r['name'] ?? null,$roles)));
  $db->exec("UPDATE device_sessions SET last_seen_at=NOW() WHERE tenant_id=? AND user_id=? AND status='active' ORDER BY created_at DESC LIMIT 1",[$user['tenant_id'],$user['id']]);
  return $user;
}
function require_permission(array $user, string $permission): void {
  $perms = $user['permissions'] ?? [];
  if (in_array('*',$perms,true)) return;
  $parts = explode('.', $permission);
  $area = $parts[0];
  $action = $parts[1] ?? 'read';
  $writeActions = ['write','delete','export','archive','share','create','update'];
  $isWrite = in_array($action, $writeActions, true);

  // Kolonnenmitglied: NUR Lesen – AUSSER Tagesdokumentation (reports) und Fotos/Videos (photos/documents).
  if (in_array('kolonnenmitglied', $user['role_codes'] ?? [], true)) {
    if (!$isWrite) return;                                  // Lesen immer erlaubt
    if (in_array($area, ['reports','photos','documents'], true)) return; // erlaubte Schreibbereiche
    Response::error('Als Kolonnenmitglied haben Sie nur Lesezugriff (außer Tagesdokumentation und Fotos/Videos).',403);
  }

  $area = explode('.', $permission)[0];
  // Bauakten-Kern + Dashboard/Profil sind für ALLE Mitarbeiter freigegeben.
  $alwaysAllowed = ['projects','dashboard','profile'];
  if (in_array($area,$alwaysAllowed,true)) return;
  if (in_array($permission,$perms,true)) return;
  // Bereichs-Wildcard (z. B. 'customers' oder 'customers.*' deckt 'customers.read')
  if (in_array($area,$perms,true) || in_array($area.'.*',$perms,true)) return;
  Response::error('Sie haben für diese Aktion keine Berechtigung!',403);
}
// LESE-Zugriff auf Nachschlage-/Referenzdaten, die zum Darstellen der Bauakten
// und Formulare nötig sind – für ALLE angemeldeten Mitarbeiter erlaubt.
// (Schreiben bleibt über require_permission geschützt.)
function require_module_read(array $user, array $module): void {
  $lookupAreas = ['projects','sites','screed','customers','crews','vehicles','machines','pumps','settings','reports','documents','users'];
  $perm = (string)($module['permission'] ?? '');
  $key = (string)($module['key'] ?? '');
  // Referenzdaten sind für angemeldete Mitarbeiter lesbar. Bei users ist die
  // Modulberechtigung aus historischen Gründen teilweise noch "admin", daher
  // muss zusätzlich der Modul-Key geprüft werden.
  if (in_array($perm, $lookupAreas, true) || in_array($key, $lookupAreas, true)) return;
  require_permission($user, $perm.'.read');
}

function uploaded_files(string $field): array {
  if (empty($_FILES[$field])) return [];
  $f = $_FILES[$field];
  if (is_array($f['name'])) {
    $items=[]; foreach($f['name'] as $i=>$name) {
      if (($f['error'][$i] ?? UPLOAD_ERR_NO_FILE) !== UPLOAD_ERR_OK) continue;
      $items[]=['name'=>$name,'type'=>$f['type'][$i]??null,'tmp_name'=>$f['tmp_name'][$i],'error'=>$f['error'][$i],'size'=>$f['size'][$i]??0];
    }
    return $items;
  }
  return (($f['error'] ?? UPLOAD_ERR_NO_FILE) === UPLOAD_ERR_OK) ? [$f] : [];
}
function truthy($value): bool { return in_array(strtolower((string)$value), ['1','true','yes','on'], true); }
function ensure_photo_columns(Database $db): void {
  $cols = $db->all("SHOW COLUMNS FROM photos");
  $names = array_map(fn($c)=>$c['Field'],$cols);
  $add = function(string $name, string $sql) use($db,$names) { if (!in_array($name,$names,true)) $db->exec($sql); };
  $add('thumbnail_path', "ALTER TABLE photos ADD COLUMN thumbnail_path VARCHAR(500) NULL AFTER compressed_path");
  $add('original_size_bytes', "ALTER TABLE photos ADD COLUMN original_size_bytes DECIMAL(14,2) NULL AFTER thumbnail_path");
  $add('compressed_size_bytes', "ALTER TABLE photos ADD COLUMN compressed_size_bytes DECIMAL(14,2) NULL AFTER original_size_bytes");
  $add('thumbnail_size_bytes', "ALTER TABLE photos ADD COLUMN thumbnail_size_bytes DECIMAL(14,2) NULL AFTER compressed_size_bytes");
  $add('width_px', "ALTER TABLE photos ADD COLUMN width_px INT NULL AFTER thumbnail_size_bytes");
  $add('height_px', "ALTER TABLE photos ADD COLUMN height_px INT NULL AFTER width_px");
}
function table_columns(Database $db, string $table): array {
  $safe=safe_identifier($table);
  try { return array_map(fn($c)=>$c['Field'],$db->all("SHOW COLUMNS FROM `$safe`")); } catch(Throwable $e) { return []; }
}
function ensure_project_number_sequence_table(Database $db): void {
  static $done=false; if($done) return; $done=true;
  try {
    $db->exec("CREATE TABLE IF NOT EXISTS project_number_sequences (tenant_id CHAR(32) NOT NULL, `year` CHAR(4) NOT NULL, last_no INT NOT NULL DEFAULT 0, created_at DATETIME NOT NULL, updated_at DATETIME NOT NULL, PRIMARY KEY (tenant_id, `year`)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
  } catch(Throwable $e) { error_log('ensure_project_number_sequence_table: '.$e->getMessage()); }
}
function next_project_no(Database $db, string $tenantId): string {
  ensure_project_number_sequence_table($db);
  $year=date('Y');
  $prefix=$year.'-%';
  $regex='^'.$year.'-[0-9]+$';
  $max=$db->one("SELECT MAX(CAST(SUBSTRING(project_no, 6) AS UNSIGNED)) max_no FROM projects WHERE tenant_id=? AND deleted_at IS NULL AND project_no LIKE ? AND project_no REGEXP ?",[$tenantId,$prefix,$regex]);
  $initial=max(0,(int)($max['max_no'] ?? 0));
  $db->exec("INSERT INTO project_number_sequences (tenant_id,`year`,last_no,created_at,updated_at) VALUES (?,?,?,NOW(),NOW()) ON DUPLICATE KEY UPDATE last_no=GREATEST(last_no, VALUES(last_no))",[$tenantId,$year,$initial]);
  $db->exec("INSERT INTO project_number_sequences (tenant_id,`year`,last_no,created_at,updated_at) VALUES (?,?,1,NOW(),NOW()) ON DUPLICATE KEY UPDATE last_no=LAST_INSERT_ID(last_no + 1), updated_at=NOW()",[$tenantId,$year]);
  $row=$db->one("SELECT LAST_INSERT_ID() n");
  $next=max(1,(int)($row['n'] ?? 1));
  return $year.'-'.str_pad((string)$next, 3, '0', STR_PAD_LEFT);
}
function module_select_columns(array $module, array $existing): string {
  // Benutzer werden an vielen Stellen nur als Nachschlagewerte benötigt
  // (Zuweisung/Anzeige in Bauakten, Tagesdoku, Aufgaben). Niemals sensible
  // Spalten wie password_hash oder 2FA-Secrets über /module/users ausgeben.
  if (($module['key'] ?? '') === 'users' || ($module['table'] ?? '') === 'users') {
    $allowed = ['id','tenant_id','name','email','phone','is_active','two_factor_enabled','role_hint','last_login_at','created_at','updated_at'];
    $cols = array_values(array_filter($allowed, fn($c) => in_array($c, $existing, true)));
    if ($cols) return implode(',', array_map(fn($c) => '`'.safe_identifier($c).'`', $cols));
  }
  return '*';
}
function leadership_role_codes(): array {
  return ['super_admin','firmenadmin','administrator','admin','inhaber','geschaeftsfuehrer','projektleiter','bauleiter'];
}
function user_has_leadership_role(array $user): bool {
  $perms=$user['permissions'] ?? [];
  if(in_array('*',$perms,true)) return true;
  $codes=array_map('strval',$user['role_codes'] ?? []);
  return (bool)array_intersect($codes, leadership_role_codes());
}
function require_project_destructive_permission(Database $db, array $user): void {
  if(!isset($user['role_codes'])) {
    $roles=$db->all("SELECT DISTINCT r.code FROM roles r JOIN user_roles ur ON ur.role_id=r.id WHERE ur.user_id=? AND r.deleted_at IS NULL",[$user['id'] ?? '']);
    $user['role_codes']=array_values(array_filter(array_map(fn($r)=>$r['code'] ?? null,$roles)));
  }
  if(user_has_leadership_role($user)) return;
  Response::error('Nur Führungspositionen dürfen Bauakten stornieren oder löschen.',403);
}
function require_current_password(Database $db, array $user, string $password): void {
  if($password==='') Response::error('Passwort fehlt',422);
  $u=$db->one("SELECT password_hash FROM users WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$user['tenant_id'],$user['id']]);
  if(!$u || !password_verify($password,(string)$u['password_hash'])) Response::error('Passwortbestätigung fehlgeschlagen',403);
}
/**
 * Datensaetze ENDGUELTIG entfernen.
 *
 * Frueher wurden Datensaetze nur mit `deleted_at` markiert und blieben in der
 * Datenbank stehen. Was im Frontend geloescht wurde, war damit weiterhin
 * vorhanden - fuer Datenschutzauskuenfte, Loeschbegehren und den Speicher-
 * verbrauch gleichermassen unguenstig.
 *
 * Diese Funktion loescht die Zeilen wirklich (DELETE). Vorher werden die
 * zugehoerigen Dateien aus der Ablage entfernt, damit keine verwaisten
 * Dateien zurueckbleiben.
 *
 * Der Funktionsname bleibt erhalten, damit ALLE bestehenden Aufrufe -
 * insbesondere die Kaskade beim Loeschen einer Bauakte - ohne weitere
 * Aenderung mitziehen.
 */
function soft_delete_where(Database $db, string $table, string $where, array $params): int {
  return purge_where($db,$table,$where,$params);
}

/** Spalten, in denen Dateipfade stehen koennen. */
function storage_path_columns(): array {
  return ['storage_path','original_path','compressed_path','thumbnail_path',
          'generated_pdf_path','signature_path','file_path','path'];
}

/**
 * Zeilen endgueltig loeschen - samt zugehoeriger Dateien.
 *
 * Rueckgabe: Anzahl der geloeschten Zeilen.
 */
function purge_where(Database $db, string $table, string $where, array $params): int {
  $cols=table_columns($db,$table);
  if(!$cols) return 0;
  $safe=safe_identifier($table);

  // 1) Dateien der betroffenen Zeilen entfernen.
  $fileCols=array_values(array_intersect(storage_path_columns(),$cols));
  if($fileCols){
    $storageRoot=(string)($GLOBALS['EW_STORAGE_ROOT'] ?? '');
    if($storageRoot!==''){
      $select=implode(',',array_map(fn($c)=>'`'.$c.'`',$fileCols));
      $rows=safe_query($db,"SELECT $select FROM `$safe` WHERE $where",$params);
      foreach($rows as $row){
        foreach($fileCols as $c){
          $path=(string)($row[$c] ?? '');
          if($path!=='') delete_private_storage_file($path,$storageRoot);
        }
      }
    }
  }

  // 2) Zeilen endgueltig entfernen.
  try { return $db->exec("DELETE FROM `$safe` WHERE $where",$params); }
  catch(Throwable $e) {
    error_log('purge_where '.$table.': '.$e->getMessage());
    // Notnagel: Laesst sich nicht loeschen (z. B. wegen Fremdschluessel),
    // wird wenigstens markiert - besser als ein stiller Fehlschlag.
    if(in_array('deleted_at',$cols,true)){
      $sets=['deleted_at=NOW()'];
      if(in_array('updated_at',$cols,true)) $sets[]='updated_at=NOW()';
      try { return $db->exec("UPDATE `$safe` SET ".implode(', ',$sets)." WHERE $where",$params); }
      catch(Throwable $e2) { error_log('purge_where fallback '.$table.': '.$e2->getMessage()); }
    }
    return 0;
  }
}
function delete_private_storage_file(string $path, string $storageRoot): bool {
  $path=trim($path); if($path==='') return false;
  if(!is_file($path)) return false;
  $real=realpath($path); $root=realpath(rtrim($storageRoot,'/'));
  if(!$real || !$root || strpos($real, rtrim($root,DIRECTORY_SEPARATOR).DIRECTORY_SEPARATOR)!==0) {
    error_log('Refused to delete file outside storage path: '.$path);
    return false;
  }
  return @unlink($real);
}
function delete_project_linked_data(Database $db, array $config, array $user, string $projectId): array {
  $tid=$user['tenant_id'];
  $siteRows=safe_query($db,"SELECT id FROM construction_sites WHERE tenant_id=? AND project_id=? AND deleted_at IS NULL",[$tid,$projectId]);
  $siteIds=array_values(array_filter(array_map(fn($r)=>(string)($r['id'] ?? ''),$siteRows)));
  $siteWhere=''; $siteParams=[];
  if($siteIds){ $siteWhere=' OR site_id IN ('.implode(',',array_fill(0,count($siteIds),'?')).')'; $siteParams=$siteIds; }

  $documents=safe_query($db,"SELECT id,storage_path FROM documents WHERE tenant_id=? AND deleted_at IS NULL AND (project_id=?$siteWhere)",array_merge([$tid,$projectId],$siteParams));
  $photos=safe_query($db,"SELECT id,original_path,compressed_path,thumbnail_path FROM photos WHERE tenant_id=? AND deleted_at IS NULL AND (project_id=?$siteWhere)",array_merge([$tid,$projectId],$siteParams));
  $reportFiles=$siteIds ? safe_query($db,"SELECT generated_pdf_path FROM reports WHERE tenant_id=? AND site_id IN (".implode(',',array_fill(0,count($siteIds),'?')).") AND deleted_at IS NULL",array_merge([$tid],$siteIds)) : [];
  $acceptanceFiles=$siteIds ? safe_query($db,"SELECT signature_path FROM acceptances WHERE tenant_id=? AND site_id IN (".implode(',',array_fill(0,count($siteIds),'?')).") AND deleted_at IS NULL",array_merge([$tid],$siteIds)) : [];

  $storageRoot=(string)($config['app']['storage_path'] ?? ''); $filesDeleted=0; $paths=[];
  foreach($documents as $doc){ if(!empty($doc['storage_path'])) $paths[]=(string)$doc['storage_path']; }
  foreach($photos as $photo){ foreach(['original_path','compressed_path','thumbnail_path'] as $key){ if(!empty($photo[$key])) $paths[]=(string)$photo[$key]; } }
  foreach($reportFiles as $row){ if(!empty($row['generated_pdf_path'])) $paths[]=(string)$row['generated_pdf_path']; }
  foreach($acceptanceFiles as $row){ if(!empty($row['signature_path'])) $paths[]=(string)$row['signature_path']; }
  foreach(array_unique($paths) as $path){ if(delete_private_storage_file($path,$storageRoot)) $filesDeleted++; }

  $docIds=array_values(array_filter(array_map(fn($r)=>(string)($r['id'] ?? ''),$documents)));
  $photoIds=array_values(array_filter(array_map(fn($r)=>(string)($r['id'] ?? ''),$photos)));
  if($docIds){
    $in=implode(',',array_fill(0,count($docIds),'?'));
    soft_delete_where($db,'document_shares',"tenant_id=? AND document_id IN ($in)",array_merge([$tid],$docIds));
  }
  if($photoIds){
    $in=implode(',',array_fill(0,count($photoIds),'?'));
    soft_delete_where($db,'photo_annotations',"tenant_id=? AND photo_id IN ($in)",array_merge([$tid],$photoIds));
  }
  soft_delete_where($db,'documents',"tenant_id=? AND (project_id=?$siteWhere)",array_merge([$tid,$projectId],$siteParams));
  soft_delete_where($db,'photos',"tenant_id=? AND (project_id=?$siteWhere)",array_merge([$tid,$projectId],$siteParams));
  soft_delete_where($db,'project_notes',"tenant_id=? AND project_id=?",[$tid,$projectId]);
  soft_delete_where($db,'material_orders',"tenant_id=? AND project_id=?",[$tid,$projectId]);
  // Materialpositionen werden über die Bestell-IDs entfernt, sobald die Bauakte gelöscht wird.
  try { $orderRows=safe_query($db,"SELECT id FROM material_orders WHERE tenant_id=? AND project_id=?",[$tid,$projectId]); $orderIds=array_values(array_filter(array_map(fn($r)=>(string)($r['id'] ?? ''),$orderRows))); if($orderIds){ $inOrders=implode(',',array_fill(0,count($orderIds),'?')); soft_delete_where($db,'material_order_items',"tenant_id=? AND order_id IN ($inOrders)",array_merge([$tid],$orderIds)); } } catch(Throwable $e) {}
  soft_delete_where($db,'project_phases',"tenant_id=? AND project_id=?",[$tid,$projectId]);
  soft_delete_where($db,'planning_assignments',"tenant_id=? AND project_id=?",[$tid,$projectId]);
  soft_delete_where($db,'calendar_events',"tenant_id=? AND project_id=?",[$tid,$projectId]);
  soft_delete_where($db,'activity_presence',"tenant_id=? AND project_id=?",[$tid,$projectId]);
  soft_delete_where($db,'tasks',"tenant_id=? AND project_id=?",[$tid,$projectId]);
  soft_delete_where($db,'reports',"tenant_id=? AND project_id=?",[$tid,$projectId]);
  soft_delete_where($db,'inspection_results',"tenant_id=? AND project_id=?",[$tid,$projectId]);
  soft_delete_where($db,'quality_checks',"tenant_id=? AND project_id=?",[$tid,$projectId]);
  soft_delete_where($db,'defects',"tenant_id=? AND project_id=?",[$tid,$projectId]);
  if($siteIds){
    $in=implode(',',array_fill(0,count($siteIds),'?'));
    $siteParamList=array_merge([$tid],$siteIds);
    foreach(['rooms','measurements','cm_measurements','inspection_results','quality_protocols','acceptances','reports','warranty_cases','customer_portal_items','subcontractor_orders','tasks','defects','planning_assignments','calendar_events','quality_checks','activity_presence'] as $siteTable){
      soft_delete_where($db,$siteTable,"tenant_id=? AND site_id IN ($in)",$siteParamList);
    }
  }
  soft_delete_where($db,'construction_sites',"tenant_id=? AND project_id=?",[$tid,$projectId]);
  return ['documents'=>count($documents),'photos'=>count($photos),'files_deleted'=>$filesDeleted,'sites'=>count($siteIds)];
}
// Runs a SELECT but degrades to an empty result set instead of throwing when the
// underlying table or column does not exist on this database. Keeps aggregate
// "Akten"/dashboard endpoints alive when one optional dataset is unavailable.
function safe_query(Database $db, string $sql, array $params=[]): array {
  try { return $db->all($sql,$params); } catch(Throwable $e) { error_log('safe_query skipped: '.$e->getMessage()); return []; }
}
// Creates the quality_checks (QM) table on demand. It is referenced by the
// Bauakte/file endpoint but was missing from the base schema, which caused a
// 500 Internal Server Error whenever a project already had a construction site.
function ensure_quality_checks_table(Database $db): void {
  static $done=false; if($done) return; $done=true;
  try {
    $db->exec("CREATE TABLE IF NOT EXISTS quality_checks (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, site_id VARCHAR(255) NULL, project_id VARCHAR(255) NULL, room_id VARCHAR(255) NULL, check_type VARCHAR(80) NULL, title VARCHAR(255) NULL, status VARCHAR(80) NOT NULL DEFAULT 'open', result VARCHAR(80) NULL, measured_value VARCHAR(120) NULL, target_value VARCHAR(120) NULL, notes TEXT NULL, checked_by CHAR(32) NULL, checked_at DATETIME NULL, created_by CHAR(32) NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, deleted_at DATETIME NULL, KEY idx_quality_checks_site (tenant_id, site_id), KEY idx_quality_checks_project (tenant_id, project_id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
  } catch(Throwable $e) { error_log('ensure_quality_checks_table: '.$e->getMessage()); }
}
// Adds an explicit project_id to documents and photos and backfills it from the
// site relationship. This makes Bauakte file scoping exact (each Akte shows only
// its own documents/photos) instead of relying solely on site_id matching.
function ensure_file_project_columns(Database $db): void {
  static $done=false; if($done) return; $done=true;
  foreach(['documents','photos'] as $table){
    $cols=table_columns($db,$table);
    if($cols && !in_array('project_id',$cols,true)){
      try { $db->exec("ALTER TABLE `$table` ADD COLUMN project_id VARCHAR(255) NULL AFTER site_id"); } catch(Throwable $e) { error_log("add project_id to $table: ".$e->getMessage()); }
    }
  }
  // Backfill from construction_sites.project_id where missing.
  try { $db->exec("UPDATE documents d JOIN construction_sites cs ON cs.id=d.site_id AND cs.tenant_id=d.tenant_id SET d.project_id=cs.project_id WHERE (d.project_id IS NULL OR d.project_id='') AND cs.project_id IS NOT NULL"); } catch(Throwable $e) {}
  try { $db->exec("UPDATE photos p JOIN construction_sites cs ON cs.id=p.site_id AND cs.tenant_id=p.tenant_id SET p.project_id=cs.project_id WHERE (p.project_id IS NULL OR p.project_id='') AND cs.project_id IS NOT NULL"); } catch(Throwable $e) {}
}
function add_column_if_missing(Database $db, string $table, string $column, string $definition): void {
  $cols=table_columns($db,$table);
  if($cols && !in_array($column,$cols,true)) {
    try { $db->exec("ALTER TABLE `".safe_identifier($table)."` ADD COLUMN `".safe_identifier($column)."` $definition"); }
    catch(Throwable $e) { error_log("add_column_if_missing $table.$column: ".$e->getMessage()); }
  }
}
function add_index_if_missing(Database $db, string $table, string $index, string $definition): void {
  try {
    $exists=$db->one("SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=? AND INDEX_NAME=? LIMIT 1",[$table,$index]);
    if(!$exists) $db->exec("ALTER TABLE `".safe_identifier($table)."` $definition");
  } catch(Throwable $e) { error_log("add_index_if_missing $table.$index: ".$e->getMessage()); }
}
function ensure_default_screed_types(Database $db, string $tid): void {
  // Die 5 häufigsten Estricharten nach DIN EN 13813 werden einmalig je Mandant angelegt,
  // falls noch keine Estrichart hinterlegt ist. Idempotent: läuft nur bei leerem Bestand.
  try {
    $count=(int)($db->one("SELECT COUNT(*) c FROM screed_types WHERE tenant_id=? AND deleted_at IS NULL",[$tid])['c'] ?? 0);
    if($count>0) return;
    $defaults=[
      ['Zementestrich (CT)','DIN EN 13813 CT','Zement',28,45,0,'Klassischer, feuchtebeständiger Estrich für nahezu alle Bereiche.'],
      ['Calciumsulfatestrich (CA)','DIN EN 13813 CA','Calciumsulfat/Anhydrit',7,35,0,'Schnell begehbar, ideal für Fußbodenheizung, nicht für Dauernässe.'],
      ['Calciumsulfat-Fließestrich (CAF)','DIN EN 13813 CAF','Calciumsulfat (fließfähig)',7,35,1,'Selbstnivellierend, sehr gut für Flächenheizung geeignet.'],
      ['Gussasphaltestrich (AS)','DIN EN 13813 AS','Bitumen',1,25,0,'Fugenlos, sofort belastbar, feuchtigkeitsunempfindlich.'],
      ['Kunstharzestrich (SR)','DIN EN 13813 SR','Reaktionsharz',1,4,0,'Sehr belastbar und chemikalienbeständig, für Industrieböden.'],
    ];
    foreach($defaults as $d){
      $db->exec(
        "INSERT INTO screed_types (id,tenant_id,`name`,`norm`,`binder`,`drying_time_days`,`min_thickness_mm`,`is_heated`,`notes`,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,NOW(),NOW())",
        [uuid(),$tid,$d[0],$d[1],$d[2],$d[3],$d[4],$d[5],$d[6]]
      );
    }
  } catch(Throwable $e) { error_log('ensure_default_screed_types: '.$e->getMessage()); }
}

function ensure_runtime_columns(Database $db): void {
  static $done=false; if($done) return; $done=true;
  add_column_if_missing($db,'vehicles','license_plate','VARCHAR(255) NULL AFTER `plate`');
  add_column_if_missing($db,'vehicles','registration','VARCHAR(255) NULL AFTER `license_plate`');
  try { $db->exec("UPDATE vehicles SET license_plate=plate WHERE (license_plate IS NULL OR license_plate='') AND plate IS NOT NULL AND plate<>''"); } catch(Throwable $e) {}
  // Erweiterte Fahrzeugverwaltung: zusaetzliche Stammdatenfelder (idempotent angelegt).
  add_column_if_missing($db,'vehicles','make','VARCHAR(120) NULL AFTER `type`');
  add_column_if_missing($db,'vehicles','model','VARCHAR(120) NULL AFTER `make`');
  add_column_if_missing($db,'vehicles','year_built','INT NULL AFTER `model`');
  add_column_if_missing($db,'vehicles','vin','VARCHAR(64) NULL AFTER `year_built`');
  add_column_if_missing($db,'vehicles','mileage_km','INT NULL AFTER `vin`');
  add_column_if_missing($db,'vehicles','fuel_type','VARCHAR(60) NULL AFTER `mileage_km`');
  add_column_if_missing($db,'vehicles','holder','VARCHAR(160) NULL AFTER `fuel_type`');
  add_column_if_missing($db,'vehicles','insurer','VARCHAR(160) NULL AFTER `holder`');
  add_column_if_missing($db,'vehicles','policy_no','VARCHAR(120) NULL AFTER `insurer`');
  add_column_if_missing($db,'vehicles','insurance_due','DATE NULL AFTER `policy_no`');
  add_column_if_missing($db,'vehicles','next_service','DATE NULL AFTER `inspection_due`');
  add_column_if_missing($db,'vehicles','purchase_date','DATE NULL AFTER `next_service`');
  // Dokumente koennen Fahrzeugen/Pumpen zugeordnet werden.
  add_column_if_missing($db,'documents','vehicle_id','VARCHAR(255) NULL AFTER `room_id`');
  // Kalendertermine: Der Termindialog erfasst seit jeher einen Ort, die
  // Tabelle hatte dafuer aber keine Spalte – der Wert ging beim Speichern
  // verloren. Wird hier idempotent nachgezogen.
  add_column_if_missing($db,'calendar_events','location','VARCHAR(255) NULL AFTER `description`');
  // Zuständigkeit eines Termins (Mitarbeiter). Ohne Spalte ging die Auswahl
  // beim Speichern verloren.
  add_column_if_missing($db,'calendar_events','assigned_user_id','VARCHAR(255) NULL AFTER `site_id`');
  ensure_pumps_table($db);
  ensure_suppliers_table($db);
  ensure_material_orders_table($db);
  add_column_if_missing($db,'projects','hinweise','TEXT NULL AFTER `notes`');
  add_column_if_missing($db,'construction_sites','hinweise','TEXT NULL AFTER `notes`');
  add_column_if_missing($db,'customers','contact_person','VARCHAR(255) NULL AFTER `name`');
  // Erstanmeldung: erzwungener Passwortwechsel
  add_column_if_missing($db,'users','must_change_password','TINYINT(1) NOT NULL DEFAULT 0');
  try { $db->exec("ALTER TABLE calendar_events MODIFY COLUMN `project_id` VARCHAR(255) NULL"); } catch(Throwable $e) {}
  add_column_if_missing($db,'calendar_events','all_day','TINYINT(1) NOT NULL DEFAULT 0 AFTER `end_at`');
  add_column_if_missing($db,'calendar_events','event_type','VARCHAR(80) NULL AFTER `all_day`');
  add_column_if_missing($db,'calendar_events','color','VARCHAR(255) NULL AFTER `event_type`');
  add_column_if_missing($db,'calendar_events','visibility','VARCHAR(80) NULL AFTER `color`');
  add_column_if_missing($db,'calendar_events','recurrence_rule','VARCHAR(255) NULL AFTER `visibility`');
  add_column_if_missing($db,'calendar_events','reminder_minutes','DECIMAL(14,2) NULL AFTER `recurrence_rule`');
  add_column_if_missing($db,'calendar_events','external_provider','VARCHAR(80) NULL AFTER `reminder_minutes`');
  add_column_if_missing($db,'calendar_events','external_id','VARCHAR(255) NULL AFTER `external_provider`');
  add_column_if_missing($db,'documents','customer_visible','TINYINT(1) NOT NULL DEFAULT 0');
  ensure_calendar_categories($db);
  // Performance-Indizes (idempotent) – häufige Filterkombinationen
  add_index_if_missing($db,'projects','idx_projects_tenant_status','ADD INDEX idx_projects_tenant_status (tenant_id, status)');
  add_index_if_missing($db,'projects','idx_projects_tenant_customer','ADD INDEX idx_projects_tenant_customer (tenant_id, customer_id)');
  add_index_if_missing($db,'planning_assignments','idx_plan_tenant_start','ADD INDEX idx_plan_tenant_start (tenant_id, start_at)');
  add_index_if_missing($db,'documents','idx_documents_tenant_project','ADD INDEX idx_documents_tenant_project (tenant_id, project_id)');
  add_index_if_missing($db,'user_roles','idx_user_roles_user','ADD INDEX idx_user_roles_user (user_id)');
  add_index_if_missing($db,'users','idx_users_tenant_active','ADD INDEX idx_users_tenant_active (tenant_id, is_active)');
}
function ensure_pumps_table(Database $db): void {
  static $done=false; if($done) return; $done=true;
  $db->exec("CREATE TABLE IF NOT EXISTS pumps (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, name VARCHAR(160) NOT NULL, manufacturer VARCHAR(120) NULL, model VARCHAR(120) NULL, serial_no VARCHAR(120) NULL, type VARCHAR(80) NULL, status VARCHAR(60) NULL DEFAULT 'available', max_output_l_min DECIMAL(10,2) NULL, max_pressure_bar DECIMAL(10,2) NULL, max_distance_m DECIMAL(10,2) NULL, hose_length_m DECIMAL(10,2) NULL, operating_hours INT NULL, year_built INT NULL, inspection_due DATE NULL, next_service DATE NULL, notes TEXT NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, deleted_at DATETIME NULL, KEY idx_pumps_tenant (tenant_id), KEY idx_pumps_status (tenant_id,status)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
}
/**
 * Schnellanmeldung: Geraete, die sich mit PIN anmelden duerfen.
 *
 * Gedanke: Auf einem Buerorechner oder einem festen Baustellen-Tablet ist die
 * volle Anmeldung mit langer E-Mail und Passwort mehrmals taeglich laestig.
 * Nach EINER vollstaendigen Anmeldung kann das Geraet hinterlegt werden;
 * danach genuegt eine PIN.
 *
 * Sicherheitsgedanke dahinter:
 *  - Die PIN allein reicht NICHT. Sie gilt nur zusammen mit der Kennung des
 *    Geraets, die im Browser dieses Geraets liegt. Wer die PIN kennt, kommt
 *    von einem anderen Geraet aus nicht hinein.
 *  - Die PIN wird wie ein Passwort gehasht gespeichert, nie im Klartext.
 *  - Nach 5 Fehlversuchen wird der Geraeteeintrag gesperrt und die volle
 *    Anmeldung ist wieder noetig.
 *  - Jeder Eintrag laeuft nach 90 Tagen ohne Nutzung ab.
 */
function ensure_quick_login_table(Database $db): void {
  static $done=false; if($done) return; $done=true;
  $db->exec("CREATE TABLE IF NOT EXISTS quick_logins (
    id CHAR(32) PRIMARY KEY,
    tenant_id CHAR(32) NOT NULL,
    user_id CHAR(32) NOT NULL,
    device_id VARCHAR(64) NOT NULL,
    device_label VARCHAR(120) NULL,
    pin_hash VARCHAR(255) NOT NULL,
    failed_attempts INT NOT NULL DEFAULT 0,
    locked_at DATETIME NULL,
    last_used_at DATETIME NULL,
    created_at DATETIME NOT NULL,
    updated_at DATETIME NULL,
    revoked_at DATETIME NULL,
    UNIQUE KEY uq_quick_device (device_id),
    KEY idx_quick_user (tenant_id,user_id)
  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
}

/** Ist die Schnellanmeldung fuer diesen Mandanten freigeschaltet? */
function quick_login_enabled(Database $db, ?string $tenantId): bool {
  try {
    ensure_app_settings_table($db);
    $row=$db->one("SELECT value_json FROM app_settings WHERE tenant_id=? AND setting_key='quick_login'",[$tenantId]);
    if(!$row) return false;
    $cfg=json_decode((string)$row['value_json'],true);
    return is_array($cfg) && !empty($cfg['enabled']);
  } catch(Throwable $e) { return false; }
}

function ensure_app_settings_table(Database $db): void {
  static $done=false; if($done) return; $done=true;
  $db->exec("CREATE TABLE IF NOT EXISTS app_settings (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NULL, setting_key VARCHAR(80) NOT NULL, value_json LONGTEXT NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, UNIQUE KEY uq_app_setting (tenant_id,setting_key)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
}
function whats_new_file_path(): string { return __DIR__.'/content/whats-new.txt'; }
function read_whats_new_text(): string {
  $path=whats_new_file_path();
  if(!is_file($path)) return '';
  $text=@file_get_contents($path);
  if($text===false) return '';
  $text=str_replace(["\r\n","\r"],"\n",(string)$text);
  return trim($text);
}
function parse_whats_new_entries(string $text): array {
  $entries=[]; $current=null;
  foreach(explode("\n",$text) as $rawLine){
    $line=trim($rawLine);
    if($line==='') continue;
    if(preg_match('/^##\s*(.+)$/u',$line,$m)){
      if($current) $entries[]=$current;
      $head=trim($m[1]); $date=''; $title=$head; $version='';
      if(preg_match('/^(\d{4}-\d{2}-\d{2})\s*[–-]\s*(.+)$/u',$head,$hm)){ $date=$hm[1]; $title=trim($hm[2]); }
      elseif(preg_match('/^(v?\d+(?:\.\d+){1,3})\s*[–-]\s*(.+)$/u',$head,$hm)){ $version=$hm[1]; $title=trim($hm[2]); }
      $current=['version'=>$version,'date'=>$date,'title'=>$title,'notes'=>''];
      continue;
    }
    if(!$current){ if(strpos($line,'#')===0) continue; $current=['version'=>'','date'=>'','title'=>'Update','notes'=>'']; }
    $note=preg_replace('/^[-*]\s*/u','',$line);
    $current['notes'].=($current['notes']!==''?"\n":'').$note;
  }
  if($current) $entries[]=$current;
  return $entries;
}
function ensure_project_notes_table(Database $db): void {
  static $done=false; if($done) return; $done=true;
  $db->exec("CREATE TABLE IF NOT EXISTS project_notes (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, project_id VARCHAR(255) NOT NULL, entry_type VARCHAR(40) NOT NULL DEFAULT 'note', author_id CHAR(32) NULL, author_name VARCHAR(160) NULL, body TEXT NULL, document_id CHAR(32) NULL, meta_json TEXT NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, deleted_at DATETIME NULL, KEY idx_project_notes (tenant_id,project_id,created_at)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
}
function ensure_customer_portal_table(Database $db): void {
  static $done=false; if($done) return; $done=true;
  $db->exec("CREATE TABLE IF NOT EXISTS customer_portal_access (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, customer_id VARCHAR(255) NOT NULL, token_hash CHAR(64) NOT NULL, pin_hash CHAR(64) NULL, is_active TINYINT(1) NOT NULL DEFAULT 1, last_access_at DATETIME NULL, failed_attempts INT NOT NULL DEFAULT 0, created_at DATETIME NOT NULL, updated_at DATETIME NULL, revoked_at DATETIME NULL, UNIQUE KEY uq_portal_token (token_hash), KEY idx_portal_customer (tenant_id,customer_id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
  add_column_if_missing($db,'customer_portal_access','pin_hash','CHAR(64) NULL');
  add_column_if_missing($db,'customer_portal_access','failed_attempts','INT NOT NULL DEFAULT 0');
}
function status_label_de(string $status): string {
  $map=['new'=>'Neu','planned'=>'Geplant','in_progress'=>'In Arbeit','delayed'=>'Verzögert','defect'=>'Mangel','completed'=>'Abgeschlossen','closed'=>'Geschlossen','cancelled'=>'Storniert','open'=>'Offen','active'=>'Aktiv','to_order'=>'Zu bestellen','ordered'=>'Bestellt','done'=>'Erledigt','cancelled'=>'Abgesagt'];
  return $map[$status] ?? $status;
}
// System-Ereignis in den Notiz-/Verlaufstrang schreiben (z. B. Zuweisung, Statuswechsel)
function log_project_event(Database $db, array $user, string $projectId, string $body, string $type='system'): void {
  if(!$projectId) return;
  try {
    ensure_project_notes_table($db);
    $db->exec("INSERT INTO project_notes (id,tenant_id,project_id,entry_type,author_id,author_name,body,created_at,updated_at) VALUES (?,?,?,?,?,?,?,NOW(),NOW())",[uuid(),$user['tenant_id'],$projectId,$type,$user['id']??null,$user['name']??null,$body]);
  } catch(Throwable $e) { error_log('log_project_event: '.$e->getMessage()); }
}
function ensure_call_logs_table(Database $db): void {
  static $done=false; if($done) return; $done=true;
  $db->exec("CREATE TABLE IF NOT EXISTS call_logs (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, kind VARCHAR(20) NOT NULL DEFAULT 'call', caller_name VARCHAR(200) NULL, caller_phone VARCHAR(80) NULL, company VARCHAR(200) NULL, direction VARCHAR(20) NOT NULL DEFAULT 'inbound', subject VARCHAR(255) NULL, notes TEXT NULL, follow_up TINYINT(1) NOT NULL DEFAULT 0, inquiry_status VARCHAR(30) NOT NULL DEFAULT 'open', converted_project_id VARCHAR(255) NULL, project_id VARCHAR(255) NULL, note_id CHAR(32) NULL, handled_by VARCHAR(160) NULL, called_at DATETIME NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, deleted_at DATETIME NULL, KEY idx_calls_tenant (tenant_id,created_at), KEY idx_calls_kind (tenant_id,kind,inquiry_status), KEY idx_calls_project (tenant_id,project_id), KEY idx_calls_followup (tenant_id,follow_up)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
  add_column_if_missing($db,'call_logs','kind',"VARCHAR(20) NOT NULL DEFAULT 'call' AFTER `tenant_id`");
  add_column_if_missing($db,'call_logs','caller_email',"VARCHAR(200) NULL AFTER `caller_phone`");
  add_column_if_missing($db,'call_logs','customer_id',"VARCHAR(255) NULL AFTER `caller_email`");
  add_index_if_missing($db,'call_logs','idx_calls_customer',"ADD KEY `idx_calls_customer` (`tenant_id`,`customer_id`)");
  add_column_if_missing($db,'call_logs','address',"VARCHAR(255) NULL AFTER `company`");
  add_column_if_missing($db,'call_logs','zip',"VARCHAR(20) NULL AFTER `address`");
  add_column_if_missing($db,'call_logs','city',"VARCHAR(160) NULL AFTER `zip`");
  add_column_if_missing($db,'call_logs','source',"VARCHAR(40) NULL AFTER `direction`");
  add_column_if_missing($db,'call_logs','priority',"VARCHAR(20) NOT NULL DEFAULT 'normal' AFTER `source`");
  add_column_if_missing($db,'call_logs','desired_start',"DATE NULL AFTER `priority`");
  add_column_if_missing($db,'call_logs','area_m2',"DECIMAL(10,2) NULL AFTER `desired_start`");
  add_column_if_missing($db,'call_logs','screed_type',"VARCHAR(120) NULL AFTER `area_m2`");
  add_column_if_missing($db,'call_logs','next_step',"VARCHAR(255) NULL AFTER `screed_type`");
  add_column_if_missing($db,'call_logs','due_date',"DATE NULL AFTER `next_step`");
  add_column_if_missing($db,'call_logs','inquiry_status',"VARCHAR(30) NOT NULL DEFAULT 'open' AFTER `follow_up`");
  add_column_if_missing($db,'call_logs','converted_project_id',"VARCHAR(255) NULL AFTER `inquiry_status`");
  add_index_if_missing($db,'call_logs','idx_calls_kind',"ADD KEY `idx_calls_kind` (`tenant_id`,`kind`,`inquiry_status`)");
  try { $db->exec("UPDATE call_logs SET kind='call' WHERE kind IS NULL OR kind=''"); } catch(Throwable $e) {}
}
function insert_call_log(Database $db, array $data): void {
  $cols = table_columns($db, 'call_logs');
  $now = date('Y-m-d H:i:s');
  $data['created_at'] = $data['created_at'] ?? $now;
  $data['updated_at'] = $data['updated_at'] ?? $now;
  $available = [];
  foreach ($data as $key => $value) {
    if (in_array($key, $cols, true)) $available[$key] = $value;
  }
  if (!isset($available['id']) || !isset($available['tenant_id'])) Response::error('Anfrage/Anruf konnte nicht gespeichert werden: Datenbankstruktur unvollständig',500);
  $names = array_keys($available);
  $sql = "INSERT INTO call_logs (`".implode('`,`', array_map('safe_identifier', $names))."`) VALUES (".implode(',', array_fill(0,count($names),'?')).")";
  $db->exec($sql, array_values($available));
}
function update_call_log(Database $db, string $tenantId, string $id, array $changes): void {
  $cols = table_columns($db, 'call_logs');
  $sets=[]; $vals=[];
  foreach($changes as $key=>$value){ if(in_array($key,$cols,true)){ $sets[]='`'.safe_identifier($key).'`=?'; $vals[]=$value; } }
  if(!$sets) Response::error('Keine speicherbaren Änderungen',422);
  if(in_array('updated_at',$cols,true)) $sets[]='updated_at=NOW()';
  $vals[]=$tenantId; $vals[]=$id;
  $db->exec("UPDATE call_logs SET ".implode(',',$sets)." WHERE tenant_id=? AND id=?",$vals);
}
function ensure_calendar_categories(Database $db): void {
  $db->exec("CREATE TABLE IF NOT EXISTS calendar_categories (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, code VARCHAR(80) NOT NULL, label VARCHAR(160) NOT NULL, color VARCHAR(32) NULL, icon VARCHAR(80) NULL, is_active TINYINT(1) NOT NULL DEFAULT 1, sort_order INT NOT NULL DEFAULT 100, notes TEXT NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, deleted_at DATETIME NULL, UNIQUE KEY uq_calendar_category_code (tenant_id,code), KEY idx_calendar_categories_tenant (tenant_id), KEY idx_calendar_categories_active (tenant_id,is_active,sort_order)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
}
function vehicle_plate_expr(Database $db): string {
  $cols=table_columns($db,'vehicles');
  $parts=[];
  foreach(['plate','license_plate','registration'] as $col) if(in_array($col,$cols,true)) $parts[]='v.`'.$col.'`';
  return $parts ? 'COALESCE('.implode(',',$parts).')' : "''";
}
function ensure_planning_columns(Database $db): void {
  ensure_runtime_columns($db);
  $add=function(string $name, string $def) use($db){ add_column_if_missing($db,'planning_assignments',$name,$def); };
  $add('project_id','CHAR(32) NULL AFTER `tenant_id`');
  $add('site_id','VARCHAR(255) NULL AFTER `project_id`');
  $add('crew_id','VARCHAR(255) NULL');
  $add('vehicle_id','VARCHAR(255) NULL');
  $add('machine_id','VARCHAR(255) NULL');
  $add('title',"VARCHAR(255) NOT NULL DEFAULT 'Einplanung'");
  $add('status',"VARCHAR(80) NULL DEFAULT 'planned'");
  $add('start_at','DATETIME NULL');
  $add('end_at','DATETIME NULL');
  $add('notes','TEXT NULL');
  add_index_if_missing($db,'planning_assignments','idx_planning_assignments_project','ADD KEY `idx_planning_assignments_project` (`project_id`)');
  add_index_if_missing($db,'planning_assignments','idx_planning_assignments_site','ADD KEY `idx_planning_assignments_site` (`site_id`)');
}
function write_resized_image(string $sourceTmp, string $target, int $maxSide, int $quality = 72): array {
  $info = @getimagesize($sourceTmp);
  if (!$info || !function_exists('imagecreatefromstring')) {
    copy($sourceTmp, $target);
    return ['width'=>null,'height'=>null,'size'=>filesize($target) ?: null];
  }
  $raw = file_get_contents($sourceTmp);
  $img = @imagecreatefromstring($raw);
  if (!$img) { copy($sourceTmp, $target); return ['width'=>$info[0]??null,'height'=>$info[1]??null,'size'=>filesize($target) ?: null]; }
  $w = imagesx($img); $h = imagesy($img); $ratio = min(1, $maxSide / max($w, $h));
  $nw = max(1, (int)round($w * $ratio)); $nh = max(1, (int)round($h * $ratio));
  $canvas = imagecreatetruecolor($nw, $nh);
  imagecopyresampled($canvas, $img, 0, 0, 0, 0, $nw, $nh, $w, $h);
  if (function_exists('imagewebp')) imagewebp($canvas, $target, $quality); else imagejpeg($canvas, $target, $quality);
  imagedestroy($img); imagedestroy($canvas);
  return ['width'=>$nw,'height'=>$nh,'size'=>filesize($target) ?: null];
}

function ensure_lexware_tables(Database $db): void {
  $db->exec("CREATE TABLE IF NOT EXISTS lexware_connections (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, api_base_url VARCHAR(255) NOT NULL DEFAULT 'https://api.lexware.io', api_key_encrypted TEXT NULL, is_active TINYINT(1) NOT NULL DEFAULT 0, auto_sync_customers TINYINT(1) NOT NULL DEFAULT 1, auto_sync_projects TINYINT(1) NOT NULL DEFAULT 1, last_test_at DATETIME NULL, last_error TEXT NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, deleted_at DATETIME NULL, KEY idx_lexware_connections_tenant (tenant_id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
  $db->exec("CREATE TABLE IF NOT EXISTS lexware_entity_links (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, local_entity_type VARCHAR(80) NOT NULL, local_entity_id CHAR(32) NOT NULL, lexware_entity_type VARCHAR(80) NOT NULL, lexware_id VARCHAR(120) NULL, lexware_number VARCHAR(120) NULL, sync_direction VARCHAR(40) NOT NULL DEFAULT 'manual', status VARCHAR(40) NOT NULL DEFAULT 'linked', last_synced_at DATETIME NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, deleted_at DATETIME NULL, UNIQUE KEY uq_lexware_local (tenant_id,local_entity_type,local_entity_id,lexware_entity_type), KEY idx_lexware_remote (tenant_id,lexware_entity_type,lexware_id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
  $db->exec("CREATE TABLE IF NOT EXISTS lexware_synced_records (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, entity_type VARCHAR(80) NOT NULL, lexware_id VARCHAR(120) NOT NULL, lexware_number VARCHAR(120) NULL, local_entity_type VARCHAR(80) NULL, local_entity_id CHAR(32) NULL, title VARCHAR(255) NULL, status VARCHAR(80) NULL, document_date DATE NULL, amount_total DECIMAL(14,2) NULL, currency VARCHAR(8) NULL, contact_lexware_id VARCHAR(120) NULL, raw_json MEDIUMTEXT NULL, last_synced_at DATETIME NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, deleted_at DATETIME NULL, UNIQUE KEY uq_lexware_synced (tenant_id,entity_type,lexware_id), KEY idx_lexware_synced_tenant (tenant_id), KEY idx_lexware_synced_local (tenant_id,local_entity_type,local_entity_id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
  $db->exec("CREATE TABLE IF NOT EXISTS lexware_auto_sync_runs (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, started_at DATETIME NOT NULL, finished_at DATETIME NULL, status VARCHAR(40) NOT NULL DEFAULT 'running', summary_json TEXT NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, KEY idx_lexware_auto_sync_runs_tenant (tenant_id, started_at)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
  foreach ([['customers','lexware_contact_id','ALTER TABLE customers ADD COLUMN lexware_contact_id VARCHAR(120) NULL AFTER customer_no'], ['projects','lexware_order_id','ALTER TABLE projects ADD COLUMN lexware_order_id VARCHAR(120) NULL AFTER project_no'], ['projects','lexware_quote_id','ALTER TABLE projects ADD COLUMN lexware_quote_id VARCHAR(120) NULL AFTER lexware_order_id']] as $item) {
    $cols=$db->all("SHOW COLUMNS FROM `{$item[0]}`"); $names=array_map(fn($c)=>$c['Field'],$cols); if(!in_array($item[1],$names,true)) $db->exec($item[2]);
  }
  $cols=$db->all("SHOW COLUMNS FROM lexware_connections"); $names=array_map(fn($c)=>$c['Field'],$cols);
  foreach ([
    ['auto_sync_interval_minutes',"ALTER TABLE lexware_connections ADD COLUMN auto_sync_interval_minutes INT NOT NULL DEFAULT 4 AFTER auto_sync_projects"],
    ['sync_contacts',"ALTER TABLE lexware_connections ADD COLUMN sync_contacts TINYINT(1) NOT NULL DEFAULT 1 AFTER auto_sync_interval_minutes"],
    ['sync_vouchers',"ALTER TABLE lexware_connections ADD COLUMN sync_vouchers TINYINT(1) NOT NULL DEFAULT 1 AFTER sync_contacts"],
    ['sync_invoices',"ALTER TABLE lexware_connections ADD COLUMN sync_invoices TINYINT(1) NOT NULL DEFAULT 1 AFTER sync_vouchers"],
    ['sync_quotations',"ALTER TABLE lexware_connections ADD COLUMN sync_quotations TINYINT(1) NOT NULL DEFAULT 1 AFTER sync_invoices"],
    ['last_auto_sync_at',"ALTER TABLE lexware_connections ADD COLUMN last_auto_sync_at DATETIME NULL AFTER last_test_at"],
    ['last_sync_status',"ALTER TABLE lexware_connections ADD COLUMN last_sync_status VARCHAR(40) NULL AFTER last_auto_sync_at"],
    ['last_sync_summary',"ALTER TABLE lexware_connections ADD COLUMN last_sync_summary TEXT NULL AFTER last_sync_status"]
  ] as $pair) { if(!in_array($pair[0],$names,true)) { try { $db->exec($pair[1]); } catch(Throwable $e) {} } }
}
function secret_encrypt(string $plain, string $secret): string {
  if(function_exists('openssl_encrypt')) { $iv=random_bytes(16); $key=hash('sha256',$secret,true); $cipher=openssl_encrypt($plain,'AES-256-CBC',$key,OPENSSL_RAW_DATA,$iv); return 'aes:'.base64_encode($iv.$cipher); }
  return 'b64:'.base64_encode($plain);
}
function secret_decrypt(?string $encoded, string $secret): ?string {
  if(!$encoded) return null;
  if(str_starts_with($encoded,'aes:') && function_exists('openssl_decrypt')) { $raw=base64_decode(substr($encoded,4),true); if($raw===false || strlen($raw)<17) return null; $iv=substr($raw,0,16); $cipher=substr($raw,16); $key=hash('sha256',$secret,true); $plain=openssl_decrypt($cipher,'AES-256-CBC',$key,OPENSSL_RAW_DATA,$iv); return $plain===false?null:$plain; }
  if(str_starts_with($encoded,'b64:')) return base64_decode(substr($encoded,4)) ?: null;
  return null;
}
function lexware_get_config(Database $db, array $config, string $tenantId): array {
  ensure_lexware_tables($db);
  $row=$db->one("SELECT * FROM lexware_connections WHERE tenant_id=? AND deleted_at IS NULL ORDER BY created_at DESC LIMIT 1",[$tenantId]);
  if(!$row){ $id=uuid(); $db->exec("INSERT INTO lexware_connections (id,tenant_id,api_base_url,is_active,auto_sync_customers,auto_sync_projects,auto_sync_interval_minutes,sync_contacts,sync_vouchers,sync_invoices,sync_quotations,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?,?,NOW(),NOW())",[$id,$tenantId,'https://api.lexware.io',0,1,1,4,1,1,1,1]); $row=$db->one("SELECT * FROM lexware_connections WHERE id=?",[$id]); }
  $row['has_api_key']=!empty($row['api_key_encrypted']);
  unset($row['api_key_encrypted']);
  return $row;
}
function lexware_log(Database $db, string $tenantId, string $entityType, string $status, string $message, ?string $entityId=null, $payload=null): void {
  // Speicher-schonend: erfolgreiche Routine-Syncs werden nicht mehr als einzelne Logzeilen abgelegt.
  // Stattdessen wird nur der letzte Ausführungszeitpunkt direkt an der Lexware-Verbindung aktualisiert.
  try {
    ensure_lexware_tables($db);
    $summary = $payload ? json_encode($payload, JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES) : $message;
    $db->exec("UPDATE lexware_connections SET last_auto_sync_at=NOW(), last_sync_status=?, last_sync_summary=?, last_error=?, updated_at=NOW() WHERE tenant_id=? AND deleted_at IS NULL", [
      $status,
      $summary,
      $status === 'failed' ? $message : null,
      $tenantId
    ]);
    if($status !== 'failed') return;
    // Fehler werden weiterhin gespeichert, damit die Ursache später nachvollziehbar bleibt.
    $cols=$db->all("SHOW COLUMNS FROM lexware_sync_logs"); $names=array_map(fn($c)=>$c['Field'],$cols); $id=uuid();
    $payloadJson=$payload?json_encode($payload,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES):null;
    if(in_array('message',$names,true) && in_array('payload_json',$names,true)) $db->exec("INSERT INTO lexware_sync_logs (id,tenant_id,entity_type,entity_id,status,message,payload_json,synced_at,created_at,updated_at) VALUES (?,?,?,?,?,?,?,NOW(),NOW(),NOW())",[$id,$tenantId,$entityType,$entityId,$status,$message,$payloadJson]);
    elseif(in_array('message',$names,true)) $db->exec("INSERT INTO lexware_sync_logs (id,tenant_id,entity_type,entity_id,status,message,created_at,updated_at) VALUES (?,?,?,?,?,?,NOW(),NOW())",[$id,$tenantId,$entityType,$entityId,$status,$message]);
    else $db->exec("INSERT INTO lexware_sync_logs (id,tenant_id,entity_type,entity_id,status,error_message,payload_json,created_at,updated_at) VALUES (?,?,?,?,?,?,?,NOW(),NOW())",[$id,$tenantId,$entityType,$entityId,$status,$message,$payloadJson]);
  } catch(Throwable $e) {}
}
function lexware_api_request(Database $db, array $config, string $tenantId, string $method, string $path, ?array $body=null): array {
  ensure_lexware_tables($db);
  $row=$db->one("SELECT * FROM lexware_connections WHERE tenant_id=? AND deleted_at IS NULL ORDER BY created_at DESC LIMIT 1",[$tenantId]);
  if(!$row || (int)$row['is_active']!==1) Response::error('Lexware-Integration ist nicht aktiv',422);
  $token=secret_decrypt($row['api_key_encrypted'] ?? null, $config['app']['jwt_secret'] ?? 'estrichweb'); if(!$token) Response::error('Lexware API-Key fehlt',422);
  if(!function_exists('curl_init')) Response::error('PHP-cURL ist auf dem Webspace nicht aktiv',500);
  $base=rtrim((string)($row['api_base_url'] ?: 'https://api.lexware.io'),'/'); $url=$base.'/'.ltrim($path,'/');
  $ch=curl_init($url); $headers=['Authorization: Bearer '.$token,'Accept: application/json','Content-Type: application/json'];
  curl_setopt_array($ch,[CURLOPT_RETURNTRANSFER=>true,CURLOPT_TIMEOUT=>30,CURLOPT_HTTPHEADER=>$headers,CURLOPT_CUSTOMREQUEST=>strtoupper($method)]);
  if($body!==null) curl_setopt($ch,CURLOPT_POSTFIELDS,json_encode($body,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES));
  $raw=curl_exec($ch); $status=(int)curl_getinfo($ch,CURLINFO_RESPONSE_CODE); $err=curl_error($ch); curl_close($ch);
  if($raw===false) Response::error('Lexware-Verbindung fehlgeschlagen: '.$err,502);
  $decoded=json_decode($raw,true); if($status>=400) Response::error('Lexware API meldet HTTP '.$status,502,['response'=>$decoded ?: $raw]);
  return is_array($decoded)?$decoded:['raw'=>$raw,'status'=>$status];
}
function lexware_contact_name(array $contact): string {
  $company=$contact['company']['name'] ?? null; if($company) return (string)$company;
  $person=$contact['person'] ?? []; $name=trim(($person['firstName'] ?? '').' '.($person['lastName'] ?? '')); return $name ?: ($contact['id'] ?? 'Lexware Kontakt');
}
function lexware_contact_email(array $contact): ?string {
  $emails=$contact['emailAddresses']['business'] ?? $contact['emailAddresses']['office'] ?? null; return is_array($emails) ? ($emails[0] ?? null) : (is_string($emails)?$emails:null);
}

function lexware_contact_phone(array $contact): ?string {
  $phones=$contact['phoneNumbers']['business'] ?? $contact['phoneNumbers']['office'] ?? $contact['phoneNumbers']['mobile'] ?? null;
  return is_array($phones) ? ($phones[0] ?? null) : (is_string($phones)?$phones:null);
}
function lexware_contact_address(array $contact): array {
  $addresses=$contact['addresses'] ?? [];
  $entry=null;
  foreach(['billing','shipping','business'] as $key){ if(isset($addresses[$key]) && is_array($addresses[$key]) && !empty($addresses[$key][0])) { $entry=$addresses[$key][0]; break; } }
  if(!$entry && is_array($addresses)) foreach($addresses as $value){ if(is_array($value) && !empty($value[0]) && is_array($value[0])) { $entry=$value[0]; break; } }
  return ['address'=>trim((string)($entry['street'] ?? $entry['addressLine1'] ?? '')), 'zip'=>(string)($entry['zip'] ?? $entry['postalCode'] ?? ''), 'city'=>(string)($entry['city'] ?? '')];
}
function lexware_api_request_array(Database $db, array $config, string $tenantId, string $method, string $path, ?array $body=null): array {
  ensure_lexware_tables($db);
  $row=$db->one("SELECT * FROM lexware_connections WHERE tenant_id=? AND deleted_at IS NULL ORDER BY created_at DESC LIMIT 1",[$tenantId]);
  if(!$row || (int)$row['is_active']!==1) throw new RuntimeException('Lexware-Integration ist nicht aktiv');
  $token=secret_decrypt($row['api_key_encrypted'] ?? null, $config['app']['jwt_secret'] ?? 'estrichweb'); if(!$token) throw new RuntimeException('Lexware API-Key fehlt');
  if(!function_exists('curl_init')) throw new RuntimeException('PHP-cURL ist auf dem Webspace nicht aktiv');
  $base=rtrim((string)($row['api_base_url'] ?: 'https://api.lexware.io'),'/'); $url=$base.'/'.ltrim($path,'/');
  $ch=curl_init($url); $headers=['Authorization: Bearer '.$token,'Accept: application/json','Content-Type: application/json'];
  curl_setopt_array($ch,[CURLOPT_RETURNTRANSFER=>true,CURLOPT_TIMEOUT=>35,CURLOPT_HTTPHEADER=>$headers,CURLOPT_CUSTOMREQUEST=>strtoupper($method)]);
  if($body!==null) curl_setopt($ch,CURLOPT_POSTFIELDS,json_encode($body,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES));
  $raw=curl_exec($ch); $status=(int)curl_getinfo($ch,CURLINFO_RESPONSE_CODE); $err=curl_error($ch); curl_close($ch);
  if($raw===false) throw new RuntimeException('Lexware-Verbindung fehlgeschlagen: '.$err);
  $decoded=json_decode($raw,true); if($status>=400) throw new RuntimeException('Lexware API meldet HTTP '.$status);
  return is_array($decoded)?$decoded:['raw'=>$raw,'status'=>$status];
}
function lexware_items(array $data): array { return $data['content'] ?? $data['items'] ?? (isset($data[0])?$data:[]); }
function lexware_upsert_synced_record(Database $db, string $tenantId, string $entityType, array $item, ?string $localType=null, ?string $localId=null): void {
  $id=(string)($item['id'] ?? ''); if($id==='') return;
  $number=(string)($item['voucherNumber'] ?? $item['documentNumber'] ?? $item['number'] ?? $item['id']);
  $title=(string)($item['title'] ?? $item['subject'] ?? $item['voucherNumber'] ?? $item['documentNumber'] ?? $item['id']);
  $status=(string)($item['status'] ?? $item['voucherStatus'] ?? 'synced');
  $date=(string)($item['voucherDate'] ?? $item['documentDate'] ?? $item['createdDate'] ?? ''); $date=$date!==''?substr($date,0,10):null;
  $amount=$item['totalGrossAmount'] ?? $item['totalAmount'] ?? $item['totalPrice']['totalGrossAmount'] ?? null;
  $currency=(string)($item['currency'] ?? $item['totalPrice']['currency'] ?? 'EUR');
  $contactId=(string)($item['contactId'] ?? $item['address']['contactId'] ?? '');
  $json=json_encode($item,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES);
  $existing=$db->one("SELECT id FROM lexware_synced_records WHERE tenant_id=? AND entity_type=? AND lexware_id=?",[$tenantId,$entityType,$id]);
  if($existing) $db->exec("UPDATE lexware_synced_records SET lexware_number=?, local_entity_type=COALESCE(?,local_entity_type), local_entity_id=COALESCE(?,local_entity_id), title=?, status=?, document_date=?, amount_total=?, currency=?, contact_lexware_id=?, raw_json=?, last_synced_at=NOW(), updated_at=NOW() WHERE id=?",[$number,$localType,$localId,$title,$status,$date,$amount,$currency,$contactId,$json,$existing['id']]);
  else $db->exec("INSERT INTO lexware_synced_records (id,tenant_id,entity_type,lexware_id,lexware_number,local_entity_type,local_entity_id,title,status,document_date,amount_total,currency,contact_lexware_id,raw_json,last_synced_at,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,NOW(),NOW(),NOW())",[uuid(),$tenantId,$entityType,$id,$number,$localType,$localId,$title,$status,$date,$amount,$currency,$contactId,$json]);
}
function lexware_ensure_contact_tables(Database $db): void {
  ensure_lexware_tables($db);
  ensure_suppliers_table($db);
  add_column_if_missing($db,'suppliers','lexware_contact_id','VARCHAR(120) NULL AFTER supplier_no');
}
// Verarbeitet ein Array von Lexware-Kontakten (Kunden + Lieferanten) und liefert Zaehler zurueck.
function lexware_process_contacts(Database $db, string $tenantId, array $items): array {
  $created=0; $updated=0; $linked=0; $supCreated=0; $supUpdated=0;
  foreach($items as $contact){
    if(!is_array($contact) || empty($contact['id'])) continue;
    $lexId=(string)$contact['id'];
    $name=lexware_contact_name($contact);
    $email=lexware_contact_email($contact);
    $phone=lexware_contact_phone($contact);
    $addr=lexware_contact_address($contact);
    $roles=$contact['roles'] ?? [];
    $isCustomer=isset($roles['customer']);
    $isVendor=isset($roles['vendor']);
    $customerNo=$roles['customer']['number'] ?? null;
    $vendorNo=$roles['vendor']['number'] ?? null;

    // Kunde (Standard, wenn keine Rolle gesetzt ist -> als Kunde behandeln)
    if($isCustomer || !$isVendor){
      $existing=$db->one("SELECT * FROM customers WHERE tenant_id=? AND lexware_contact_id=? AND deleted_at IS NULL",[$tenantId,$lexId]);
      if(!$existing && $email) $existing=$db->one("SELECT * FROM customers WHERE tenant_id=? AND email=? AND deleted_at IS NULL LIMIT 1",[$tenantId,$email]);
      if($existing){ $db->exec("UPDATE customers SET name=?, customer_no=COALESCE(customer_no,?), email=COALESCE(?,email), phone=COALESCE(?,phone), address=COALESCE(NULLIF(?,''),address), zip=COALESCE(NULLIF(?,''),zip), city=COALESCE(NULLIF(?,''),city), lexware_contact_id=?, updated_at=NOW() WHERE tenant_id=? AND id=?",[$name,$customerNo,$email,$phone,$addr['address'],$addr['zip'],$addr['city'],$lexId,$tenantId,$existing['id']]); $localId=$existing['id']; $updated++; }
      else { $localId=uuid(); $db->exec("INSERT INTO customers (id,tenant_id,name,customer_no,lexware_contact_id,email,phone,address,zip,city,notes,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?,?,NOW(),NOW())",[$localId,$tenantId,$name,$customerNo,$lexId,$email,$phone,$addr['address'],$addr['zip'],$addr['city'],'Aus Lexware Office synchronisiert']); $created++; }
      $link=$db->one("SELECT id FROM lexware_entity_links WHERE tenant_id=? AND local_entity_type='customer' AND local_entity_id=? AND lexware_entity_type='contact'",[$tenantId,$localId]);
      if(!$link) $db->exec("INSERT INTO lexware_entity_links (id,tenant_id,local_entity_type,local_entity_id,lexware_entity_type,lexware_id,lexware_number,sync_direction,status,last_synced_at,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,NOW(),NOW(),NOW())",[uuid(),$tenantId,'customer',$localId,'contact',$lexId,$customerNo,'inbound','linked']);
      else $db->exec("UPDATE lexware_entity_links SET lexware_id=?, lexware_number=?, last_synced_at=NOW(), updated_at=NOW() WHERE id=?",[$lexId,$customerNo,$link['id']]);
      lexware_upsert_synced_record($db,$tenantId,'contact',$contact,'customer',$localId); $linked++;
    }

    // Lieferant (Vendor) -> suppliers
    if($isVendor){
      $sup=$db->one("SELECT * FROM suppliers WHERE tenant_id=? AND lexware_contact_id=? AND deleted_at IS NULL",[$tenantId,$lexId]);
      if(!$sup && $email) $sup=$db->one("SELECT * FROM suppliers WHERE tenant_id=? AND email=? AND deleted_at IS NULL LIMIT 1",[$tenantId,$email]);
      if($sup){ $db->exec("UPDATE suppliers SET name=?, supplier_no=COALESCE(supplier_no,?), email=COALESCE(?,email), phone=COALESCE(?,phone), address=COALESCE(NULLIF(?,''),address), zip=COALESCE(NULLIF(?,''),zip), city=COALESCE(NULLIF(?,''),city), lexware_contact_id=?, updated_at=NOW() WHERE tenant_id=? AND id=?",[$name,$vendorNo,$email,$phone,$addr['address'],$addr['zip'],$addr['city'],$lexId,$tenantId,$sup['id']]); $supUpdated++; }
      else { $db->exec("INSERT INTO suppliers (id,tenant_id,supplier_no,lexware_contact_id,name,email,phone,address,zip,city,notes,is_active,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?,?,1,NOW(),NOW())",[uuid(),$tenantId,$vendorNo,$lexId,$name,$email,$phone,$addr['address'],$addr['zip'],$addr['city'],'Aus Lexware Office synchronisiert']); $supCreated++; }
    }
  }
  return ['created'=>$created,'updated'=>$updated,'linked'=>$linked,'suppliers_created'=>$supCreated,'suppliers_updated'=>$supUpdated];
}
function lexware_sync_contacts_full(Database $db, array $config, string $tenantId, int $maxPages=300): array {
  lexware_ensure_contact_tables($db);
  $created=0; $updated=0; $linked=0; $supCreated=0; $supUpdated=0;
  $size=100;
  for($page=0;$page<$maxPages;$page++){
    $data=lexware_api_request_array($db,$config,$tenantId,'GET','/v1/contacts?page='.$page.'&size='.$size);
    $items=lexware_items($data);
    if(!is_array($items) || count($items)===0) break;
    $r=lexware_process_contacts($db,$tenantId,$items);
    $created+=$r['created']; $updated+=$r['updated']; $linked+=$r['linked']; $supCreated+=$r['suppliers_created']; $supUpdated+=$r['suppliers_updated'];
    if(isset($data['last']) && $data['last']===true) break;
    if(count($items) < $size) break;
  }
  return ['created'=>$created,'updated'=>$updated,'linked'=>$linked,'suppliers_created'=>$supCreated,'suppliers_updated'=>$supUpdated];
}
function lexware_sync_documents(Database $db, array $config, string $tenantId): array {
  ensure_lexware_tables($db); $summary=['quotations'=>0,'invoices'=>0,'vouchers'=>0,'errors'=>[]];
  foreach([['quotations','/v1/quotations?page=0&size=100'],['invoices','/v1/invoices?page=0&size=100'],['vouchers','/v1/vouchers?page=0&size=100']] as $target){
    try { $data=lexware_api_request_array($db,$config,$tenantId,'GET',$target[1]); $items=lexware_items($data); foreach($items as $item){ if(is_array($item)){ lexware_upsert_synced_record($db,$tenantId,$target[0],$item); $summary[$target[0]]++; } } }
    catch(Throwable $e){ $summary['errors'][]=$target[0].': '.$e->getMessage(); }
  }
  return $summary;
}
function lexware_run_auto_sync(Database $db, array $config, string $tenantId, bool $force=false): array {
  ensure_lexware_tables($db);
  $conn=$db->one("SELECT * FROM lexware_connections WHERE tenant_id=? AND deleted_at IS NULL ORDER BY created_at DESC LIMIT 1",[$tenantId]);
  if(!$conn || (int)$conn['is_active']!==1) return ['ok'=>true,'skipped'=>true,'reason'=>'Lexware nicht aktiv'];
  $interval=max(1,(int)($conn['auto_sync_interval_minutes'] ?? 4));
  if(!$force && !empty($conn['last_auto_sync_at']) && strtotime((string)$conn['last_auto_sync_at']) > time()-($interval*60)) return ['ok'=>true,'skipped'=>true,'reason'=>'Noch nicht fällig','next_in_seconds'=>($interval*60)-(time()-strtotime((string)$conn['last_auto_sync_at']))];
  $runId='last';
  $summary=[];
  try {
    if((int)($conn['sync_contacts'] ?? 1)===1 || (int)($conn['auto_sync_customers'] ?? 1)===1) $summary['contacts']=lexware_sync_contacts_full($db,$config,$tenantId,300);
    if((int)($conn['sync_quotations'] ?? 1)===1 || (int)($conn['sync_invoices'] ?? 1)===1 || (int)($conn['sync_vouchers'] ?? 1)===1) $summary['documents']=lexware_sync_documents($db,$config,$tenantId);
    $db->exec("UPDATE lexware_connections SET last_auto_sync_at=NOW(), last_sync_status='success', last_sync_summary=?, last_error=NULL, updated_at=NOW() WHERE id=?",[json_encode($summary,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES),$conn['id']]);
    /* Erfolgreiche Routine-Syncs werden nicht einzeln geloggt. */
    return ['ok'=>true,'run_id'=>$runId,'summary'=>$summary];
  } catch(Throwable $e) {
    $db->exec("UPDATE lexware_connections SET last_auto_sync_at=NOW(), last_sync_status='failed', last_sync_summary=?, last_error=?, updated_at=NOW() WHERE id=?",[json_encode(['error'=>$e->getMessage()],JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES),$e->getMessage(),$conn['id']]);
    lexware_log($db,$tenantId,'auto_sync','failed',$e->getMessage(),null,$summary);
    return ['ok'=>false,'run_id'=>$runId,'error'=>$e->getMessage(),'summary'=>$summary];
  }
}

/**
 * Ist die Anmeldung derzeit gesperrt?
 *
 * Gezaehlt werden Fehlversuche der letzten 15 Minuten seit dem letzten
 * ERFOLGREICHEN Login – ein gelungener Login setzt den Zaehler also zurueck.
 */
function login_lock_state(Database $db, string $email, string $ip): array {
  $window=15;   // Minuten
  $maxEmail=5;  // Fehlversuche je Kennung
  $maxIp=20;    // Fehlversuche je Herkunftsadresse
  try {
    $byEmail=(int)($db->one("SELECT COUNT(*) c FROM login_logs
      WHERE email=? AND success=0 AND created_at > NOW() - INTERVAL ? MINUTE
        AND created_at > COALESCE((SELECT MAX(created_at) FROM login_logs l2 WHERE l2.email=login_logs.email AND l2.success=1),'1970-01-01')",
      [$email,$window])['c'] ?? 0);
    $byIp=$ip==='' ? 0 : (int)($db->one("SELECT COUNT(*) c FROM login_logs
      WHERE ip=? AND success=0 AND created_at > NOW() - INTERVAL ? MINUTE",[$ip,$window])['c'] ?? 0);
  } catch(Throwable $e) {
    // Bei einem Fehler in der Zaehlung darf die Anmeldung nicht blockieren.
    error_log('login_lock_state: '.$e->getMessage());
    return ['locked'=>false,'minutes'=>0];
  }
  if($byEmail>=$maxEmail || $byIp>=$maxIp) return ['locked'=>true,'minutes'=>$window];
  return ['locked'=>false,'minutes'=>0];
}

/** Wie viele Versuche bleiben bis zur Sperre? */
function login_attempts_left(Database $db, string $email, string $ip): int {
  try {
    $c=(int)($db->one("SELECT COUNT(*) c FROM login_logs
      WHERE email=? AND success=0 AND created_at > NOW() - INTERVAL 15 MINUTE",[$email])['c'] ?? 0);
    return max(0, 5-$c);
  } catch(Throwable $e) { return 0; }
}

function issue_login_response(Database $db, array $config, array $u, Request $req): array {
  $perms=$db->all("SELECT DISTINCT p.code FROM permissions p JOIN role_permissions rp ON rp.permission_id=p.id JOIN user_roles ur ON ur.role_id=rp.role_id WHERE ur.user_id=?",[$u['id']]);
  $permissions=array_map(fn($x)=>$x['code'],$perms);
  $roles=$db->all("SELECT DISTINCT r.code, r.name FROM roles r JOIN user_roles ur ON ur.role_id=r.id WHERE ur.user_id=? AND r.deleted_at IS NULL",[$u['id']]);
  $roleCodes=array_values(array_filter(array_map(fn($x)=>$x['code'] ?? null,$roles)));
  $roleNames=array_values(array_filter(array_map(fn($x)=>$x['name'] ?? null,$roles)));
  $token=Jwt::sign(['sub'=>$u['id'],'tenant_id'=>$u['tenant_id'],'email'=>$u['email']],$config['app']['jwt_secret'],(int)$config['app']['jwt_ttl_minutes']);
  $refresh=bin2hex(random_bytes(48));
  $db->exec("INSERT INTO refresh_tokens (id,tenant_id,user_id,token_hash,expires_at,created_at,updated_at) VALUES (?,?,?,?,DATE_ADD(NOW(), INTERVAL ? DAY),NOW(),NOW())",[uuid(),$u['tenant_id'],$u['id'],hash('sha256',$refresh),(int)$config['app']['refresh_ttl_days']]);
  $db->exec("INSERT INTO device_sessions (id,tenant_id,user_id,device_name,device_fingerprint,ip,user_agent,status,last_seen_at,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,NOW(),NOW(),NOW())",[uuid(),$u['tenant_id'],$u['id'],$req->body['device_name']??'Browser',$req->body['device_fingerprint']??null,$_SERVER['REMOTE_ADDR']??null,$_SERVER['HTTP_USER_AGENT']??null,'active']);
  return ['token'=>$token,'refresh_token'=>$refresh,'user'=>['id'=>$u['id'],'name'=>$u['name'],'email'=>$u['email'],'tenant_id'=>$u['tenant_id'],'tenant_slug'=>$u['tenant_slug'],'tenant_name'=>$u['tenant_name'],'permissions'=>$permissions,'role_codes'=>$roleCodes,'role_names'=>$roleNames,'must_change_password'=>(int)($u['must_change_password'] ?? 0)===1]];
}
function app_base_url(array $config): string {
  $url=trim((string)($config['app']['url'] ?? ''));
  if($url !== '') return rtrim($url,'/');
  $scheme = (!empty($_SERVER['HTTPS']) && $_SERVER['HTTPS'] !== 'off') ? 'https' : 'http';
  $host = $_SERVER['HTTP_HOST'] ?? 'localhost';
  return $scheme.'://'.$host;
}
function microsoft_sso_config(array $config): array {
  $ms=$config['microsoft_sso'] ?? [];
  $tenant=trim((string)($ms['tenant_id'] ?? 'common')) ?: 'common';
  $clientId=trim((string)($ms['client_id'] ?? ''));
  $clientSecret=trim((string)($ms['client_secret'] ?? ''));
  $redirect=trim((string)($ms['redirect_uri'] ?? '')) ?: app_base_url($config).'/api/auth/microsoft/callback';
  $enabled=truthy($ms['enabled'] ?? false) && $clientId !== '' && $clientSecret !== '';
  return ['enabled'=>$enabled,'tenant_id'=>$tenant,'client_id'=>$clientId,'client_secret'=>$clientSecret,'redirect_uri'=>$redirect];
}
function ensure_sso_tables(Database $db): void {
  $db->exec("CREATE TABLE IF NOT EXISTS sso_login_tickets (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, user_id CHAR(32) NOT NULL, ticket_hash CHAR(64) NOT NULL, provider VARCHAR(40) NOT NULL, expires_at DATETIME NOT NULL, used_at DATETIME NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, KEY idx_sso_ticket_hash(ticket_hash), KEY idx_sso_user(tenant_id,user_id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
  $db->exec("CREATE TABLE IF NOT EXISTS sso_settings (id CHAR(32) PRIMARY KEY, tenant_id CHAR(32) NOT NULL, provider VARCHAR(40) NOT NULL DEFAULT 'microsoft', is_enabled TINYINT(1) NOT NULL DEFAULT 0, provider_tenant_id VARCHAR(120) NOT NULL DEFAULT 'common', client_id VARCHAR(160) NULL, client_secret_encrypted TEXT NULL, redirect_uri VARCHAR(500) NULL, allowed_domains TEXT NULL, auto_provision TINYINT(1) NOT NULL DEFAULT 0, default_role_id CHAR(32) NULL, created_at DATETIME NOT NULL, updated_at DATETIME NULL, deleted_at DATETIME NULL, UNIQUE KEY uq_sso_provider (tenant_id,provider)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
}
function sso_public_config(Database $db, array $config, string $tenantId): array {
  ensure_sso_tables($db);
  $row=$db->one("SELECT * FROM sso_settings WHERE tenant_id=? AND provider='microsoft' AND deleted_at IS NULL",[$tenantId]);
  $file=microsoft_sso_config($config);
  if(!$row) return ['provider'=>'microsoft','enabled'=>$file['enabled'],'provider_tenant_id'=>$file['tenant_id'],'client_id'=>$file['client_id'] ? 'configured' : '', 'redirect_uri'=>$file['redirect_uri'],'has_client_secret'=>$file['client_secret']!=='','allowed_domains'=>'','auto_provision'=>0,'default_role_id'=>null,'source'=>'local.php'];
  return ['provider'=>'microsoft','enabled'=>(int)$row['is_enabled']===1 && !empty($row['client_id']) && !empty($row['client_secret_encrypted']),'provider_tenant_id'=>$row['provider_tenant_id'],'client_id'=>$row['client_id'],'redirect_uri'=>$row['redirect_uri'] ?: app_base_url($config).'/api/auth/microsoft/callback','has_client_secret'=>!empty($row['client_secret_encrypted']),'allowed_domains'=>$row['allowed_domains'] ?? '','auto_provision'=>(int)($row['auto_provision'] ?? 0),'default_role_id'=>$row['default_role_id'] ?? null,'source'=>'database'];
}
function microsoft_sso_runtime(Database $db, array $config, string $tenantId): array {
  ensure_sso_tables($db);
  $row=$db->one("SELECT * FROM sso_settings WHERE tenant_id=? AND provider='microsoft' AND deleted_at IS NULL",[$tenantId]);
  if(!$row) return microsoft_sso_config($config);
  $secret=secret_decrypt($row['client_secret_encrypted'] ?? null, $config['app']['jwt_secret'] ?? 'estrichweb') ?: '';
  $redirect=trim((string)($row['redirect_uri'] ?? '')) ?: app_base_url($config).'/api/auth/microsoft/callback';
  return ['enabled'=>(int)$row['is_enabled']===1 && !empty($row['client_id']) && $secret !== '', 'tenant_id'=>trim((string)($row['provider_tenant_id'] ?: 'common')), 'client_id'=>(string)$row['client_id'], 'client_secret'=>$secret, 'redirect_uri'=>$redirect, 'allowed_domains'=>$row['allowed_domains'] ?? '', 'auto_provision'=>(int)($row['auto_provision'] ?? 0), 'default_role_id'=>$row['default_role_id'] ?? null];
}
function default_tenant_id(Database $db, Request $req): ?string {
  $slug=trim((string)($req->query['tenant_slug'] ?? $req->body['tenant_slug'] ?? ''));
  if($slug!=='') { $row=$db->one("SELECT id FROM tenants WHERE slug=? AND deleted_at IS NULL",[$slug]); if($row) return $row['id']; }
  $row=$db->one("SELECT id FROM tenants WHERE deleted_at IS NULL ORDER BY created_at ASC LIMIT 1");
  return $row['id'] ?? null;
}
function jwt_decode_unverified(?string $jwt): ?array {
  if(!$jwt || substr_count($jwt,'.')!==2) return null;
  $parts=explode('.',$jwt); $payload=$parts[1];
  $payload=strtr($payload,'-_','+/'); $payload.=str_repeat('=', (4 - strlen($payload) % 4) % 4);
  $json=base64_decode($payload,true); if($json===false) return null;
  $data=json_decode($json,true); return is_array($data)?$data:null;
}
function mime_from_path(string $path): string {
  $ext=strtolower(pathinfo($path,PATHINFO_EXTENSION));
  $map=['jpg'=>'image/jpeg','jpeg'=>'image/jpeg','png'=>'image/png','webp'=>'image/webp','gif'=>'image/gif','heic'=>'image/heic','pdf'=>'application/pdf','mp4'=>'video/mp4','webm'=>'video/webm','mov'=>'video/quicktime','m4v'=>'video/x-m4v','doc'=>'application/msword','docx'=>'application/vnd.openxmlformats-officedocument.wordprocessingml.document','xls'=>'application/vnd.ms-excel','xlsx'=>'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'];
  if(isset($map[$ext])) return $map[$ext];
  if(function_exists('mime_content_type') && is_file($path)) { $m=@mime_content_type($path); if($m) return $m; }
  return 'application/octet-stream';
}
// Der beim Upload gespeicherte MIME-Typ ist haeufig leer oder generisch
// (application/octet-stream), z. B. bei Uploads von Mobilgeraeten. Mit
// X-Content-Type-Options: nosniff weigert sich der Browser dann, die Datei
// inline anzuzeigen -> die Dokumentenvorschau in der Bauakte bleibt leer.
// Darum wird ein generischer Typ immer aus der Dateiendung nachgebessert.
// Zentrale Versionskennung – wird von /health und /public/app-info genutzt.
const APP_VERSION = '2.0.0';

function reliable_mime(?string $storedMime, string $path): string {
  $stored=strtolower(trim((string)$storedMime));
  $generic=['','application/octet-stream','binary/octet-stream','application/download','application/force-download','content/unknown','*/*'];
  if(in_array($stored,$generic,true)) return mime_from_path($path);
  $byPath=mime_from_path($path);
  // Endung gewinnt, wenn sie einen konkreten Typ liefert und der gespeicherte
  // Wert offensichtlich nicht dazu passt (z. B. "image/jpeg" fuer eine PDF).
  if($byPath!=='application/octet-stream'){
    $storedMain=explode('/',$stored)[0]; $pathMain=explode('/',$byPath)[0];
    if($storedMain!==$pathMain) return $byPath;
  }
  return $stored;
}
function stream_private_file(string $path, ?string $mime=null, ?string $downloadName=null, string $disposition='inline'): void {
  $resolved=$path;
  if($path && !is_file($resolved)) {
    $storageRoot=realpath(__DIR__.'/../storage') ?: (__DIR__.'/../storage');
    $candidates=[];
    $normalized=str_replace('\\','/',$path);
    $pos=strpos($normalized,'/storage/');
    if($pos!==false) $candidates[]=$storageRoot.'/'.ltrim(substr($normalized,$pos+9),'/');
    $candidates[]=$storageRoot.'/'.basename($path);
    foreach($candidates as $candidate) { if(is_file($candidate)) { $resolved=$candidate; break; } }
  }
  if(!$path || !is_file($resolved)) Response::error('Datei nicht gefunden',404);
  header('Content-Type: '.($mime ?: mime_from_path($resolved)));
  header('Content-Length: '.filesize($resolved));
  header('Accept-Ranges: bytes');
  header('Cache-Control: private, max-age=3600');
  header('X-Content-Type-Options: nosniff');
  if($downloadName) header('Content-Disposition: '.$disposition.'; filename="'.str_replace('"','',$downloadName).'"');
  readfile($resolved); exit;
}

// ===========================================================================
// E-Mail (Benachrichtigungen / Zuweisungen) – eigenstaendiger SMTP-Client
// ===========================================================================
function app_email_config(Database $db, ?string $tid): ?array {
  try { ensure_app_settings_table($db); } catch(Throwable $e) {}
  $row=null;
  if($tid) $row=$db->one("SELECT value_json FROM app_settings WHERE tenant_id=? AND setting_key='email'",[$tid]);
  if(!$row) $row=$db->one("SELECT value_json FROM app_settings WHERE setting_key='email' AND value_json IS NOT NULL ORDER BY updated_at DESC LIMIT 1");
  if(!$row) return null;
  $cfg=json_decode($row['value_json'],true);
  return is_array($cfg) ? $cfg : null;
}

function smtp_dialog($fp, string $cmd, array $expect): array {
  if($cmd!=='') fwrite($fp,$cmd."\r\n");
  $data='';
  while(($line=fgets($fp,515))!==false){ $data.=$line; if(strlen($line)<4 || $line[3]===' ') break; }
  $code=(int)substr(trim($data),0,3);
  return ['code'=>$code,'data'=>$data,'ok'=>in_array($code,$expect,true)];
}

// Sendet eine HTML-Mail ueber SMTP. Rueckgabe: ['ok'=>bool,'error'=>string]
function send_smtp_mail(array $cfg, string $toEmail, string $toName, string $subject, string $htmlBody): array {
  $host=trim((string)($cfg['host'] ?? '')); $port=(int)($cfg['port'] ?? 587);
  $user=(string)($cfg['user'] ?? ''); $pass=(string)($cfg['pass'] ?? '');
  $secure=strtolower((string)($cfg['secure'] ?? ($port===465?'ssl':'tls'))); // ssl | tls | none
  $fromEmail=trim((string)($cfg['from_email'] ?? $user)); $fromName=(string)($cfg['from_name'] ?? 'EstrichWeb');
  if($toEmail==='' || !filter_var($toEmail,FILTER_VALIDATE_EMAIL)) return ['ok'=>false,'error'=>'Ungueltige Empfaenger-Adresse'];

  // Fallback auf PHP mail(), wenn kein SMTP-Host konfiguriert ist
  if($host===''){
    $headers="MIME-Version: 1.0\r\nContent-Type: text/html; charset=UTF-8\r\nFrom: ".mb_encode_mimeheader($fromName)." <".$fromEmail.">\r\n";
    $ok=@mail($toEmail,'=?UTF-8?B?'.base64_encode($subject).'?=',$htmlBody,$headers);
    return ['ok'=>$ok,'error'=>$ok?'':'PHP mail() fehlgeschlagen'];
  }

  $transport=($secure==='ssl')? 'ssl://'.$host : $host;
  $ctxOpt=stream_context_create(['ssl'=>['verify_peer'=>false,'verify_peer_name'=>false,'allow_self_signed'=>true]]);
  $fp=@stream_socket_client($transport.':'.$port,$errno,$errstr,15,STREAM_CLIENT_CONNECT,$ctxOpt);
  if(!$fp) return ['ok'=>false,'error'=>"Verbindung fehlgeschlagen: $errstr ($errno)"];
  stream_set_timeout($fp,15);
  try {
    if(!smtp_dialog($fp,'',[220])['ok']) throw new RuntimeException('Kein 220-Greeting');
    $ehloHost=preg_replace('/[^A-Za-z0-9.\-]/','',(string)($_SERVER['SERVER_NAME'] ?? 'localhost')) ?: 'localhost';
    if(!smtp_dialog($fp,'EHLO '.$ehloHost,[250])['ok']) { if(!smtp_dialog($fp,'HELO '.$ehloHost,[250])['ok']) throw new RuntimeException('EHLO abgelehnt'); }
    if($secure==='tls'){
      if(!smtp_dialog($fp,'STARTTLS',[220])['ok']) throw new RuntimeException('STARTTLS abgelehnt');
      if(!@stream_socket_enable_crypto($fp,true,STREAM_CRYPTO_METHOD_TLS_CLIENT|STREAM_CRYPTO_METHOD_TLSv1_2_CLIENT)) throw new RuntimeException('TLS-Handshake fehlgeschlagen');
      smtp_dialog($fp,'EHLO '.$ehloHost,[250]);
    }
    if($user!==''){
      if(!smtp_dialog($fp,'AUTH LOGIN',[334])['ok']) throw new RuntimeException('AUTH LOGIN abgelehnt');
      if(!smtp_dialog($fp,base64_encode($user),[334])['ok']) throw new RuntimeException('Benutzer abgelehnt');
      if(!smtp_dialog($fp,base64_encode($pass),[235])['ok']) throw new RuntimeException('Anmeldung fehlgeschlagen (Benutzer/Passwort)');
    }
    if(!smtp_dialog($fp,'MAIL FROM:<'.$fromEmail.'>',[250])['ok']) throw new RuntimeException('MAIL FROM abgelehnt');
    if(!smtp_dialog($fp,'RCPT TO:<'.$toEmail.'>',[250,251])['ok']) throw new RuntimeException('Empfaenger abgelehnt');
    if(!smtp_dialog($fp,'DATA',[354])['ok']) throw new RuntimeException('DATA abgelehnt');
    $headers =
      'From: '.mb_encode_mimeheader($fromName).' <'.$fromEmail.">\r\n".
      'To: '.($toName!==''?mb_encode_mimeheader($toName).' ':'').'<'.$toEmail.">\r\n".
      'Subject: =?UTF-8?B?'.base64_encode($subject)."?=\r\n".
      "MIME-Version: 1.0\r\n".
      "Content-Type: text/html; charset=UTF-8\r\n".
      "Content-Transfer-Encoding: base64\r\n";
    $body=chunk_split(base64_encode($htmlBody));
    fwrite($fp,$headers."\r\n".$body."\r\n.\r\n");
    $end=smtp_dialog($fp,'',[250]);
    smtp_dialog($fp,'QUIT',[221]);
    fclose($fp);
    return $end['ok'] ? ['ok'=>true,'error'=>''] : ['ok'=>false,'error'=>'Server hat Nachricht abgelehnt'];
  } catch(Throwable $e){ @fclose($fp); return ['ok'=>false,'error'=>$e->getMessage()]; }
}

// Bequemer Wrapper: liest Konfig, prueft 'enabled', sendet. Wirft nie.
function notify_by_email(Database $db, ?string $tid, ?string $toEmail, string $toName, string $subject, string $htmlBody): bool {
  try {
    if(!$toEmail) return false;
    $cfg=app_email_config($db,$tid);
    if(!$cfg || empty($cfg['enabled'])) return false;
    $res=send_smtp_mail($cfg,(string)$toEmail,$toName,$subject,$htmlBody);
    return !empty($res['ok']);
  } catch(Throwable $e){ error_log('notify_by_email: '.$e->getMessage()); return false; }
}

function email_layout(string $title, string $bodyHtml, string $footer=''): string {
  $t=htmlspecialchars($title,ENT_QUOTES,'UTF-8');
  return '<div style="font-family:Arial,Helvetica,sans-serif;max-width:560px;margin:0 auto;color:#0f172a">'
    .'<div style="background:#1f3a8a;color:#fff;padding:16px 20px;border-radius:10px 10px 0 0;font-size:18px;font-weight:bold">EstrichWeb</div>'
    .'<div style="border:1px solid #e2e8f0;border-top:0;border-radius:0 0 10px 10px;padding:20px">'
    .'<h2 style="margin:0 0 12px;font-size:18px">'.$t.'</h2>'.$bodyHtml
    .($footer!==''?'<p style="margin-top:18px;color:#64748b;font-size:12px">'.$footer.'</p>':'')
    .'</div></div>';
}

function store_photo(Database $db, array $config, array $user, array $file, array $meta): array {
  ensure_photo_columns($db);
  $tid=$user['tenant_id']; $id=uuid(); $siteId=(string)($meta['site_id'] ?? 'unknown-site');
  $year=date('Y'); $month=date('m');
  $base=rtrim($config['app']['storage_path'],'/').'/photos/'.$tid.'/'.$year.'/'.$month.'/'.$siteId;
  if(!is_dir($base)) mkdir($base,0775,true);
  $useWebp=function_exists('imagewebp'); $ext=$useWebp?'webp':'jpg';
  $compressed=$base.'/'.$id.'.'.$ext; $thumb=$base.'/'.$id.'_thumb.'.$ext; $originalPath=null;
  if (truthy($meta['store_original'] ?? false)) {
    $safeExt=strtolower(pathinfo($file['name'],PATHINFO_EXTENSION) ?: 'jpg');
    if(!in_array($safeExt,['jpg','jpeg','png','webp','heic'],true)) $safeExt='jpg';
    $originalPath=$base.'/'.$id.'_original.'.$safeExt;
    move_uploaded_file($file['tmp_name'],$originalPath);
    $source=$originalPath;
  } else {
    $source=$file['tmp_name'];
  }
  $main=write_resized_image($source,$compressed,1600,72);
  $thumbnail=write_resized_image($source,$thumb,360,62);
  ensure_file_project_columns($db);
  $db->exec("INSERT INTO photos (id,tenant_id,site_id,project_id,room_id,caption,category,original_path,compressed_path,thumbnail_path,original_size_bytes,compressed_size_bytes,thumbnail_size_bytes,width_px,height_px,taken_at,gps_lat,gps_lng,annotations_json,is_customer_visible,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,NOW(),NOW())",[
    $id,$tid,$siteId,$meta['project_id']??null,$meta['room_id']??null,$meta['caption']??($meta['work_done']??null),$meta['category']??'daily_documentation',$originalPath,$compressed,$thumb,$file['size']??null,$main['size']??null,$thumbnail['size']??null,$main['width']??null,$main['height']??null,$meta['taken_at']??null,$meta['gps_lat']??null,$meta['gps_lng']??null,$meta['annotations_json']??null,truthy($meta['customer_visible']??false)?1:0
  ]);
  return $db->one("SELECT * FROM photos WHERE id=?",[$id]) ?: ['id'=>$id];
}

// Wie store_photo, aber fuer eine bereits im Dateisystem liegende Datei
// (z. B. nach move_uploaded_file im /documents/upload-Handler). Nutzt copy()
// statt move_uploaded_file, damit Bilder aus dem Foto-Tab korrekt landen.
function store_photo_from_path(Database $db, array $config, array $user, array $file, array $meta): array {
  ensure_photo_columns($db);
  $tid=$user['tenant_id']; $id=uuid(); $siteId=(string)($meta['site_id'] ?? 'unknown-site');
  $year=date('Y'); $month=date('m');
  $base=rtrim($config['app']['storage_path'],'/').'/photos/'.$tid.'/'.$year.'/'.$month.'/'.$siteId;
  if(!is_dir($base)) mkdir($base,0775,true);
  $useWebp=function_exists('imagewebp'); $ext=$useWebp?'webp':'jpg';
  $compressed=$base.'/'.$id.'.'.$ext; $thumb=$base.'/'.$id.'_thumb.'.$ext; $originalPath=null;
  $src=(string)($file['tmp_name'] ?? '');
  if (truthy($meta['store_original'] ?? false) && is_file($src)) {
    $safeExt=strtolower(pathinfo($file['name'],PATHINFO_EXTENSION) ?: 'jpg');
    if(!in_array($safeExt,['jpg','jpeg','png','webp','heic'],true)) $safeExt='jpg';
    $originalPath=$base.'/'.$id.'_original.'.$safeExt;
    @copy($src,$originalPath);
    $source=is_file($originalPath)?$originalPath:$src;
  } else {
    $source=$src;
  }
  $main=write_resized_image($source,$compressed,1600,72);
  $thumbnail=write_resized_image($source,$thumb,360,62);
  ensure_file_project_columns($db);
  $db->exec("INSERT INTO photos (id,tenant_id,site_id,project_id,room_id,caption,category,original_path,compressed_path,thumbnail_path,original_size_bytes,compressed_size_bytes,thumbnail_size_bytes,width_px,height_px,taken_at,gps_lat,gps_lng,annotations_json,is_customer_visible,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,NOW(),?,?,?,?,NOW(),NOW())",[
    $id,$tid,$siteId,$meta['project_id']??null,$meta['room_id']??null,$meta['caption']??null,$meta['category']??'bauakte',$originalPath,$compressed,$thumb,$file['size']??null,$main['size']??null,$thumbnail['size']??null,$main['width']??null,$main['height']??null,$meta['gps_lat']??null,$meta['gps_lng']??null,$meta['annotations_json']??null,truthy($meta['customer_visible']??false)?1:0
  ]);
  return $db->one("SELECT * FROM photos WHERE id=?",[$id]) ?: ['id'=>$id];
}

function store_document_file(Database $db, array $config, array $user, array $file, array $meta): array {
  $tid=$user['tenant_id']; $id=uuid();
  $allowed=['pdf','doc','docx','xls','xlsx','jpg','jpeg','png','webp','heic','mp4','mov','m4v','webm'];
  $ext=strtolower(pathinfo($file['name'],PATHINFO_EXTENSION) ?: 'bin');
  if(!in_array($ext,$allowed,true)) Response::error('Dateityp nicht erlaubt: '.$ext,422);
  if(($file['size'] ?? 0) > (int)$config['app']['upload_max_bytes']) Response::error('Datei ist zu groß',413);
  $base=rtrim($config['app']['storage_path'],'/').'/documents/'.$tid.'/'.date('Y').'/'.date('m');
  if(!is_dir($base)) mkdir($base,0775,true);

  // Speicherschonend: Bilddokumente serverseitig herunterskalieren/konvertieren.
  $imageExts=['jpg','jpeg','png','webp','heic'];
  $isImage=in_array($ext,$imageExts,true) && function_exists('imagecreatefromstring') && @getimagesize($file['tmp_name']);
  $storedMime=$file['type']??null; $storedSize=$file['size']??null; $storedExt=$ext;
  if($isImage){
    $useWebp=function_exists('imagewebp'); $storedExt=$useWebp?'webp':'jpg';
    $path=$base.'/'.$id.'.'.$storedExt;
    $res=write_resized_image($file['tmp_name'],$path,2000,78); // max 2000px Längsseite
    // Nur die komprimierte Variante übernehmen, wenn sie wirklich kleiner ist.
    if(($res['size']??0) > 0 && ($res['size'] < ($file['size']??PHP_INT_MAX))){
      $storedMime=$useWebp?'image/webp':'image/jpeg'; $storedSize=$res['size'];
    } else {
      // Komprimierung brachte nichts -> Original behalten.
      @unlink($path); $storedExt=$ext; $path=$base.'/'.$id.'.'.$ext;
      if(!move_uploaded_file($file['tmp_name'],$path)){ if(!@copy($file['tmp_name'],$path)) Response::error('Upload fehlgeschlagen',500); }
    }
  } else {
    $path=$base.'/'.$id.'.'.$ext;
    if(!move_uploaded_file($file['tmp_name'],$path)) Response::error('Upload fehlgeschlagen',500);
  }
  ensure_file_project_columns($db);
  $db->exec("INSERT INTO documents (id,tenant_id,site_id,project_id,room_id,name,original_name,mime_type,size_bytes,storage_path,category,created_by,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,NOW(),NOW())",[$id,$tid,$meta['site_id']??null,$meta['project_id']??null,$meta['room_id']??null,$meta['name']??$file['name'],$file['name'],$storedMime,$storedSize,$path,$meta['category']??'general',$user['id']]);
  return $db->one("SELECT * FROM documents WHERE id=?",[$id]) ?: ['id'=>$id];
}



function ensure_activity_presence(Database $db): void {
  $db->exec("CREATE TABLE IF NOT EXISTS activity_presence (
    id CHAR(32) PRIMARY KEY,
    tenant_id CHAR(32) NOT NULL,
    user_id CHAR(32) NOT NULL,
    project_id CHAR(32) NULL,
    site_id CHAR(32) NULL,
    screen VARCHAR(120) NULL,
    last_seen_at DATETIME NOT NULL,
    created_at DATETIME NOT NULL,
    updated_at DATETIME NULL,
    UNIQUE KEY uq_activity_user_context (tenant_id,user_id,project_id,site_id),
    KEY idx_activity_project (tenant_id,project_id,last_seen_at),
    KEY idx_activity_site (tenant_id,site_id,last_seen_at)
  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
}

function ensure_suppliers_table(Database $db): void {
  static $done=false; if($done) return; $done=true;
  $db->exec("CREATE TABLE IF NOT EXISTS suppliers (
    id CHAR(32) PRIMARY KEY,
    tenant_id CHAR(32) NOT NULL,
    supplier_no VARCHAR(80) NULL,
    name VARCHAR(255) NOT NULL,
    contact_person VARCHAR(255) NULL,
    email VARCHAR(255) NULL,
    phone VARCHAR(80) NULL,
    address VARCHAR(255) NULL,
    zip VARCHAR(20) NULL,
    city VARCHAR(120) NULL,
    tax_no VARCHAR(120) NULL,
    iban VARCHAR(80) NULL,
    notes TEXT NULL,
    is_active TINYINT(1) NOT NULL DEFAULT 1,
    created_at DATETIME NOT NULL,
    updated_at DATETIME NULL,
    deleted_at DATETIME NULL,
    KEY idx_suppliers_tenant (tenant_id,name),
    KEY idx_suppliers_active (tenant_id,is_active)
  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
}
function ensure_material_orders_table(Database $db): void {
  static $done=false; if($done) return; $done=true;
  ensure_suppliers_table($db);
  $db->exec("CREATE TABLE IF NOT EXISTS material_orders (
    id CHAR(32) PRIMARY KEY,
    tenant_id CHAR(32) NOT NULL,
    project_id CHAR(32) NOT NULL,
    site_id CHAR(32) NULL,
    supplier_id CHAR(32) NULL,
    order_no VARCHAR(80) NOT NULL,
    status VARCHAR(40) NOT NULL DEFAULT 'to_order',
    requested_delivery_date DATE NULL,
    delivery_address VARCHAR(255) NULL,
    contact_name VARCHAR(255) NULL,
    notes TEXT NULL,
    created_by CHAR(32) NULL,
    created_at DATETIME NOT NULL,
    updated_at DATETIME NULL,
    deleted_at DATETIME NULL,
    UNIQUE KEY uq_material_order_no (tenant_id,order_no),
    KEY idx_material_orders_project (tenant_id,project_id,status),
    KEY idx_material_orders_supplier (tenant_id,supplier_id)
  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
  $db->exec("CREATE TABLE IF NOT EXISTS material_order_items (
    id CHAR(32) PRIMARY KEY,
    tenant_id CHAR(32) NOT NULL,
    order_id CHAR(32) NOT NULL,
    position_no INT NOT NULL DEFAULT 1,
    description VARCHAR(255) NOT NULL,
    quantity DECIMAL(12,3) NULL,
    unit VARCHAR(40) NULL,
    notes TEXT NULL,
    created_at DATETIME NOT NULL,
    updated_at DATETIME NULL,
    deleted_at DATETIME NULL,
    KEY idx_material_order_items_order (tenant_id,order_id,position_no)
  ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci");
}
function next_material_order_no(Database $db, string $tenantId): string {
  ensure_material_orders_table($db);
  $year=date('Y');
  $row=$db->one("SELECT order_no FROM material_orders WHERE tenant_id=? AND order_no LIKE ? ORDER BY order_no DESC LIMIT 1",[$tenantId,'MB-'.$year.'-%']);
  $next=1;
  if($row && preg_match('/MB-'.preg_quote($year,'/').'-(\d+)/',(string)$row['order_no'],$m)) $next=((int)$m[1])+1;
  return 'MB-'.$year.'-'.str_pad((string)$next,3,'0',STR_PAD_LEFT);
}
function material_order_status_valid(string $status): bool { return in_array($status,['to_order','ordered','done','cancelled'],true); }
function material_order_with_items(Database $db, string $tenantId, string $id): ?array {
  $order=$db->one("SELECT mo.*, s.name supplier_name, s.contact_person supplier_contact_person, s.email supplier_email, s.phone supplier_phone, s.address supplier_address, s.zip supplier_zip, s.city supplier_city FROM material_orders mo LEFT JOIN suppliers s ON s.id=mo.supplier_id AND s.tenant_id=mo.tenant_id AND s.deleted_at IS NULL WHERE mo.tenant_id=? AND mo.id=? AND mo.deleted_at IS NULL",[$tenantId,$id]);
  if(!$order) return null;
  $order['items']=safe_query($db,"SELECT * FROM material_order_items WHERE tenant_id=? AND order_id=? AND deleted_at IS NULL ORDER BY position_no ASC, created_at ASC",[$tenantId,$id]);
  return $order;
}
function save_material_order_items(Database $db, string $tenantId, string $orderId, $items): void {
  if(!is_array($items)) $items=[];
  soft_delete_where($db,'material_order_items',"tenant_id=? AND order_id=?",[$tenantId,$orderId]);
  $pos=1;
  foreach($items as $item){
    if(!is_array($item)) continue;
    $desc=trim((string)($item['description'] ?? ''));
    if($desc==='') continue;
    $qty=is_numeric($item['quantity'] ?? null) ? (float)$item['quantity'] : null;
    $unit=trim((string)($item['unit'] ?? '')) ?: null;
    $notes=trim((string)($item['notes'] ?? '')) ?: null;
    $db->exec("INSERT INTO material_order_items (id,tenant_id,order_id,position_no,description,quantity,unit,notes,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,NOW(),NOW())",[uuid(),$tenantId,$orderId,$pos++,$desc,$qty,$unit,$notes]);
  }
}

// ---------------------------------------------------------------------------
// Auffangnetz fuer schwere Fehler.
//
// Der try/catch am Dateiende faengt Ausnahmen. Ein *fataler* Fehler (z. B.
// erschoepfter Speicher oder ein Abbruch waehrend der Ausgabe) laeuft daran
// vorbei – der Browser bekommt dann eine leere oder HTML-Antwort und die
// Oberflaeche kann nur noch "Serverfehler" melden, ohne Ursache.
//
// Diese Funktion sorgt dafuer, dass IMMER eine lesbare JSON-Antwort mit der
// tatsaechlichen Ursache zurueckkommt.
register_shutdown_function(function(){
  $err = error_get_last();
  if(!$err || !in_array($err['type'], [E_ERROR, E_PARSE, E_CORE_ERROR, E_COMPILE_ERROR, E_USER_ERROR], true)) return;
  if(headers_sent()) return;
  http_response_code(500);
  header('Content-Type: application/json; charset=utf-8');
  echo json_encode([
    'error' => 'Schwerer Serverfehler: '.$err['message'],
    'file'  => basename((string)$err['file']).':'.$err['line']
  ], JSON_UNESCAPED_UNICODE);
});

try {
  $configFile = __DIR__.'/config/local.php';
  if (!file_exists($configFile)) Response::error('Nicht installiert. Bitte /install/install.php ausführen.',503);
  $config = require $configFile;
  // Ablagepfad global merken: purge_where() braucht ihn, um beim Loeschen
  // auch die zugehoerigen Dateien zu entfernen, wird aber an vielen Stellen
  // ohne $config aufgerufen.
  $GLOBALS['EW_STORAGE_ROOT'] = (string)($config['app']['storage_path'] ?? '');
  $modules = require __DIR__.'/config/modules.php';
  if (!is_array($modules)) { Response::error('Modulkonfiguration ungültig. Bitte api/config/modules.php aus dem Hotfix erneut hochladen.',500); }
  $moduleMap = module_map($modules);
  $db = new Database($config); $audit = new Audit($db); $req = new Request();
  $public = ['/auth/login','/auth/refresh','/auth/microsoft/status','/auth/microsoft/start','/auth/microsoft/callback','/auth/microsoft/complete','/auth/quick/status','/auth/quick/login','/health','/modules','/public/app-info'];
  $isPublic = in_array($req->path,$public,true) || strpos($req->path,'/public/portal/')===0 || strpos($req->path,'/public/cron/')===0;
  $user = $isPublic ? null : require_user($db,$config,$req);
  $ctx=['db'=>$db,'config'=>$config,'audit'=>$audit,'user'=>$user,'modules'=>$modules,'moduleMap'=>$moduleMap];
  $r = new Router();

  $r->add('GET','/health', fn($req,$ctx)=>Response::json(['ok'=>true,'app'=>'EstrichWeb Enterprise STRATO Complete','version'=>APP_VERSION,'time'=>date(DATE_ATOM)]));
  $r->add('GET','/modules', fn($req,$ctx)=>Response::json(['items'=>$ctx['modules']]));

  // ---- App-/Firmenstammdaten, Meldungen, Was-ist-neu (Settings) ----
  // Öffentliche App-Infos für Login/Dashboard (ohne Anmeldung lesbar)
  $r->add('GET','/public/app-info', function($req,$ctx){
    ensure_app_settings_table($ctx['db']);
    $tenantId=default_tenant_id($ctx['db'],$req);
    $get=function(string $key,$default=null) use($ctx,$tenantId){ $row=$ctx['db']->one("SELECT value_json FROM app_settings WHERE tenant_id=? AND setting_key=?",[$tenantId,$key]); return $row? json_decode($row['value_json'],true) : $default; };
    // Fallback: Wenn der Standard-Mandant keine Login-Meldung/Firma hat, irgendeinen
    // gesetzten Wert nehmen (vor dem Login ist der konkrete Mandant oft nicht bekannt).
    $getAny=function(string $key,$default=null) use($ctx){ $row=$ctx['db']->one("SELECT value_json FROM app_settings WHERE setting_key=? AND value_json IS NOT NULL AND value_json<>'' AND value_json<>'null' ORDER BY updated_at DESC LIMIT 1",[$key]); return $row? json_decode($row['value_json'],true) : $default; };
    $login=$get('login_message',null); if(!is_array($login) || empty($login['enabled'])) { $any=$getAny('login_message',null); if(is_array($any)) $login=$any; }
    $company=$get('company',null); if(!is_array($company) || empty($company['name'])) { $anyC=$getAny('company',null); if(is_array($anyC)) $company=$anyC; }
    // „Was gibt es Neues" wird bewusst nicht mehr aus der Datenbank gepflegt.
    // Updates sind Bestandteil des ausgelieferten Codes und werden statisch aus
    // api/content/whats-new.txt gelesen. Dadurch können normale Nutzer nichts
    // versehentlich bearbeiten und Deploy-Updates bleiben reproduzierbar.
    $whatsNewText=read_whats_new_text();
    // Die Anmeldemaske zeigt die Version in der Fusszeile – dafuer wird sie
    // hier mit ausgegeben. Die neueste Kennung aus whats-new.txt gewinnt,
    // sonst greift die Grundversion.
    $entries=parse_whats_new_entries($whatsNewText);
    $version=APP_VERSION;
    foreach($entries as $e){ if(!empty($e['version'])){ $version=(string)$e['version']; break; } }
    Response::json([
      'company'=>$company ?: new stdClass(),
      'login_message'=>$login ?: new stdClass(),
      // Damit die Anmeldemaske weiss, ob sie die PIN-Anmeldung anbieten darf.
      'quick_login_enabled'=>quick_login_enabled($ctx['db'],$tenantId),
      'version'=>$version,
      'changelog_text'=>$whatsNewText,
      'changelog'=>$entries
    ]);
  });
  // Angemeldet: alle Einstellungen lesen
  $r->add('GET','/settings/app', function($req,$ctx){
    // Lesen der App-Einstellungen (Firmenname, Banner, Changelog) ist für alle
    // angemeldeten Mitarbeiter nötig (z. B. Firmenname in PDFs). Schreiben bleibt geschützt.
    ensure_app_settings_table($ctx['db']); $tid=$ctx['user']['tenant_id'];
    $get=function(string $key,$default=null) use($ctx,$tid){ $row=$ctx['db']->one("SELECT value_json FROM app_settings WHERE tenant_id=? AND setting_key=?",[$tid,$key]); return $row? json_decode($row['value_json'],true) : $default; };
    Response::json([
      'company'=>$get('company',new stdClass()),
      'dashboard_message'=>$get('dashboard_message',new stdClass()),
      'login_message'=>$get('login_message',new stdClass()),
      // Ebenfalls ergaenzt: Ohne diese Zeile kam der gespeicherte Wert nie
      // in der Oberflaeche an, der Schalter stand nach dem Neuladen wieder aus.
      'quick_login'=>$get('quick_login',new stdClass()),
      'changelog_text'=>read_whats_new_text(),
      'changelog'=>parse_whats_new_entries(read_whats_new_text())
    ]);
  });
  // Angemeldet: eine Einstellung speichern
  $r->add('PUT','/settings/app/{key}', function($req,$ctx,$p){
    require_permission($ctx['user'],'settings.write');
    ensure_app_settings_table($ctx['db']); $tid=$ctx['user']['tenant_id'];
    // FEHLTE: 'quick_login'. Ohne den Eintrag hat der Server das Speichern mit
    // "Unbekannter Einstellungsschluessel" abgewiesen – der Schalter im
    // Backend liess sich umlegen, blieb aber wirkungslos.
    $allowed=['company','dashboard_message','login_message','email','quick_login'];
    if(!in_array($p['key'],$allowed,true)) Response::error('Unbekannter Einstellungsschlüssel',422);
    $value=$req->body['value'] ?? $req->body;
    // E-Mail: leeres Passwort = bestehendes behalten (UI sendet Passwort nicht erneut)
    if($p['key']==='email' && is_array($value) && empty($value['pass'])){
      $prev=$ctx['db']->one("SELECT value_json FROM app_settings WHERE tenant_id=? AND setting_key='email'",[$tid]);
      if($prev){ $old=json_decode($prev['value_json'],true); if(is_array($old) && !empty($old['pass'])) $value['pass']=$old['pass']; }
    }
    $json=json_encode($value,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES);
    $existing=$ctx['db']->one("SELECT id FROM app_settings WHERE tenant_id=? AND setting_key=?",[$tid,$p['key']]);
    if($existing) $ctx['db']->exec("UPDATE app_settings SET value_json=?, updated_at=NOW() WHERE id=?",[$json,$existing['id']]);
    else $ctx['db']->exec("INSERT INTO app_settings (id,tenant_id,setting_key,value_json,created_at,updated_at) VALUES (?,?,?,?,NOW(),NOW())",[uuid(),$tid,$p['key'],$json]);
    $ctx['audit']->log($tid,$ctx['user']['id'],'app_settings',$existing['id']??null,'update',null,['key'=>$p['key']]);
    Response::json(['ok'=>true,'key'=>$p['key'],'value'=>$value]);
  });

  // Admin: E-Mail-Konfiguration lesen (ohne Passwort)
  // Cron: Erinnerungen fuer faellige/ueberfaellige Aufgaben (per Schluessel gesichert)
  // Aufruf z. B. taeglich:  https://DOMAIN/api/public/cron/due-tasks?key=<JWT_SECRET>
  $r->add('GET','/public/cron/due-tasks', function($req,$ctx){
    $key=(string)($req->query['key'] ?? '');
    $expected=(string)($ctx['config']['app']['cron_key'] ?? $ctx['config']['app']['jwt_secret'] ?? '');
    if($expected==='' || !hash_equals($expected,$key)) Response::error('Forbidden',403);
    $db=$ctx['db'];
    // optionales Spaltenfeld zum Entprellen
    add_column_if_missing($db,'tasks','reminder_sent_at','DATETIME NULL');
    $rows=safe_query($db,"SELECT t.id,t.tenant_id,t.title,t.due_date,t.assigned_user_id,u.name uname,u.email uemail
      FROM tasks t JOIN users u ON u.id=t.assigned_user_id AND u.deleted_at IS NULL
      WHERE t.deleted_at IS NULL AND t.assigned_user_id IS NOT NULL AND t.assigned_user_id<>''
        AND t.status NOT IN ('done','completed','cancelled','closed')
        AND t.due_date IS NOT NULL AND t.due_date <= CURDATE()
        AND (t.reminder_sent_at IS NULL OR t.reminder_sent_at < (NOW() - INTERVAL 20 HOUR))
      ORDER BY t.due_date ASC LIMIT 500");
    $sent=0;
    foreach($rows as $t){
      if(empty($t['uemail'])) continue;
      $titel=htmlspecialchars((string)($t['title'] ?? 'Aufgabe'),ENT_QUOTES,'UTF-8');
      $faellig=htmlspecialchars((string)($t['due_date'] ?? ''),ENT_QUOTES,'UTF-8');
      $overdue=$t['due_date'] < date('Y-m-d');
      $html=email_layout($overdue?'Überfällige Aufgabe':'Aufgabe fällig',
        '<p>Hallo '.htmlspecialchars((string)$t['uname'],ENT_QUOTES,'UTF-8').',</p>'
        .'<p>folgende Aufgabe ist '.($overdue?'<b>überfällig</b>':'heute fällig').':</p>'
        .'<p style="font-size:16px;font-weight:bold">'.$titel.'</p>'
        .'<p>Fällig am: '.$faellig.'</p>'
        .'<p>Bitte in EstrichWeb bearbeiten.</p>',
        'Automatische Erinnerung von EstrichWeb');
      if(notify_by_email($db,(string)$t['tenant_id'],(string)$t['uemail'],(string)$t['uname'],'EstrichWeb – '.($overdue?'Überfällige':'Fällige').' Aufgabe: '.$t['title'],$html)){
        try { $db->exec("UPDATE tasks SET reminder_sent_at=NOW() WHERE id=?",[$t['id']]); } catch(Throwable $e) {}
        $sent++;
      }
    }
    Response::json(['ok'=>true,'reminders_sent'=>$sent,'candidates'=>count($rows)]);
  });

  $r->add('GET','/admin/email-settings', function($req,$ctx){
    require_permission($ctx['user'],'settings.write');
    $cfg=app_email_config($ctx['db'],$ctx['user']['tenant_id']) ?: [];
    $hasPass=!empty($cfg['pass']); unset($cfg['pass']);
    $cfg['has_password']=$hasPass;
    if(!isset($cfg['enabled'])) $cfg['enabled']=false;
    Response::json(['settings'=>$cfg]);
  });

  // Admin: Test-E-Mail senden
  $r->add('POST','/admin/email-settings/test', function($req,$ctx){
    require_permission($ctx['user'],'settings.write');
    $tid=$ctx['user']['tenant_id'];
    $cfg=app_email_config($ctx['db'],$tid);
    if(!$cfg) Response::error('Keine E-Mail-Konfiguration gespeichert',422);
    $to=trim((string)($req->body['to'] ?? $ctx['user']['email'] ?? ''));
    if($to==='') Response::error('Keine Zieladresse',422);
    $html=email_layout('Test-E-Mail','<p>Dies ist eine Test-Nachricht aus EstrichWeb. Wenn Sie diese E-Mail erhalten, ist der E-Mail-Versand korrekt eingerichtet.</p>');
    $res=send_smtp_mail($cfg,$to,(string)($ctx['user']['name'] ?? ''),'EstrichWeb – Test-E-Mail',$html);
    if(empty($res['ok'])) Response::error('Versand fehlgeschlagen: '.($res['error'] ?? 'unbekannt'),502);
    Response::json(['ok'=>true,'to'=>$to]);
  });

  $r->add('POST','/auth/login', function($req,$ctx) {
    assert_required($req->body,['email','password']);
    $db=$ctx['db']; $email=mb_strtolower(trim((string)$req->body['email']));
    $ip=(string)($_SERVER['REMOTE_ADDR'] ?? '');
    add_column_if_missing($db,'users','must_change_password','TINYINT(1) NOT NULL DEFAULT 0');

    // ---- Sperre nach wiederholten Fehlversuchen ---------------------------
    // Ohne Bremse kann ein Angreifer beliebig viele Passwoerter durchprobieren.
    // Gezaehlt wird getrennt nach Kennung UND nach Herkunftsadresse:
    //   * 5 Fehlversuche je Kennung in 15 Minuten  -> 15 Minuten gesperrt
    //   * 20 Fehlversuche je Adresse in 15 Minuten -> 15 Minuten gesperrt
    // Die zweite Regel greift, wenn jemand viele verschiedene Kennungen
    // durchprobiert. Ein erfolgreicher Login setzt den Zaehler zurueck.
    $lock=login_lock_state($db,$email,$ip);
    if($lock['locked']){
      $db->exec("INSERT INTO login_logs (id,tenant_id,user_id,email,ip,user_agent,success,failure_reason,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,NOW(),NOW())",
        [uuid(),null,null,$email,$ip,$_SERVER['HTTP_USER_AGENT']??null,0,'locked_out']);
      Response::error('Zu viele Fehlversuche. Bitte in '.$lock['minutes'].' Minute(n) erneut versuchen.',429,['retry_after_minutes'=>$lock['minutes']]);
    }

    $u=$db->one("SELECT u.*, t.slug tenant_slug, t.name tenant_name FROM users u JOIN tenants t ON t.id=u.tenant_id WHERE LOWER(u.email)=? AND u.deleted_at IS NULL",[$email]);

    // Gleich langer Rechenweg, egal ob die Kennung existiert. Sonst laesst
    // sich an der Antwortzeit ablesen, welche Kennungen es gibt.
    $hash=(string)($u['password_hash'] ?? '$2y$12$0000000000000000000000000000000000000000000000000000');
    $passwordOk=password_verify((string)$req->body['password'], $hash);

    $inactive = $u && (int)$u['is_active']!==1;
    $success = $u && $passwordOk && !$inactive;
    $reason = $success ? null : ($inactive ? 'inactive_user' : 'invalid_credentials');

    $db->exec("INSERT INTO login_logs (id,tenant_id,user_id,email,ip,user_agent,success,failure_reason,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,NOW(),NOW())",[uuid(),$u['tenant_id']??null,$u['id']??null,$email,$ip,$_SERVER['HTTP_USER_AGENT']??null,$success?1:0,$reason]);

    if (!$success) {
      // Bewusst dieselbe Meldung fuer "Kennung unbekannt" und "Passwort
      // falsch" – sonst laesst sich herausfinden, welche Kennungen es gibt.
      $left=login_attempts_left($db,$email,$ip);
      $hint=$left>0 && $left<=2 ? ' Noch '.$left.' Versuch(e) bis zur Sperre.' : '';
      Response::error('E-Mail oder Passwort ist nicht korrekt.'.$hint,401);
    }
    if ((int)($u['two_factor_enabled'] ?? 0) === 1 && empty($req->body['two_factor_code'])) Response::error('2FA-Code erforderlich',412,['two_factor_required'=>true]);
    Response::json(issue_login_response($db,$ctx['config'],$u,$req));
  });

  // ---- Schnellanmeldung mit PIN -----------------------------------------
  // Prueft, ob DIESES Geraet hinterlegt ist. Gibt nur zurueck, was die
  // Anmeldemaske braucht – nie die Kennung oder gar die PIN.
  $r->add('GET','/auth/quick/status', function($req,$ctx){
    $db=$ctx['db']; ensure_quick_login_table($db);
    $tenantId=default_tenant_id($db,$req);
    if(!quick_login_enabled($db,$tenantId)) Response::json(['available'=>false,'enabled'=>false]);
    $deviceId=trim((string)($_GET['device_id'] ?? ''));
    if($deviceId==='') Response::json(['available'=>false,'enabled'=>true]);
    $row=$db->one("SELECT q.device_label,q.locked_at,u.name user_name
                   FROM quick_logins q JOIN users u ON u.id=q.user_id
                   WHERE q.device_id=? AND q.revoked_at IS NULL AND u.deleted_at IS NULL
                     AND q.last_used_at > NOW() - INTERVAL 90 DAY",[$deviceId]);
    if(!$row) Response::json(['available'=>false,'enabled'=>true]);
    if(!empty($row['locked_at'])) Response::json(['available'=>false,'enabled'=>true,'locked'=>true]);
    Response::json([
      'available'   => true,
      'enabled'     => true,
      'user_name'   => $row['user_name'],
      'device_label'=> $row['device_label']
    ]);
  });

  // Anmeldung mit Geraetekennung + PIN.
  $r->add('POST','/auth/quick/login', function($req,$ctx){
    $db=$ctx['db']; ensure_quick_login_table($db);
    $deviceId=trim((string)($req->body['device_id'] ?? ''));
    $pin=trim((string)($req->body['pin'] ?? ''));
    $ip=(string)($_SERVER['REMOTE_ADDR'] ?? '');
    if($deviceId===''||$pin==='') Response::error('Angaben unvollständig',422);

    $q=$db->one("SELECT * FROM quick_logins WHERE device_id=? AND revoked_at IS NULL",[$deviceId]);
    if(!$q) Response::error('Für dieses Gerät ist keine Schnellanmeldung eingerichtet.',401);
    if(!empty($q['locked_at'])) Response::error('Schnellanmeldung gesperrt. Bitte einmal vollständig anmelden.',423);

    if(!password_verify($pin,(string)$q['pin_hash'])){
      $fails=(int)$q['failed_attempts']+1;
      // Nach 5 Fehlversuchen ist Schluss – dann hilft nur die volle Anmeldung.
      $lock = $fails>=5 ? 'NOW()' : 'NULL';
      $db->exec("UPDATE quick_logins SET failed_attempts=?, locked_at=$lock, updated_at=NOW() WHERE id=?",[$fails,$q['id']]);
      $db->exec("INSERT INTO login_logs (id,tenant_id,user_id,email,ip,user_agent,success,failure_reason,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,NOW(),NOW())",
        [uuid(),$q['tenant_id'],$q['user_id'],'(PIN)',$ip,$_SERVER['HTTP_USER_AGENT']??null,0,'invalid_pin']);
      $left=max(0,5-$fails);
      Response::error($left>0 ? "PIN ist nicht korrekt. Noch $left Versuch(e)." : 'PIN mehrfach falsch. Bitte einmal vollständig anmelden.',401);
    }

    $u=$db->one("SELECT u.*, t.slug tenant_slug, t.name tenant_name FROM users u JOIN tenants t ON t.id=u.tenant_id
                 WHERE u.id=? AND u.deleted_at IS NULL AND u.is_active=1",[$q['user_id']]);
    if(!$u) Response::error('Benutzerkonto nicht verfügbar',401);
    if(!quick_login_enabled($db,$u['tenant_id'])) Response::error('Die Schnellanmeldung ist nicht freigeschaltet.',403);

    $db->exec("UPDATE quick_logins SET failed_attempts=0, locked_at=NULL, last_used_at=NOW(), updated_at=NOW() WHERE id=?",[$q['id']]);
    $db->exec("INSERT INTO login_logs (id,tenant_id,user_id,email,ip,user_agent,success,failure_reason,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,NOW(),NOW())",
      [uuid(),$u['tenant_id'],$u['id'],$u['email'],$ip,$_SERVER['HTTP_USER_AGENT']??null,1,null]);
    Response::json(issue_login_response($db,$ctx['config'],$u,$req));
  });

  $r->add('GET','/auth/microsoft/status', function($req,$ctx){
    $tenantId=default_tenant_id($ctx['db'],$req);
    if(!$tenantId) Response::json(['enabled'=>false,'provider'=>'microsoft']);
    Response::json(sso_public_config($ctx['db'],$ctx['config'],$tenantId));
  });

  $r->add('GET','/auth/microsoft/start', function($req,$ctx){
    $tenantId=default_tenant_id($ctx['db'],$req); if(!$tenantId) Response::error('Mandant für Microsoft SSO nicht gefunden',422);
    $ms=microsoft_sso_runtime($ctx['db'],$ctx['config'],$tenantId);
    if(!$ms['enabled']) Response::error('Microsoft Single Sign-On ist nicht eingerichtet',422);
    $state=bin2hex(random_bytes(20)).'.'.$tenantId;
    setcookie('ew_ms_state',hash('sha256',$state),time()+600,'/','',!empty($_SERVER['HTTPS']),true);
    $params=http_build_query([
      'client_id'=>$ms['client_id'],
      'response_type'=>'code',
      'redirect_uri'=>$ms['redirect_uri'],
      'response_mode'=>'query',
      'scope'=>'openid profile email User.Read',
      'state'=>$state
    ]);
    header('Location: https://login.microsoftonline.com/'.rawurlencode($ms['tenant_id']).'/oauth2/v2.0/authorize?'.$params, true, 302);
    exit;
  });

  $r->add('GET','/auth/microsoft/callback', function($req,$ctx){
    $state=(string)($req->query['state'] ?? '');
    if($state === '' || !hash_equals((string)($_COOKIE['ew_ms_state'] ?? ''), hash('sha256',$state))) Response::error('Microsoft SSO State ungültig',401);
    $parts=explode('.',$state); $tenantId=$parts[1] ?? default_tenant_id($ctx['db'],$req); if(!$tenantId) Response::error('Mandant für Microsoft SSO nicht gefunden',422);
    $ms=microsoft_sso_runtime($ctx['db'],$ctx['config'],$tenantId);
    if(!$ms['enabled']) Response::error('Microsoft Single Sign-On ist nicht eingerichtet',422);
    $code=(string)($req->query['code'] ?? ''); if($code==='') Response::error('Microsoft SSO Code fehlt',422);
    if(!function_exists('curl_init')) Response::error('PHP-cURL ist auf dem Webspace nicht aktiv',500);
    $tokenUrl='https://login.microsoftonline.com/'.rawurlencode($ms['tenant_id']).'/oauth2/v2.0/token';
    $post=http_build_query(['client_id'=>$ms['client_id'],'client_secret'=>$ms['client_secret'],'code'=>$code,'redirect_uri'=>$ms['redirect_uri'],'grant_type'=>'authorization_code','scope'=>'openid profile email User.Read']);
    $ch=curl_init($tokenUrl); curl_setopt_array($ch,[CURLOPT_RETURNTRANSFER=>true,CURLOPT_POST=>true,CURLOPT_POSTFIELDS=>$post,CURLOPT_HTTPHEADER=>['Content-Type: application/x-www-form-urlencoded'],CURLOPT_TIMEOUT=>30]);
    $raw=curl_exec($ch); $status=(int)curl_getinfo($ch,CURLINFO_RESPONSE_CODE); $err=curl_error($ch); curl_close($ch);
    if($raw===false || $status>=400) Response::error('Microsoft SSO Token-Austausch fehlgeschlagen'.($err?': '.$err:''),502);
    $tokens=json_decode($raw,true); $claims=jwt_decode_unverified($tokens['id_token'] ?? null);
    $email=mb_strtolower(trim((string)($claims['preferred_username'] ?? $claims['email'] ?? $claims['upn'] ?? '')));
    if($email==='') Response::error('Microsoft SSO liefert keine E-Mail-Adresse',422);
    $db=$ctx['db']; ensure_sso_tables($db);
    $u=$db->one("SELECT u.*, t.slug tenant_slug, t.name tenant_name FROM users u JOIN tenants t ON t.id=u.tenant_id WHERE LOWER(u.email)=? AND u.tenant_id=? AND u.deleted_at IS NULL AND u.is_active=1",[$email,$tenantId]);
    if(!$u) Response::error('Für diese Microsoft-Adresse ist kein aktiver Benutzer angelegt',403);
    $ticket=bin2hex(random_bytes(32));
    $db->exec("INSERT INTO sso_login_tickets (id,tenant_id,user_id,ticket_hash,provider,expires_at,created_at,updated_at) VALUES (?,?,?,?,?,DATE_ADD(NOW(), INTERVAL 5 MINUTE),NOW(),NOW())",[uuid(),$u['tenant_id'],$u['id'],hash('sha256',$ticket),'microsoft']);
    header('Location: '.app_base_url($ctx['config']).'/login?sso_ticket='.rawurlencode($ticket), true, 302); exit;
  });

  $r->add('POST','/auth/microsoft/complete', function($req,$ctx){
    assert_required($req->body,['ticket']); ensure_sso_tables($ctx['db']);
    $hash=hash('sha256',(string)$req->body['ticket']);
    $row=$ctx['db']->one("SELECT * FROM sso_login_tickets WHERE ticket_hash=? AND provider='microsoft' AND used_at IS NULL AND expires_at > NOW()",[$hash]);
    if(!$row) Response::error('Microsoft SSO Ticket ungültig oder abgelaufen',401);
    $u=$ctx['db']->one("SELECT u.*, t.slug tenant_slug, t.name tenant_name FROM users u JOIN tenants t ON t.id=u.tenant_id WHERE u.id=? AND u.tenant_id=? AND u.deleted_at IS NULL AND u.is_active=1",[$row['user_id'],$row['tenant_id']]);
    if(!$u) Response::error('Benutzer nicht gefunden oder gesperrt',401);
    $ctx['db']->exec("UPDATE sso_login_tickets SET used_at=NOW(), updated_at=NOW() WHERE id=?",[$row['id']]);
    Response::json(issue_login_response($ctx['db'],$ctx['config'],$u,$req));
  });

  $r->add('POST','/auth/refresh', function($req,$ctx) {
    assert_required($req->body,['refresh_token']);
    $hash=hash('sha256',(string)$req->body['refresh_token']);
    $row=$ctx['db']->one("SELECT rt.*, u.email FROM refresh_tokens rt JOIN users u ON u.id=rt.user_id WHERE token_hash=? AND revoked_at IS NULL AND expires_at>NOW()",[$hash]);
    if(!$row) Response::error('Refresh Token ungültig',401);
    $token=Jwt::sign(['sub'=>$row['user_id'],'tenant_id'=>$row['tenant_id'],'email'=>$row['email']],$ctx['config']['app']['jwt_secret'],(int)$ctx['config']['app']['jwt_ttl_minutes']);
    Response::json(['token'=>$token]);
  });

  $r->add('GET','/me', fn($req,$ctx)=>Response::json(['user'=>$ctx['user']]));
  $r->add('GET','/auth/check', function($req,$ctx){
    Response::json([
      'authenticated'=>true,
      'user'=>['id'=>$ctx['user']['id'],'name'=>$ctx['user']['name'],'email'=>$ctx['user']['email']],
      'tenant'=>['id'=>$ctx['user']['tenant_id'],'name'=>$ctx['user']['tenant_name'] ?? null]
    ]);
  });



  $r->add('GET','/admin/user-roles', function($req,$ctx){
    require_permission($ctx['user'],'users.read');
    $rows=$ctx['db']->all("SELECT ur.user_id, ur.role_id FROM user_roles ur JOIN users u ON u.id=ur.user_id WHERE u.tenant_id=? AND u.deleted_at IS NULL",[$ctx['user']['tenant_id']]);
    $map=[]; foreach($rows as $row){ $map[$row['user_id']][]=$row['role_id']; }
    Response::json(['items'=>$rows,'map'=>$map]);
  });

  // Roles with their assigned permission codes.
  $r->add('GET','/admin/roles', function($req,$ctx){
    require_permission($ctx['user'],'users.read');
    $tid=$ctx['user']['tenant_id']; $db=$ctx['db'];
    $roles=$db->all("SELECT id,name,code,description,is_system FROM roles WHERE deleted_at IS NULL AND (tenant_id=? OR tenant_id IS NULL) ORDER BY is_system DESC, name",[$tid]);
    $rp=safe_query($db,"SELECT rp.role_id, p.code FROM role_permissions rp JOIN permissions p ON p.id=rp.permission_id");
    $counts=safe_query($db,"SELECT role_id, COUNT(*) c FROM user_roles GROUP BY role_id");
    $userMap=[]; foreach($counts as $c){ $userMap[$c['role_id']]=(int)$c['c']; }
    $map=[]; foreach($rp as $row){ $map[$row['role_id']][]=$row['code']; }
    foreach($roles as &$role){ $role['permissions']=$map[$role['id']] ?? []; $role['user_count']=$userMap[$role['id']] ?? 0; }
    Response::json(['items'=>$roles]);
  });

  // All available permission codes (code + label).
  $r->add('GET','/admin/permissions', function($req,$ctx){
    require_permission($ctx['user'],'users.read');
    $rows=safe_query($ctx['db'],"SELECT code,label FROM permissions ORDER BY code");
    Response::json(['items'=>$rows]);
  });

  // Create a tenant role.
  $r->add('POST','/admin/roles', function($req,$ctx){
    require_permission($ctx['user'],'users.write');
    $tid=$ctx['user']['tenant_id']; $db=$ctx['db'];
    $name=trim((string)($req->body['name'] ?? '')); if($name==='') Response::error('Name fehlt',422);
    $code=trim((string)($req->body['code'] ?? '')); if($code==='') $code=strtolower(preg_replace('/[^a-z0-9]+/i','_',$name));
    $id=uuid();
    $db->exec("INSERT INTO roles (id,tenant_id,name,code,description,is_system,created_at,updated_at) VALUES (?,?,?,?,?,0,NOW(),NOW())",[$id,$tid,$name,$code,$req->body['description']??null]);
    foreach(($req->body['permissions'] ?? []) as $pc){ $perm=$db->one("SELECT id FROM permissions WHERE code=?",[(string)$pc]); if($perm) $db->exec("INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES (?,?)",[$id,$perm['id']]); }
    $ctx['audit']->log($tid,$ctx['user']['id'],'roles',$id,'create',null,['name'=>$name,'code'=>$code]);
    Response::json(['ok'=>true,'id'=>$id],201);
  });

  // Replace a role's permission set.
  $r->add('PUT','/admin/roles/{id}/permissions', function($req,$ctx,$p){
    require_permission($ctx['user'],'users.write');
    $tid=$ctx['user']['tenant_id']; $db=$ctx['db'];
    $role=$db->one("SELECT * FROM roles WHERE id=? AND deleted_at IS NULL AND (tenant_id=? OR tenant_id IS NULL)",[$p['id'],$tid]);
    if(!$role) Response::error('Rolle nicht gefunden',404);
    $codes=$req->body['permissions'] ?? []; if(!is_array($codes)) Response::error('permissions muss eine Liste sein',422);
    $before=$db->all("SELECT p.code FROM role_permissions rp JOIN permissions p ON p.id=rp.permission_id WHERE rp.role_id=?",[$p['id']]);
    $db->exec("DELETE FROM role_permissions WHERE role_id=?",[$p['id']]);
    foreach($codes as $code){ $perm=$db->one("SELECT id FROM permissions WHERE code=?",[(string)$code]); if($perm) $db->exec("INSERT IGNORE INTO role_permissions (role_id,permission_id) VALUES (?,?)",[$p['id'],$perm['id']]); }
    $ctx['audit']->log($tid,$ctx['user']['id'],'role_permissions',$p['id'],'update',$before,$codes);
    Response::json(['ok'=>true,'role_id'=>$p['id'],'permissions'=>array_values($codes)]);
  });

  // Eigene Kontodaten für die Profilseite: Rolle, letzte Anmeldung,
  // Zwei-Faktor-Status und Alter des Passworts. Bewusst nur Angaben zum
  // EIGENEN Konto – ohne Sonderrechte.
  $r->add('GET','/me', function($req,$ctx){
    $tid=$ctx['user']['tenant_id']; $db=$ctx['db'];
    add_column_if_missing($db,'users','password_changed_at','DATETIME NULL');
    $u=$db->one("SELECT u.id,u.name,u.email,u.is_active,u.two_factor_enabled,u.password_changed_at,u.created_at,u.role_id
                 FROM users u WHERE u.tenant_id=? AND u.id=? AND u.deleted_at IS NULL",[$tid,$ctx['user']['id']]);
    if(!$u) Response::error('Benutzer nicht gefunden',404);
    $role=null;
    if(!empty($u['role_id'])){
      $r2=$db->one("SELECT name FROM roles WHERE id=?",[$u['role_id']]);
      $role=$r2['name'] ?? null;
    }
    $last=$db->one("SELECT created_at FROM login_logs WHERE user_id=? AND success=1 ORDER BY created_at DESC LIMIT 1 OFFSET 1",[$ctx['user']['id']]);
    Response::json(array_merge($u,[
      'role_name'     => $role,
      'last_login_at' => $last['created_at'] ?? null,
      'app_version'   => defined('APP_VERSION') ? APP_VERSION : null
    ]));
  });

  // Schnellanmeldung fuer das aktuelle Geraet einrichten oder aufheben.
  $r->add('POST','/me/quick-login', function($req,$ctx){
    $db=$ctx['db']; ensure_quick_login_table($db);
    $tid=$ctx['user']['tenant_id'];
    if(!quick_login_enabled($db,$tid)) Response::error('Die Schnellanmeldung ist für Ihren Betrieb nicht freigeschaltet.',403);
    $deviceId=trim((string)($req->body['device_id'] ?? ''));
    $pin=trim((string)($req->body['pin'] ?? ''));
    $label=trim((string)($req->body['device_label'] ?? ''));
    if($deviceId==='') Response::error('Gerätekennung fehlt',422);
    if(!preg_match('/^\d{6}$/',$pin)) Response::error('Die PIN muss aus genau 6 Ziffern bestehen.',422);
    // Zu einfache PINs abweisen – 123456 oder 000000 sind keine Sicherung.
    if(preg_match('/^(\d)\1{5}$/',$pin) || in_array($pin,['123456','654321','111111','000000','123123'],true)){
      Response::error('Diese PIN ist zu einfach. Bitte eine andere wählen.',422);
    }
    // Ein Geraet gehoert immer nur EINEM Konto.
    $db->exec("DELETE FROM quick_logins WHERE device_id=?",[$deviceId]);
    $db->exec("INSERT INTO quick_logins (id,tenant_id,user_id,device_id,device_label,pin_hash,failed_attempts,last_used_at,created_at,updated_at)
               VALUES (?,?,?,?,?,?,0,NOW(),NOW(),NOW())",
      [uuid(),$tid,$ctx['user']['id'],$deviceId,$label!==''?$label:null,password_hash($pin,PASSWORD_DEFAULT)]);
    $ctx['audit']->log($tid,$ctx['user']['id'],'quick_logins',$deviceId,'create',null,['label'=>$label]);
    Response::json(['ok'=>true]);
  });

  $r->add('DELETE','/me/quick-login', function($req,$ctx){
    $db=$ctx['db']; ensure_quick_login_table($db);
    $tid=$ctx['user']['tenant_id'];
    $deviceId=trim((string)($_GET['device_id'] ?? ''));
    if($deviceId!==''){
      $db->exec("DELETE FROM quick_logins WHERE device_id=? AND tenant_id=? AND user_id=?",[$deviceId,$tid,$ctx['user']['id']]);
    } else {
      // Ohne Kennung: ALLE Geraete des Kontos aufheben (z. B. bei Verlust).
      $db->exec("DELETE FROM quick_logins WHERE tenant_id=? AND user_id=?",[$tid,$ctx['user']['id']]);
    }
    Response::json(['ok'=>true]);
  });

  // Liste der eigenen Geraete – fuer die Profilseite.
  $r->add('GET','/me/quick-login', function($req,$ctx){
    $db=$ctx['db']; ensure_quick_login_table($db);
    $tid=$ctx['user']['tenant_id'];
    $rows=$db->all("SELECT device_id,device_label,last_used_at,locked_at,created_at FROM quick_logins
                    WHERE tenant_id=? AND user_id=? AND revoked_at IS NULL ORDER BY last_used_at DESC",[$tid,$ctx['user']['id']]);
    Response::json(['items'=>$rows,'enabled'=>quick_login_enabled($db,$tid)]);
  });

  // Eigenes Passwort ändern (löscht ggf. den Zwang zur Erstanmeldung-Änderung)
  $r->add('POST','/me/change-password', function($req,$ctx){
    $tid=$ctx['user']['tenant_id']; $db=$ctx['db'];
    $current=(string)($req->body['current_password'] ?? '');
    $next=(string)($req->body['new_password'] ?? '');
    if(strlen($next)<8) Response::error('Neues Passwort muss mindestens 8 Zeichen haben',422);
    $u=$db->one("SELECT password_hash FROM users WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$ctx['user']['id']]);
    if(!$u) Response::error('Benutzer nicht gefunden',404);
    if(!password_verify($current,(string)$u['password_hash'])) Response::error('Aktuelles Passwort ist falsch',403);
    add_column_if_missing($db,'users','must_change_password','TINYINT(1) NOT NULL DEFAULT 0');
    // Zeitpunkt festhalten – die Profilseite zeigt daraus das Alter des
    // Passworts an ("vor 41 Tagen geändert").
    add_column_if_missing($db,'users','password_changed_at','DATETIME NULL');
    $db->exec("UPDATE users SET password_hash=?, must_change_password=0, password_changed_at=NOW(), updated_at=NOW() WHERE tenant_id=? AND id=?",[password_hash($next,PASSWORD_DEFAULT),$tid,$ctx['user']['id']]);
    $ctx['audit']->log($tid,$ctx['user']['id'],'users',$ctx['user']['id'],'password_change',null,null);
    Response::json(['ok'=>true]);
  });

  // ---- Kundenportal: Zugang verwalten (Admin) + öffentlicher Lesezugriff ----
  $r->add('GET','/admin/customers/{id}/portal', function($req,$ctx,$p){
    require_portal_enabled($ctx['config']);
    require_permission($ctx['user'],'customers.read'); ensure_customer_portal_table($ctx['db']);
    $tid=$ctx['user']['tenant_id'];
    $row=$ctx['db']->one("SELECT id,is_active,last_access_at,created_at,revoked_at FROM customer_portal_access WHERE tenant_id=? AND customer_id=? AND revoked_at IS NULL ORDER BY created_at DESC LIMIT 1",[$tid,$p['id']]);
    Response::json(['access'=>$row ?: null,'has_access'=>$row && (int)$row['is_active']===1]);
  });
  $r->add('POST','/admin/customers/{id}/portal', function($req,$ctx,$p){
    require_portal_enabled($ctx['config']);
    require_permission($ctx['user'],'customers.write'); ensure_customer_portal_table($ctx['db']);
    $tid=$ctx['user']['tenant_id']; $db=$ctx['db'];
    $cust=$db->one("SELECT id,name FROM customers WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$p['id']]);
    if(!$cust) Response::error('Kunde nicht gefunden',404);
    // Alte Zugänge widerrufen, neuen Token + 5-stelligen PIN erzeugen (nur jetzt im Klartext zurückgegeben).
    $db->exec("UPDATE customer_portal_access SET revoked_at=NOW(), is_active=0, updated_at=NOW() WHERE tenant_id=? AND customer_id=? AND revoked_at IS NULL",[$tid,$p['id']]);
    $token=bin2hex(random_bytes(18));
    $pin=str_pad((string)random_int(0,99999),5,'0',STR_PAD_LEFT);
    $db->exec("INSERT INTO customer_portal_access (id,tenant_id,customer_id,token_hash,pin_hash,is_active,failed_attempts,created_at,updated_at) VALUES (?,?,?,?,?,1,0,NOW(),NOW())",[uuid(),$tid,$p['id'],hash('sha256',$token),hash('sha256',$pin)]);
    Response::json(['token'=>$token,'pin'=>$pin,'customer_name'=>$cust['name']],201);
  });
  $r->add('DELETE','/admin/customers/{id}/portal', function($req,$ctx,$p){
    require_portal_enabled($ctx['config']);
    require_permission($ctx['user'],'customers.write'); ensure_customer_portal_table($ctx['db']);
    $tid=$ctx['user']['tenant_id'];
    $ctx['db']->exec("UPDATE customer_portal_access SET revoked_at=NOW(), is_active=0, updated_at=NOW() WHERE tenant_id=? AND customer_id=? AND revoked_at IS NULL",[$tid,$p['id']]);
    Response::json(['ok'=>true]);
  });
  // Schritt 1: Token prüfen – verrät nur, ob PIN nötig ist (keine Daten ohne PIN).
  $r->add('GET','/public/portal/{token}', function($req,$ctx,$p){
    require_portal_enabled($ctx['config']);
    ensure_customer_portal_table($ctx['db']); $db=$ctx['db'];
    $acc=$db->one("SELECT id,tenant_id,customer_id FROM customer_portal_access WHERE token_hash=? AND is_active=1 AND revoked_at IS NULL",[hash('sha256',(string)$p['token'])]);
    if(!$acc) Response::error('Zugang ungültig oder widerrufen',403);
    $company=null; try { $row=$db->one("SELECT value_json FROM app_settings WHERE tenant_id=? AND setting_key='company'",[$acc['tenant_id']]); if($row) $company=json_decode($row['value_json'],true); } catch(Throwable $e) {}
    Response::json(['requires_pin'=>true,'company'=>$company ?: new stdClass()]);
  });
  // Schritt 2: Token + PIN -> Daten. Sperrt nach zu vielen Fehlversuchen.
  $r->add('POST','/public/portal/{token}/verify', function($req,$ctx,$p){
    require_portal_enabled($ctx['config']);
    ensure_customer_portal_table($ctx['db']); $db=$ctx['db'];
    $acc=$db->one("SELECT * FROM customer_portal_access WHERE token_hash=? AND is_active=1 AND revoked_at IS NULL",[hash('sha256',(string)$p['token'])]);
    if(!$acc) Response::error('Zugang ungültig oder widerrufen',403);
    if((int)($acc['failed_attempts']??0) >= 8) Response::error('Zu viele Fehlversuche. Bitte fordern Sie einen neuen Zugang an.',429);
    $pin=preg_replace('/\D+/','',(string)($req->body['pin'] ?? ''));
    if($pin==='' || hash('sha256',$pin) !== $acc['pin_hash']){
      $db->exec("UPDATE customer_portal_access SET failed_attempts=failed_attempts+1, updated_at=NOW() WHERE id=?",[$acc['id']]);
      Response::error('PIN ist nicht korrekt',403);
    }
    $db->exec("UPDATE customer_portal_access SET last_access_at=NOW(), failed_attempts=0 WHERE id=?",[$acc['id']]);
    $tid=$acc['tenant_id']; $cid=$acc['customer_id'];
    $customer=$db->one("SELECT id,name,contact_person,email,phone,address,zip,city FROM customers WHERE tenant_id=? AND id=?",[$tid,$cid]);
    $company=null; try { $row=$db->one("SELECT value_json FROM app_settings WHERE tenant_id=? AND setting_key='company'",[$tid]); if($row) $company=json_decode($row['value_json'],true); } catch(Throwable $e) {}
    $projects=safe_query($db,"SELECT id,project_no,name,status,start_date,end_date,address,city FROM projects WHERE tenant_id=? AND customer_id=? AND deleted_at IS NULL ORDER BY created_at DESC LIMIT 100",[$tid,$cid]);
    $docs=[];
    try { $docs=safe_query($db,"SELECT d.id,d.name,d.created_at,d.project_id FROM documents d WHERE d.tenant_id=? AND d.project_id IN (SELECT id FROM projects WHERE tenant_id=? AND customer_id=?) AND d.deleted_at IS NULL AND (d.customer_visible=1) ORDER BY d.created_at DESC LIMIT 200",[$tid,$tid,$cid]); }
    catch(Throwable $e) { $docs=[]; }
    Response::json(['customer'=>$customer,'company'=>$company ?: new stdClass(),'projects'=>$projects,'documents'=>$docs]);
  });

  // Streamt ein fuer den Kunden freigegebenes Dokument (Token + PIN gesichert).
  $r->add('GET','/public/portal/{token}/document/{id}', function($req,$ctx,$p){
    require_portal_enabled($ctx['config']);
    ensure_customer_portal_table($ctx['db']); $db=$ctx['db'];
    $acc=$db->one("SELECT * FROM customer_portal_access WHERE token_hash=? AND is_active=1 AND revoked_at IS NULL",[hash('sha256',(string)$p['token'])]);
    if(!$acc) Response::error('Zugang ungültig oder widerrufen',403);
    $pin=preg_replace('/\D+/','',(string)($req->query['pin'] ?? ''));
    if($pin==='' || hash('sha256',$pin) !== $acc['pin_hash']) Response::error('PIN ist nicht korrekt',403);
    $tid=$acc['tenant_id']; $cid=$acc['customer_id'];
    $doc=$db->one("SELECT * FROM documents WHERE tenant_id=? AND id=? AND deleted_at IS NULL AND customer_visible=1 AND project_id IN (SELECT id FROM projects WHERE tenant_id=? AND customer_id=?)",[$tid,$p['id'],$tid,$cid]);
    if(!$doc) Response::error('Dokument nicht gefunden oder nicht freigegeben',404);
    $name=(string)($doc['original_name'] ?: $doc['name'] ?: 'dokument');
    $mime=reliable_mime((string)($doc['mime_type'] ?? ''),(string)$doc['storage_path']);
    $disposition=truthy($req->query['download'] ?? false) ? 'attachment' : 'inline';
    stream_private_file((string)$doc['storage_path'],$mime,$name,$disposition);
  });
  // alte direkte Datenroute entfällt zugunsten des PIN-Flows

  // ---- Anrufe / Anfragenverwaltung ----
  $r->add('GET','/calls', function($req,$ctx){
    require_permission($ctx['user'],'customers.read'); ensure_call_logs_table($ctx['db']);
    $tid=$ctx['user']['tenant_id'];
    $kind=isset($req->query['kind']) ? (string)$req->query['kind'] : '';
    $params=[$tid]; $where="tenant_id=? AND deleted_at IS NULL";
    if(in_array($kind,['call','inquiry'],true)){ $where.=" AND kind=?"; $params[]=$kind; }
    $rows=safe_query($ctx['db'],"SELECT * FROM call_logs WHERE $where ORDER BY COALESCE(called_at, created_at) DESC LIMIT 500",$params);
    Response::json(['items'=>$rows]);
  });
  $r->add('GET','/calls/{id}', function($req,$ctx,$p){
    require_permission($ctx['user'],'customers.read'); ensure_call_logs_table($ctx['db']);
    $row=$ctx['db']->one("SELECT * FROM call_logs WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$ctx['user']['tenant_id'],$p['id']]);
    if(!$row) Response::error('Eintrag nicht gefunden',404);
    Response::json($row);
  });
  $r->add('POST','/calls', function($req,$ctx){
    // Anrufe darf jeder erfassen, der Kunden sehen darf. Wer ans Telefon
    // geht, braucht kein Schreibrecht auf Kundenstammdaten.
    require_permission($ctx['user'],'customers.read'); ensure_call_logs_table($ctx['db']);
    $tid=$ctx['user']['tenant_id']; $db=$ctx['db']; $b=$req->body;
    $caller=trim((string)($b['caller_name'] ?? ''));
    $phone=trim((string)($b['caller_phone'] ?? ''));
    $email=trim((string)($b['caller_email'] ?? ''));
    if($caller==='' && $phone==='' && $email==='') Response::error('Bitte mindestens Name, Telefonnummer oder E-Mail angeben',422);
    $id=uuid();
    // FEHLERURSACHE (behoben 20.08.2026): Hier stand
    //   in_array(($b['kind'] ?? 'call'), [...]) ? $b['kind'] : 'call'
    // Geprueft wurde also der Ersatzwert, zurueckgegeben aber $b['kind'] –
    // ohne Ersatz. Fehlte das Feld im Aufruf, wurde NULL in die Spalte
    // `kind` geschrieben, die NOT NULL ist: Datenbankfehler, Antwort 500.
    // Der alte Dialog sendete `kind` immer mit, die neue Schnellerfassung
    // nicht – deshalb trat es nur dort auf.
    $kindRaw = (string)($b['kind'] ?? 'call');
    $kind = in_array($kindRaw, ['call','inquiry'], true) ? $kindRaw : 'call';
    $projectId=$kind==='call' ? trim((string)($b['project_id'] ?? '')) : '';
    $subject=trim((string)($b['subject'] ?? ''));
    $notes=trim((string)($b['notes'] ?? ''));
    // Gleiches Muster wie bei `kind`: Erst den Wert MIT Ersatz in eine
    // Variable holen, dann pruefen. Sonst liefert der Ja-Zweig wieder den
    // fehlenden Schluessel und damit einen leeren Wert.
    $directionRaw = (string)($b['direction'] ?? 'inbound');
    $direction = in_array($directionRaw, ['inbound','outbound'], true) ? $directionRaw : 'inbound';
    $sourceRaw = (string)($b['source'] ?? 'phone');
    $source = in_array($sourceRaw, ['phone','email','website','recommendation','other'], true) ? $sourceRaw : 'phone';
    $priorityRaw=(string)($b['priority'] ?? 'normal');
    if($priorityRaw==='high') $priorityRaw='urgent';
    $priority=in_array($priorityRaw,['low','normal','urgent'],true)?$priorityRaw:'normal';
    $dateOrNull=function($value){ $v=trim((string)$value); if($v==='') return null; $ts=strtotime($v); return $ts ? date('Y-m-d',$ts) : null; };
    $calledAt=trim((string)($b['called_at'] ?? '')); if($calledAt==='') $calledAt=date('Y-m-d H:i:s'); else $calledAt=date('Y-m-d H:i:s',strtotime($calledAt));
    $handledBy=$ctx['user']['name'] ?? null;
    // Bei normalem Anruf + Auftragsverknüpfung automatisch eine Notiz im Notizstrang anlegen.
    $noteId=null;
    if($kind==='call' && $projectId!==''){
      $proj=$db->one("SELECT id FROM projects WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$projectId]);
      if(!$proj){ $projectId=''; }
      else {
        ensure_project_notes_table($db); $noteId=uuid();
        $dirLabel=$direction==='outbound'?'Ausgehender Anruf':'Eingehender Anruf';
        // Der reine Text bleibt als Rueckfall erhalten (aeltere Ansichten,
        // Export, Suche). Die Einzelangaben stehen zusaetzlich strukturiert
        // in meta_json – damit kann die Bauakte den Anruf als eigene Karte
        // darstellen statt als Textblock.
        $body="📞 ".$dirLabel.($caller!==''?" – ".$caller:'').($phone!==''?" (".$phone.")":'').($subject!==''?"\nBetreff: ".$subject:'').($notes!==''?"\n".$notes:'');
        $callMeta=json_encode([
          'type'      => 'call',
          'direction' => $direction,
          'caller'    => $caller,
          'phone'     => $phone,
          'subject'   => $subject,
          'text'      => $notes,
          'called_at' => $calledAt,
          'call_id'   => $id,
          'follow_up' => !empty($b['follow_up']) ? 1 : 0
        ], JSON_UNESCAPED_UNICODE);
        $db->exec("INSERT INTO project_notes (id,tenant_id,project_id,entry_type,author_id,author_name,body,meta_json,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,NOW(),NOW())",[$noteId,$tid,$projectId,'call',$ctx['user']['id']??null,$handledBy,$body,$callMeta]);
      }
    }
    $inquiryStatus=$kind==='inquiry' ? 'open' : 'open';
    try {
      insert_call_log($db, [
        'id'=>$id,'tenant_id'=>$tid,'kind'=>$kind,'caller_name'=>$caller?:null,'caller_phone'=>$phone?:null,'caller_email'=>$email?:null,
        'customer_id'=>$kind==='call' ? (trim((string)($b['customer_id']??''))?:null) : null,
        'company'=>trim((string)($b['company']??''))?:null,'address'=>trim((string)($b['address']??''))?:null,'zip'=>trim((string)($b['zip']??''))?:null,'city'=>trim((string)($b['city']??''))?:null,
        'direction'=>$direction,'source'=>$source,'priority'=>$priority,'desired_start'=>$dateOrNull($b['desired_start']??''),'area_m2'=>is_numeric($b['area_m2']??null)?(float)$b['area_m2']:null,
        'screed_type'=>trim((string)($b['screed_type']??''))?:null,'next_step'=>trim((string)($b['next_step']??''))?:null,'due_date'=>$dateOrNull($b['due_date']??''),
        'subject'=>$subject?:null,'notes'=>$notes?:null,'follow_up'=>!empty($b['follow_up'])?1:0,'inquiry_status'=>$inquiryStatus,'converted_project_id'=>null,'project_id'=>$projectId?:null,
        'note_id'=>$noteId,'handled_by'=>$handledBy,'called_at'=>$calledAt
      ]);
    } catch(Throwable $e) {
      // Die eigentliche Ursache muss sichtbar sein. Ein pauschales
      // "Serverfehler" hilft niemandem weiter – in einer betriebsinternen
      // Anwendung ist die Datenbankmeldung die schnellste Diagnose.
      error_log('create call/inquiry: '.$e->getMessage());
      Response::error('Anruf konnte nicht gespeichert werden: '.$e->getMessage(),500);
    }
    $row=$db->one("SELECT * FROM call_logs WHERE id=?",[$id]);
    Response::json($row,201);
  });
  $r->add('POST','/calls/{id}/create-inquiry', function($req,$ctx,$p){
    require_permission($ctx['user'],'customers.write'); ensure_call_logs_table($ctx['db']);
    $tid=$ctx['user']['tenant_id']; $db=$ctx['db'];
    $source=$db->one("SELECT * FROM call_logs WHERE tenant_id=? AND id=? AND kind='call' AND deleted_at IS NULL",[$tid,$p['id']]);
    if(!$source) Response::error('Anruf nicht gefunden',404);
    $existing=$db->one("SELECT id FROM call_logs WHERE tenant_id=? AND kind='inquiry' AND deleted_at IS NULL AND notes LIKE ? ORDER BY created_at DESC LIMIT 1",[$tid,'%Ursprünglicher Anruf: '.$p['id'].'%']);
    if($existing) Response::error('Aus diesem Anruf wurde bereits eine Anfrage erstellt',409);
    $id=uuid(); $handledBy=$ctx['user']['name'] ?? null;
    $notes=trim((string)($source['notes'] ?? ''));
    $origin='Ursprünglicher Anruf: '.$source['id'];
    $notes=$notes!=='' ? $notes."\n\n".$origin : $origin;
    try {
      insert_call_log($db, [
        'id'=>$id,'tenant_id'=>$tid,'kind'=>'inquiry','caller_name'=>$source['caller_name']??null,'caller_phone'=>$source['caller_phone']??null,'caller_email'=>$source['caller_email']??null,
        'customer_id'=>$source['customer_id']??null,
        'company'=>$source['company']??null,'address'=>$source['address']??null,'zip'=>$source['zip']??null,'city'=>$source['city']??null,'direction'=>$source['direction']??'inbound',
        'source'=>$source['source']??'phone','priority'=>$source['priority']??'normal','desired_start'=>$source['desired_start']??null,'area_m2'=>$source['area_m2']??null,
        'screed_type'=>$source['screed_type']??null,'next_step'=>$source['next_step']??null,'due_date'=>$source['due_date']??null,'subject'=>$source['subject']??null,
        'notes'=>$notes,'follow_up'=>1,'inquiry_status'=>'open','converted_project_id'=>null,'project_id'=>null,'note_id'=>null,'handled_by'=>$handledBy,'called_at'=>$source['called_at']??date('Y-m-d H:i:s')
      ]);
    } catch(Throwable $e) { error_log('create inquiry from call: '.$e->getMessage()); Response::error('Anfrage konnte aus dem Anruf nicht erstellt werden. Bitte Datenbank-Migrationen prüfen.',500); }
    Response::json($db->one("SELECT * FROM call_logs WHERE id=?",[$id]),201);
  });
  $r->add('PUT','/calls/{id}', function($req,$ctx,$p){
    require_permission($ctx['user'],'customers.write'); ensure_call_logs_table($ctx['db']);
    $tid=$ctx['user']['tenant_id']; $db=$ctx['db']; $b=$req->body;
    $row=$db->one("SELECT id,kind,inquiry_status FROM call_logs WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$p['id']]);
    if(!$row) Response::error('Eintrag nicht gefunden',404);
    $dateOrNull=function($value){ $v=trim((string)$value); if($v==='') return null; $ts=strtotime($v); return $ts ? date('Y-m-d',$ts) : null; };
    $changes=[];
    foreach(['caller_name','caller_phone','caller_email','customer_id','company','address','zip','city','subject','notes','screed_type','next_step'] as $f){ if(array_key_exists($f,$b)){ $changes[$f]=trim((string)$b[$f]) ?: null; } }
    if(array_key_exists('source',$b) && in_array($b['source'],['phone','email','website','recommendation','other'],true)){ $changes['source']=$b['source']; }
    if(array_key_exists('priority',$b)){ $prio=(string)$b['priority']; if($prio==='high') $prio='urgent'; if(in_array($prio,['low','normal','urgent'],true)) $changes['priority']=$prio; }
    if(array_key_exists('desired_start',$b)){ $changes['desired_start']=$dateOrNull($b['desired_start']); }
    if(array_key_exists('due_date',$b)){ $changes['due_date']=$dateOrNull($b['due_date']); }
    if(array_key_exists('area_m2',$b)){ $changes['area_m2']=is_numeric($b['area_m2']) ? (float)$b['area_m2'] : null; }
    if(array_key_exists('follow_up',$b)){ $changes['follow_up']=!empty($b['follow_up'])?1:0; }
    if(array_key_exists('direction',$b) && in_array($b['direction'],['inbound','outbound'],true)){ $changes['direction']=$b['direction']; }
    if(($row['kind'] ?? 'call')==='inquiry' && array_key_exists('inquiry_status',$b) && in_array($b['inquiry_status'],['open','in_progress','cancelled','converted'],true)){ $changes['inquiry_status']=$b['inquiry_status']; if($b['inquiry_status']==='cancelled'){ $changes['follow_up']=0; } }
    update_call_log($db,$tid,$p['id'],$changes);
    Response::json($db->one("SELECT * FROM call_logs WHERE id=?",[$p['id']]));
  });
  $r->add('POST','/inquiries/{id}/cancel', function($req,$ctx,$p){
    require_permission($ctx['user'],'customers.write'); ensure_call_logs_table($ctx['db']);
    $tid=$ctx['user']['tenant_id']; $db=$ctx['db'];
    $row=$db->one("SELECT id FROM call_logs WHERE tenant_id=? AND id=? AND kind='inquiry' AND deleted_at IS NULL",[$tid,$p['id']]);
    if(!$row) Response::error('Anfrage nicht gefunden',404);
    $db->exec("UPDATE call_logs SET inquiry_status='cancelled', follow_up=0, updated_at=NOW() WHERE tenant_id=? AND id=?",[$tid,$p['id']]);
    Response::json($db->one("SELECT * FROM call_logs WHERE id=?",[$p['id']]));
  });
  $r->add('DELETE','/calls/{id}', function($req,$ctx,$p){
    require_permission($ctx['user'],'customers.write'); ensure_call_logs_table($ctx['db']);
    $tid=$ctx['user']['tenant_id'];
    purge_where($ctx['db'],'call_logs',"tenant_id=? AND id=?",[$tid,$p['id']]);
    Response::json(['ok'=>true]);
  });

  // Benutzer anlegen (mit Passwort + optionaler Rollenzuweisung)
  $r->add('POST','/admin/users', function($req,$ctx){
    require_permission($ctx['user'],'users.write');
    $tid=$ctx['user']['tenant_id']; $db=$ctx['db'];
    $name=trim((string)($req->body['name'] ?? ''));
    $email=strtolower(trim((string)($req->body['email'] ?? '')));
    $password=(string)($req->body['password'] ?? '');
    if($name==='' || $email==='') Response::error('Name und E-Mail sind erforderlich',422);
    if(!filter_var($email,FILTER_VALIDATE_EMAIL)) Response::error('Ungültige E-Mail-Adresse',422);
    if(strlen($password)<8) Response::error('Passwort muss mindestens 8 Zeichen haben',422);
    $exists=$db->one("SELECT id FROM users WHERE tenant_id=? AND LOWER(email)=? AND deleted_at IS NULL",[$tid,$email]);
    if($exists) Response::error('Es existiert bereits ein Benutzer mit dieser E-Mail',409);
    add_column_if_missing($db,'users','must_change_password','TINYINT(1) NOT NULL DEFAULT 0');
    $mustChange=isset($req->body['must_change_password']) ? (int)!!$req->body['must_change_password'] : 0;
    $id=uuid();
    $db->exec("INSERT INTO users (id,tenant_id,name,email,password_hash,is_active,two_factor_enabled,must_change_password,created_at,updated_at) VALUES (?,?,?,?,?,?,0,?,NOW(),NOW())",
      [$id,$tid,$name,$email,password_hash($password,PASSWORD_DEFAULT),isset($req->body['is_active'])?(int)!!$req->body['is_active']:1,$mustChange]);
    $roleIds=$req->body['role_ids'] ?? [];
    if(is_array($roleIds)){
      foreach($roleIds as $roleId){
        $role=$db->one("SELECT id FROM roles WHERE id=? AND deleted_at IS NULL AND (tenant_id=? OR tenant_id IS NULL)",[(string)$roleId,$tid]);
        if($role) $db->exec("INSERT INTO user_roles (user_id,role_id) VALUES (?,?)",[$id,$role['id']]);
      }
    }
    $ctx['audit']->log($tid,$ctx['user']['id'],'users',$id,'create',null,['name'=>$name,'email'=>$email]);
    $row=$db->one("SELECT id,name,email,is_active,two_factor_enabled,created_at FROM users WHERE id=?",[$id]);
    Response::json($row,201);
  });
  // Benutzer aktualisieren (Name, Aktiv-Status, optional Passwort zurücksetzen)
  $r->add('PUT','/admin/users/{id}', function($req,$ctx,$p){
    require_permission($ctx['user'],'users.write');
    $tid=$ctx['user']['tenant_id']; $db=$ctx['db'];
    $target=$db->one("SELECT id FROM users WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$p['id']]);
    if(!$target) Response::error('Benutzer nicht gefunden',404);
    $sets=[]; $vals=[];
    if(array_key_exists('name',$req->body)){ $sets[]='name=?'; $vals[]=trim((string)$req->body['name']); }
    if(array_key_exists('is_active',$req->body)){ $sets[]='is_active=?'; $vals[]=(int)!!$req->body['is_active']; }
    if(!empty($req->body['password'])){ if(strlen((string)$req->body['password'])<8) Response::error('Passwort muss mindestens 8 Zeichen haben',422); $sets[]='password_hash=?'; $vals[]=password_hash((string)$req->body['password'],PASSWORD_DEFAULT); }
    if(!$sets) Response::error('Keine Änderungen',422);
    $sets[]='updated_at=NOW()'; $vals[]=$tid; $vals[]=$p['id'];
    $db->exec("UPDATE users SET ".implode(',',$sets)." WHERE tenant_id=? AND id=?",$vals);
    $ctx['audit']->log($tid,$ctx['user']['id'],'users',$p['id'],'update',null,$req->body);
    $row=$db->one("SELECT id,name,email,is_active,two_factor_enabled,last_login_at FROM users WHERE id=?",[$p['id']]);
    Response::json($row);
  });
  $r->add('PUT','/admin/users/{id}/roles', function($req,$ctx,$p){
    require_permission($ctx['user'],'users.write');
    $roleIds=$req->body['role_ids'] ?? [];
    if(!is_array($roleIds)) Response::error('role_ids muss eine Liste sein',422);
    $tid=$ctx['user']['tenant_id']; $db=$ctx['db'];
    $targetUser=$db->one("SELECT id,tenant_id,name,email FROM users WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$p['id']]);
    if(!$targetUser) Response::error('Benutzer nicht gefunden',404);
    $before=$db->all("SELECT ur.role_id, r.name role_name FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE ur.user_id=?",[$p['id']]);
    $valid=[];
    foreach($roleIds as $roleId){
      $role=$db->one("SELECT id,name FROM roles WHERE id=? AND deleted_at IS NULL AND (tenant_id=? OR tenant_id IS NULL)",[(string)$roleId,$tid]);
      if($role) $valid[$role['id']]=$role['id'];
    }
    $db->exec("DELETE FROM user_roles WHERE user_id=?",[$p['id']]);
    foreach(array_values($valid) as $roleId){
      $db->exec("INSERT INTO user_roles (user_id,role_id) VALUES (?,?)",[$p['id'],$roleId]);
    }
    $after=$db->all("SELECT ur.role_id, r.name role_name FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE ur.user_id=?",[$p['id']]);
    $ctx['audit']->log($tid,$ctx['user']['id'],'user_roles',$p['id'],'update',$before,$after);
    Response::json(['ok'=>true,'user_id'=>$p['id'],'role_ids'=>array_values($valid),'roles'=>$after]);
  });

  $r->add('GET','/dashboard', function($req,$ctx){
    $tid=$ctx['user']['tenant_id']; $db=$ctx['db']; ensure_planning_columns($db); $vehiclePlate=vehicle_plate_expr($db);
    // Defensive: a single missing table/column on an older database must not 500 the whole dashboard.
    $count=function(string $table, string $where='deleted_at IS NULL') use($db,$tid){ try { return (int)($db->one("SELECT COUNT(*) c FROM `$table` WHERE tenant_id=? AND $where",[$tid])['c'] ?? 0); } catch(\Throwable $e){ return 0; } };
    $inactiveStatuses="('done','completed','complete','closed','archived','cancelled','canceled','storniert','abgeschlossen')";
    $activeSiteWhere="deleted_at IS NULL AND (status IS NULL OR status='' OR LOWER(status) NOT IN $inactiveStatuses)"
      ." AND (project_id IS NULL OR project_id IN (SELECT p.id FROM projects p WHERE p.tenant_id=construction_sites.tenant_id AND p.deleted_at IS NULL AND (p.status IS NULL OR p.status='' OR LOWER(p.status) NOT IN $inactiveStatuses)))";
    $activeProjectWhere="deleted_at IS NULL AND (status IS NULL OR status='' OR LOWER(status) NOT IN $inactiveStatuses)";
    $stats=[
      'projects'=>$count('projects',$activeProjectWhere), 'sites'=>$count('construction_sites',$activeSiteWhere), 'open_tasks'=>$count('tasks',"deleted_at IS NULL AND status NOT IN ('done','archived')"),
      'open_defects'=>$count('defects',"deleted_at IS NULL AND status NOT IN ('closed')"), 'documents'=>$count('documents'),
      'assignments_today'=>$count('planning_assignments',"deleted_at IS NULL AND DATE(start_at)=CURDATE()"),
      'overdue'=>$count('tasks',"deleted_at IS NULL AND due_date IS NOT NULL AND due_date<CURDATE() AND status NOT IN ('done','archived')"),
      'quality_open'=>$count('quality_protocols',"deleted_at IS NULL AND status NOT IN ('approved','archived')")
    ];
    $today=safe_query($db,"SELECT pa.*, COALESCE(pa.project_id, cs.project_id) resolved_project_id, cs.name site_name, cs.address site_address, cs.city site_city, p.name project_name, cr.name crew_name, v.name vehicle_name, $vehiclePlate vehicle_plate, m.name machine_name FROM planning_assignments pa LEFT JOIN construction_sites cs ON cs.id=pa.site_id LEFT JOIN projects p ON p.id=COALESCE(pa.project_id, cs.project_id) LEFT JOIN crews cr ON cr.id=pa.crew_id LEFT JOIN vehicles v ON v.id=pa.vehicle_id LEFT JOIN machines m ON m.id=pa.machine_id WHERE pa.tenant_id=? AND pa.deleted_at IS NULL AND DATE(pa.start_at)=CURDATE() AND (p.id IS NULL OR p.status IS NULL OR LOWER(p.status) NOT IN ('done','completed','complete','closed','archived','cancelled','canceled','storniert','abgeschlossen')) ORDER BY pa.start_at LIMIT 50",[$tid]);
    // "Meine heutigen Baustellen": Einsaetze meiner Kolonne(n) oder mir direkt zugewiesen.
    $uid=$ctx['user']['id'];
    $myCrewIds=[];
    try { foreach(safe_query($db,"SELECT DISTINCT crew_id FROM crew_members WHERE tenant_id=? AND user_id=? AND deleted_at IS NULL",[$tid,$uid]) as $cm){ if(!empty($cm['crew_id'])) $myCrewIds[]=$cm['crew_id']; } } catch(\Throwable $e){}
    $myToday=[];
    foreach($today as $row){
      $mine=false;
      if(!empty($row['assigned_user_id']) && $row['assigned_user_id']===$uid) $mine=true;
      if(!empty($row['crew_id']) && in_array($row['crew_id'],$myCrewIds,true)) $mine=true;
      if($mine) $myToday[]=$row;
    }
    $mods=[]; foreach($ctx['modules'] as $m){ $table=safe_identifier($m['table']); $mods[]=['key'=>$m['key'],'label'=>$m['label'],'count'=>$count($table)]; }
    Response::json(['stats'=>$stats,'today'=>$today,'my_today'=>$myToday,'modules'=>$mods]);
  }, ['dashboard.read']);

  $r->add('GET','/module/{key}', function($req,$ctx,$p){
    $m=$ctx['moduleMap'][$p['key']] ?? null; if(!$m) Response::error('Modul nicht gefunden',404);
    require_module_read($ctx['user'],$m);
    ensure_runtime_columns($ctx['db']);
    if($p['key']==='screed_types') ensure_default_screed_types($ctx['db'],$ctx['user']['tenant_id']);
    $table=safe_identifier($m['table']); $tid=$ctx['user']['tenant_id']; $limit=max(1,min(1000,(int)($req->query['limit'] ?? 250))); $offset=max(0,(int)($req->query['offset'] ?? 0)); $q=trim((string)($req->query['q'] ?? ''));
    $existing=table_columns($ctx['db'],$table);
    $select=module_select_columns($m,$existing);
    $params=[$tid]; $where="tenant_id=? AND deleted_at IS NULL";
    if($q !== ''){ $likes=[]; foreach($m['searchFields'] as $sf){ $sf=safe_identifier($sf); if(!in_array($sf,$existing,true)) continue; $likes[]="`$sf` LIKE ?"; $params[]='%'.$q.'%'; } if($likes) $where.=' AND ('.implode(' OR ',$likes).')'; }
    // Kunden-Quelle: Lexware (mit lexware_contact_id) vs. manuell (in Bauakten-App angelegt)
    $source=trim((string)($req->query['source'] ?? ''));
    if($source!=='' && $table==='customers' && in_array('lexware_contact_id',$existing,true)){
      if($source==='lexware') $where.=" AND lexware_contact_id IS NOT NULL AND lexware_contact_id<>''";
      elseif($source==='manual') $where.=" AND (lexware_contact_id IS NULL OR lexware_contact_id='')";
    }
    $total=(int)($ctx['db']->one("SELECT COUNT(*) c FROM `$table` WHERE $where",$params)['c'] ?? 0);
    $rows=$ctx['db']->all("SELECT $select FROM `$table` WHERE $where ORDER BY COALESCE(updated_at,created_at) DESC LIMIT $limit OFFSET $offset",$params);
    Response::json(['items'=>$rows,'module'=>$m['key'],'total'=>$total,'limit'=>$limit,'offset'=>$offset]);
  });

  $r->add('GET','/module/{key}/{id}', function($req,$ctx,$p){
    $m=$ctx['moduleMap'][$p['key']] ?? null; if(!$m) Response::error('Modul nicht gefunden',404);
    require_module_read($ctx['user'],$m); ensure_runtime_columns($ctx['db']); $table=safe_identifier($m['table']);
    $existing=table_columns($ctx['db'],$table); $select=module_select_columns($m,$existing);
    $row=$ctx['db']->one("SELECT $select FROM `$table` WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$ctx['user']['tenant_id'],$p['id']]);
    if(!$row) Response::error('Nicht gefunden',404); Response::json($row);
  });

  $r->add('POST','/module/{key}', function($req,$ctx,$p){
    $m=$ctx['moduleMap'][$p['key']] ?? null; if(!$m) Response::error('Modul nicht gefunden',404);
    require_permission($ctx['user'],$m['permission'].'.write'); ensure_runtime_columns($ctx['db']);
    $required=array_values(array_map(fn($f)=>$f['key'], array_filter($m['fields'], fn($f)=>!empty($f['required'])))); assert_required($req->body,$required);
    $table=safe_identifier($m['table']); $existing=table_columns($ctx['db'],$table); $id=uuid(); $tid=$ctx['user']['tenant_id']; $cols=['id','tenant_id']; $qs=['?','?']; $vals=[$id,$tid];
    if($m['key']==='projects' && in_array('project_no',$existing,true)){
      // Bauaktennummern werden serverseitig vergeben: aktuelles Jahr + fortlaufende Nummer, z. B. 2026-001.
      $req->body['project_no']=next_project_no($ctx['db'],$tid);
    }
    foreach($m['fields'] as $field){ $key=safe_identifier($field['key']); if($key==='id' || $key==='tenant_id' || !in_array($key,$existing,true)) continue; $cols[]="`$key`"; $qs[]='?'; $vals[]=field_value($req->body,$field); }
    if ($table === 'users' && in_array('password_hash',$existing,true)) { $cols[]='password_hash'; $qs[]='?'; $vals[]=password_hash((string)($req->body['initial_password'] ?? bin2hex(random_bytes(6))), PASSWORD_DEFAULT); }
    $ctx['db']->exec("INSERT INTO `$table` (".implode(',',$cols).",created_at,updated_at) VALUES (".implode(',',$qs).",NOW(),NOW())",$vals);
    $select=module_select_columns($m,$existing);
    $row=$ctx['db']->one("SELECT $select FROM `$table` WHERE id=?",[$id]); $ctx['audit']->log($tid,$ctx['user']['id'],$m['key'],$id,'create',null,$row); Response::json($row,201);
  });

  $r->add('PUT','/module/{key}/{id}', function($req,$ctx,$p){
    $m=$ctx['moduleMap'][$p['key']] ?? null; if(!$m) Response::error('Modul nicht gefunden',404);
    require_permission($ctx['user'],$m['permission'].'.write'); ensure_runtime_columns($ctx['db']); $table=safe_identifier($m['table']); $tid=$ctx['user']['tenant_id']; $existing=table_columns($ctx['db'],$table);
    $before=$ctx['db']->one("SELECT * FROM `$table` WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$p['id']]); if(!$before) Response::error('Nicht gefunden',404);
    if($m['key']==='projects' && array_key_exists('status',$req->body) && (string)$req->body['status'] === 'cancelled' && (string)($before['status'] ?? '') !== 'cancelled') {
      Response::error('Bauakten müssen über „Bauakte stornieren" mit Passwortbestätigung storniert werden.',403);
    }
    $sets=[]; $vals=[]; foreach($m['fields'] as $field){ $key=safe_identifier($field['key']); if($m['key']==='projects' && $key==='project_no') continue; if(array_key_exists($key,$req->body) && in_array($key,$existing,true)){ $sets[]="`$key`=?"; $vals[]=field_value($req->body,$field); }}
    if(!$sets) Response::error('Keine Änderungen',422); $vals[]=$tid; $vals[]=$p['id'];
    $ctx['db']->exec("UPDATE `$table` SET ".implode(',',$sets).", updated_at=NOW() WHERE tenant_id=? AND id=?",$vals);
    $select=module_select_columns($m,$existing);
    $after=$ctx['db']->one("SELECT $select FROM `$table` WHERE id=?",[$p['id']]); $ctx['audit']->log($tid,$ctx['user']['id'],$m['key'],$p['id'],'update',$before,$after);
    // Statuswechsel einer Bauakte im Notiz-/Verlaufstrang sichtbar machen
    if($m['key']==='projects' && array_key_exists('status',$req->body) && ($before['status']??null) !== ($after['status']??null)){
      log_project_event($ctx['db'],$ctx['user'],$p['id'],'Status geändert: '.status_label_de((string)($after['status']??'')),'status');
    }
    Response::json($after);
  });

  $r->add('DELETE','/module/{key}/{id}', function($req,$ctx,$p){
    $m=$ctx['moduleMap'][$p['key']] ?? null; if(!$m) Response::error('Modul nicht gefunden',404);
    if($m['key']==='audit_logs') Response::error('Audit Logs sind revisionssicher und nicht löschbar',409);
    if($m['key']==='projects') Response::error('Bauakten müssen über „Bauakte löschen" mit Warnhinweis und Passwortbestätigung gelöscht werden.',403);
    require_permission($ctx['user'],$m['permission'].'.delete'); $table=safe_identifier($m['table']); $tid=$ctx['user']['tenant_id'];
    $before=$ctx['db']->one("SELECT * FROM `$table` WHERE tenant_id=? AND id=?",[$tid,$p['id']]); if(!$before) Response::error('Nicht gefunden',404);
    // Endgueltig loeschen - samt Dateien. Der Pruefeintrag im Protokoll
    // bleibt erhalten, damit nachvollziehbar ist, WER WAS geloescht hat.
    purge_where($ctx['db'],$table,"tenant_id=? AND id=?",[$tid,$p['id']]);
    $ctx['audit']->log($tid,$ctx['user']['id'],$m['key'],$p['id'],'delete',$before,null);
    Response::json(['ok'=>true]);
  });


  $r->add('POST','/workflow/customer-project-site', function($req,$ctx){
    require_permission($ctx['user'],'customers.read');
    require_permission($ctx['user'],'projects.write');
    require_permission($ctx['user'],'sites.write');
    $body=$req->body; $customer=$body['customer'] ?? []; $project=$body['project'] ?? []; $site=$body['site'] ?? []; $rooms=$body['rooms'] ?? []; $inquiryId=trim((string)($body['inquiry_id'] ?? ''));
    if(!is_array($customer) || !is_array($project) || !is_array($site)) Response::error('Ungültiger Workflow-Payload',422);
    $customerId=$customer['customer_id'] ?? ($customer['id'] ?? null);
    foreach(['name'] as $field){ if(empty($project[$field])) Response::error('Auftrags-/Projektname fehlt',422); if(empty($site[$field])) Response::error('Baustellenname fehlt',422); }
    $tid=$ctx['user']['tenant_id']; $uid=$ctx['user']['id']; $db=$ctx['db']; $audit=$ctx['audit'];
    $wantNew=!empty($customer['create_new']);
    if((empty($customerId) || $wantNew) && !empty($customer['name'])){
      require_permission($ctx['user'],'customers.write');
      // Weiche Dublettenpruefung: bestehenden Kunden (E-Mail/Telefon/Name) wiederverwenden, sofern nicht erzwungen neu.
      $dupe=null;
      if(empty($customer['force_new'])){
        if(!empty($customer['email'])) $dupe=$db->one("SELECT * FROM customers WHERE tenant_id=? AND email=? AND deleted_at IS NULL LIMIT 1",[$tid,$customer['email']]);
        if(!$dupe && !empty($customer['phone'])) $dupe=$db->one("SELECT * FROM customers WHERE tenant_id=? AND phone=? AND deleted_at IS NULL LIMIT 1",[$tid,$customer['phone']]);
        if(!$dupe) $dupe=$db->one("SELECT * FROM customers WHERE tenant_id=? AND name=? AND deleted_at IS NULL LIMIT 1",[$tid,$customer['name']]);
      }
      if($dupe){ $existingCustomer=$dupe; $customerId=$dupe['id']; }
      else {
        $newId=uuid();
        $custNo=(trim((string)($customer['customer_no']??''))!=='')?$customer['customer_no']:null;
        $db->exec("INSERT INTO customers (id,tenant_id,name,customer_no,email,phone,address,zip,city,notes,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?,NOW(),NOW())",[$newId,$tid,$customer['name'],$custNo,$customer['email']??null,$customer['phone']??null,$customer['address']??null,$customer['zip']??null,$customer['city']??null,'In Bauakten-App angelegt']);
        $customerId=$newId;
        $existingCustomer=$db->one("SELECT * FROM customers WHERE tenant_id=? AND id=?",[$tid,$newId]);
      }
    } else {
      if(empty($customerId)) Response::error('Bitte einen Kunden auswählen oder neu anlegen',422);
      $existingCustomer=$db->one("SELECT * FROM customers WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$customerId]);
      if(!$existingCustomer) Response::error('Gewählter Kunde wurde in der Kundenkartei nicht gefunden',404);
    }
    $customer=array_merge($existingCustomer, $customer);
    ensure_project_number_sequence_table($db);
    if($inquiryId!=='') ensure_call_logs_table($db);
    $result=$db->tx(function($db) use($tid,$uid,$customerId,$customer,$project,$site,$rooms,$audit,$inquiryId){
      $projectId=uuid();
      $projectNo=next_project_no($db,$tid);
      $db->exec("INSERT INTO projects (id,tenant_id,customer_id,name,project_no,status,address,zip,city,start_date,end_date,budget_net,notes,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,NOW(),NOW())",[$projectId,$tid,$customerId,$project['name'],$projectNo,$project['status']??'new',$project['address']??($customer['address']??null),$project['zip']??($customer['zip']??null),$project['city']??($customer['city']??null),$project['start_date']??null,$project['end_date']??null,$project['budget_net']??null,$project['notes']??null]);
      $siteId=uuid();
      $siteNo=trim((string)($site['site_no'] ?? '')) !== '' ? $site['site_no'] : $projectNo;
      $db->exec("INSERT INTO construction_sites (id,tenant_id,project_id,customer_id,name,site_no,status,address,zip,city,site_manager,phone,screed_type,area_m2,planned_start,planned_end,notes,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,NOW(),NOW())",[$siteId,$tid,$projectId,$customerId,$site['name'],$siteNo,$site['status']??'planned',$site['address']??($project['address']??null),$site['zip']??($project['zip']??null),$site['city']??($project['city']??null),$site['site_manager']??null,$site['phone']??null,$site['screed_type']??null,$site['area_m2']??null,$site['planned_start']??null,$site['planned_end']??null,$site['notes']??null]);
      $roomIds=[];
      if(is_array($rooms)) foreach($rooms as $room){ if(!is_array($room) || empty($room['name'])) continue; $rid=uuid(); $roomIds[]=$rid; $db->exec("INSERT INTO rooms (id,tenant_id,site_id,name,floor,area_m2,screed_type,height_mm,heating,status,notes,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?,?,NOW(),NOW())",[$rid,$tid,$siteId,$room['name'],$room['floor']??null,$room['area_m2']??null,$room['screed_type']??($site['screed_type']??null),$room['height_mm']??null,!empty($room['heating'])?1:0,'open',$room['notes']??null]); }
      $date=$site['planned_start'] ?? $project['start_date'] ?? date('Y-m-d');
      $taskId=uuid();
      $db->exec("INSERT INTO tasks (id,tenant_id,site_id,room_id,title,description,priority,status,due_date,assigned_user_id,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?,NOW(),NOW())",[$taskId,$tid,$siteId,$roomIds[0]??null,'Erstbegehung / Untergrund prüfen','Automatisch aus Auftragserfassung angelegt: Untergrund, Dämmung, Abdichtung, Randdämmstreifen, Höhen und Fotopflichten prüfen.','high','open',$date,$uid]);
      if($inquiryId!=='') {
        try { $db->exec("UPDATE call_logs SET inquiry_status='converted', converted_project_id=?, project_id=?, follow_up=0, updated_at=NOW() WHERE tenant_id=? AND id=? AND kind='inquiry' AND deleted_at IS NULL",[$projectId,$projectId,$tid,$inquiryId]); }
        catch(Throwable $e) { error_log('mark inquiry converted: '.$e->getMessage()); }
      }
      $created=['customer_id'=>$customerId,'project_id'=>$projectId,'project_no'=>$projectNo,'site_id'=>$siteId,'site_no'=>$siteNo,'room_ids'=>$roomIds,'task_id'=>$taskId,'inquiry_id'=>$inquiryId ?: null];
      $audit->log($tid,$uid,'workflow_customer_project_site',$siteId,'create',null,$created);
      return $created;
    });
    Response::json(['ok'=>true,'workflow'=>'customer_project_site','created'=>$result],201);
  }, ['customers.read','projects.write','sites.write']);


  $r->add('POST','/projects/{id}/initial-inspection/skip', function($req,$ctx,$p){
    require_permission($ctx['user'],'inspections.write');
    $db=$ctx['db']; $tid=$ctx['user']['tenant_id']; $uid=$ctx['user']['id']; $projectId=$p['id'];
    $project=$db->one("SELECT * FROM projects WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$projectId]);
    if(!$project) Response::error('Bauakte nicht gefunden',404);
    $siteId=trim((string)($req->body['site_id'] ?? ''));
    if($siteId==='') {
      $site=$db->one("SELECT id FROM construction_sites WHERE tenant_id=? AND project_id=? AND deleted_at IS NULL ORDER BY site_no,name LIMIT 1",[$tid,$projectId]);
      $siteId=(string)($site['id'] ?? '');
    } else {
      $site=$db->one("SELECT id FROM construction_sites WHERE tenant_id=? AND id=? AND project_id=? AND deleted_at IS NULL",[$tid,$siteId,$projectId]);
      if(!$site) Response::error('Baustelle nicht gefunden',404);
    }
    if($siteId==='') Response::error('Keine Baustelle zur Bauakte gefunden',422);
    $existing=$db->one("SELECT id FROM inspection_results WHERE tenant_id=? AND site_id=? AND deleted_at IS NULL AND (result_json LIKE '%initial_site_inspection%' OR result_json LIKE '%not_required%') ORDER BY created_at DESC LIMIT 1",[$tid,$siteId]);
    $payload=['context'=>['project_id'=>$projectId,'site_id'=>$siteId,'inspector_name'=>$ctx['user']['name'] ?? null,'inspection_date'=>date('Y-m-d')],'checklist'=>[],'measurements'=>[],'photo_requirements'=>[],'media_groups'=>[],'export_options'=>[],'photos'=>[],'videos'=>[],'result'=>'not_required','rejection_reason'=>'','type'=>'initial_site_inspection','note'=>'Keine Erstbegehung notwendig'];
    if($existing) {
      $db->exec("UPDATE inspection_results SET status='skipped', result_json=?, notes='Keine Erstbegehung notwendig', updated_at=NOW() WHERE tenant_id=? AND id=?",[json_encode($payload,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES),$tid,$existing['id']]);
      $id=$existing['id'];
    } else {
      $id=uuid();
      $db->exec("INSERT INTO inspection_results (id,tenant_id,template_id,site_id,room_id,status,result_json,signed_by,signed_at,notes,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?,NOW(),NOW())",[$id,$tid,null,$siteId,null,'skipped',json_encode($payload,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES),$ctx['user']['name'] ?? null,date('Y-m-d H:i:s'),'Keine Erstbegehung notwendig']);
    }
    try { $ctx['audit']->log($tid,$uid,'initial_inspection',$id,'skip',null,['project_id'=>$projectId,'site_id'=>$siteId]); } catch(Throwable $e) {}
    Response::json(['ok'=>true,'inspection_result_id'=>$id,'status'=>'skipped','result'=>'not_required']);
  }, ['inspections.write']);


  $r->add('GET','/material-orders/summary', function($req,$ctx,$p){
    require_permission($ctx['user'],'projects.read'); ensure_material_orders_table($ctx['db']);
    $tid=$ctx['user']['tenant_id']; $db=$ctx['db'];
    $open=(int)($db->one("SELECT COUNT(*) c FROM material_orders WHERE tenant_id=? AND deleted_at IS NULL AND status='to_order'",[$tid])['c'] ?? 0);
    $ordered=(int)($db->one("SELECT COUNT(*) c FROM material_orders WHERE tenant_id=? AND deleted_at IS NULL AND status='ordered'",[$tid])['c'] ?? 0);
    $done=(int)($db->one("SELECT COUNT(*) c FROM material_orders WHERE tenant_id=? AND deleted_at IS NULL AND status='done'",[$tid])['c'] ?? 0);
    $cancelled=(int)($db->one("SELECT COUNT(*) c FROM material_orders WHERE tenant_id=? AND deleted_at IS NULL AND status='cancelled'",[$tid])['c'] ?? 0);
    Response::json(['open'=>$open,'open_count'=>$open,'ordered'=>$ordered,'done'=>$done,'cancelled'=>$cancelled,'stats'=>['to_order'=>$open,'ordered'=>$ordered,'done'=>$done,'cancelled'=>$cancelled]]);
  });

  $r->add('GET','/material-orders', function($req,$ctx,$p){
    require_permission($ctx['user'],'projects.read'); ensure_material_orders_table($ctx['db']);
    $tid=$ctx['user']['tenant_id']; $db=$ctx['db'];
    $status=trim((string)($req->query['status'] ?? ''));
    $q=trim((string)($req->query['q'] ?? ''));
    $limit=max(1,min(500,(int)($req->query['limit'] ?? 250)));
    $where=["mo.tenant_id=?","mo.deleted_at IS NULL"];
    $params=[$tid];
    if($status!=='' && material_order_status_valid($status)){ $where[]="mo.status=?"; $params[]=$status; }
    if($q!==''){
      $where[]="(mo.order_no LIKE ? OR mo.notes LIKE ? OR mo.delivery_address LIKE ? OR s.name LIKE ? OR p.name LIKE ? OR p.project_no LIKE ? OR cs.name LIKE ? OR cs.site_no LIKE ?)";
      $like='%'.$q.'%'; array_push($params,$like,$like,$like,$like,$like,$like,$like,$like);
    }
    $whereSql=implode(' AND ',$where);
    $params[]=$limit;
    $orders=safe_query($db,"SELECT mo.*, p.name project_name, p.project_no, p.customer_id, cs.name site_name, cs.site_no, s.name supplier_name, s.contact_person supplier_contact_person, s.email supplier_email, s.phone supplier_phone, s.address supplier_address, s.zip supplier_zip, s.city supplier_city FROM material_orders mo LEFT JOIN projects p ON p.id=mo.project_id AND p.tenant_id=mo.tenant_id AND p.deleted_at IS NULL LEFT JOIN construction_sites cs ON cs.id=mo.site_id AND cs.tenant_id=mo.tenant_id AND cs.deleted_at IS NULL LEFT JOIN suppliers s ON s.id=mo.supplier_id AND s.tenant_id=mo.tenant_id AND s.deleted_at IS NULL WHERE $whereSql ORDER BY FIELD(mo.status,'to_order','ordered','done','cancelled'), COALESCE(mo.requested_delivery_date,'9999-12-31') ASC, mo.created_at DESC LIMIT ?",$params);
    foreach($orders as &$order){ $order['items']=safe_query($db,"SELECT * FROM material_order_items WHERE tenant_id=? AND order_id=? AND deleted_at IS NULL ORDER BY position_no ASC, created_at ASC",[$tid,$order['id']]); }
    unset($order);
    $open=(int)($db->one("SELECT COUNT(*) c FROM material_orders WHERE tenant_id=? AND deleted_at IS NULL AND status='to_order'",[$tid])['c'] ?? 0);
    $ordered=(int)($db->one("SELECT COUNT(*) c FROM material_orders WHERE tenant_id=? AND deleted_at IS NULL AND status='ordered'",[$tid])['c'] ?? 0);
    $done=(int)($db->one("SELECT COUNT(*) c FROM material_orders WHERE tenant_id=? AND deleted_at IS NULL AND status='done'",[$tid])['c'] ?? 0);
    $cancelled=(int)($db->one("SELECT COUNT(*) c FROM material_orders WHERE tenant_id=? AND deleted_at IS NULL AND status='cancelled'",[$tid])['c'] ?? 0);
    Response::json(['items'=>$orders,'open'=>$open,'open_count'=>$open,'stats'=>['to_order'=>$open,'ordered'=>$ordered,'done'=>$done,'cancelled'=>$cancelled]]);
  });

  $r->add('GET','/projects/{id}/material-orders', function($req,$ctx,$p){
    require_permission($ctx['user'],'projects.read'); ensure_material_orders_table($ctx['db']);
    $tid=$ctx['user']['tenant_id']; $db=$ctx['db'];
    $project=$db->one("SELECT id FROM projects WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$p['id']]);
    if(!$project) Response::error('Bauakte nicht gefunden',404);
    $orders=safe_query($db,"SELECT mo.*, s.name supplier_name, s.contact_person supplier_contact_person, s.email supplier_email, s.phone supplier_phone, s.address supplier_address, s.zip supplier_zip, s.city supplier_city FROM material_orders mo LEFT JOIN suppliers s ON s.id=mo.supplier_id AND s.tenant_id=mo.tenant_id AND s.deleted_at IS NULL WHERE mo.tenant_id=? AND mo.project_id=? AND mo.deleted_at IS NULL ORDER BY mo.created_at DESC",[$tid,$p['id']]);
    foreach($orders as &$order){ $order['items']=safe_query($db,"SELECT * FROM material_order_items WHERE tenant_id=? AND order_id=? AND deleted_at IS NULL ORDER BY position_no ASC, created_at ASC",[$tid,$order['id']]); }
    unset($order);
    Response::json(['items'=>$orders]);
  });

  $r->add('POST','/projects/{id}/material-orders', function($req,$ctx,$p){
    require_permission($ctx['user'],'projects.write'); ensure_material_orders_table($ctx['db']);
    $tid=$ctx['user']['tenant_id']; $db=$ctx['db']; $b=$req->body;
    $project=$db->one("SELECT * FROM projects WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$p['id']]);
    if(!$project) Response::error('Bauakte nicht gefunden',404);
    $supplierId=trim((string)($b['supplier_id'] ?? '')) ?: null;
    if($supplierId){ $supplier=$db->one("SELECT id FROM suppliers WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$supplierId]); if(!$supplier) Response::error('Lieferant nicht gefunden',404); }
    $status=trim((string)($b['status'] ?? 'to_order')); if(!material_order_status_valid($status)) $status='to_order';
    $items=$b['items'] ?? [];
    if(!is_array($items) || !count(array_filter($items, fn($i)=>is_array($i) && trim((string)($i['description']??''))!==''))) Response::error('Bitte mindestens eine Materialposition erfassen',422);
    $id=uuid(); $orderNo=trim((string)($b['order_no'] ?? '')); if($orderNo==='') $orderNo=next_material_order_no($db,$tid);
    $date=trim((string)($b['requested_delivery_date'] ?? '')); $date=$date!=='' && strtotime($date) ? date('Y-m-d',strtotime($date)) : null;
    $siteId=trim((string)($b['site_id'] ?? '')) ?: null;
    if($siteId){ $site=$db->one("SELECT id FROM construction_sites WHERE tenant_id=? AND id=? AND project_id=? AND deleted_at IS NULL",[$tid,$siteId,$p['id']]); if(!$site) $siteId=null; }
    $db->tx(function($db) use($tid,$id,$p,$siteId,$supplierId,$orderNo,$status,$date,$b,$ctx,$items){
      $db->exec("INSERT INTO material_orders (id,tenant_id,project_id,site_id,supplier_id,order_no,status,requested_delivery_date,delivery_address,contact_name,notes,created_by,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,NOW(),NOW())",[$id,$tid,$p['id'],$siteId,$supplierId,$orderNo,$status,$date,trim((string)($b['delivery_address']??''))?:null,trim((string)($b['contact_name']??''))?:null,trim((string)($b['notes']??''))?:null,$ctx['user']['id']??null]);
      save_material_order_items($db,$tid,$id,$items);
    });
    try { log_project_event($db,$ctx['user'],$p['id'],'Materialbestellung angelegt: '.$orderNo,'material'); } catch(Throwable $e) {}
    Response::json(material_order_with_items($db,$tid,$id),201);
  });

  $r->add('PUT','/material-orders/{id}', function($req,$ctx,$p){
    require_permission($ctx['user'],'projects.write'); ensure_material_orders_table($ctx['db']);
    $tid=$ctx['user']['tenant_id']; $db=$ctx['db']; $b=$req->body;
    $order=$db->one("SELECT * FROM material_orders WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$p['id']]);
    if(!$order) Response::error('Materialbestellung nicht gefunden',404);
    $supplierId=trim((string)($b['supplier_id'] ?? $order['supplier_id'] ?? '')) ?: null;
    if($supplierId){ $supplier=$db->one("SELECT id FROM suppliers WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$supplierId]); if(!$supplier) Response::error('Lieferant nicht gefunden',404); }
    $status=trim((string)($b['status'] ?? $order['status'] ?? 'to_order')); if(!material_order_status_valid($status)) $status='to_order';
    $date=array_key_exists('requested_delivery_date',$b) ? trim((string)$b['requested_delivery_date']) : (string)($order['requested_delivery_date'] ?? ''); $date=$date!=='' && strtotime($date) ? date('Y-m-d',strtotime($date)) : null;
    $siteId=trim((string)($b['site_id'] ?? $order['site_id'] ?? '')) ?: null;
    if($siteId){ $site=$db->one("SELECT id FROM construction_sites WHERE tenant_id=? AND id=? AND project_id=? AND deleted_at IS NULL",[$tid,$siteId,$order['project_id']]); if(!$site) $siteId=null; }
    $db->tx(function($db) use($tid,$p,$siteId,$supplierId,$status,$date,$b){
      $db->exec("UPDATE material_orders SET site_id=?, supplier_id=?, status=?, requested_delivery_date=?, delivery_address=?, contact_name=?, notes=?, updated_at=NOW() WHERE tenant_id=? AND id=?",[$siteId,$supplierId,$status,$date,trim((string)($b['delivery_address']??''))?:null,trim((string)($b['contact_name']??''))?:null,trim((string)($b['notes']??''))?:null,$tid,$p['id']]);
      if(array_key_exists('items',$b)) save_material_order_items($db,$tid,$p['id'],$b['items']);
    });
    try { log_project_event($db,$ctx['user'],$order['project_id'],'Materialbestellung aktualisiert: '.($order['order_no']??''),'material'); } catch(Throwable $e) {}
    Response::json(material_order_with_items($db,$tid,$p['id']));
  });

  $r->add('POST','/material-orders/{id}/status', function($req,$ctx,$p){
    require_permission($ctx['user'],'projects.write'); ensure_material_orders_table($ctx['db']);
    $tid=$ctx['user']['tenant_id']; $db=$ctx['db']; $status=trim((string)($req->body['status'] ?? ''));
    if(!material_order_status_valid($status)) Response::error('Ungültiger Status',422);
    $order=$db->one("SELECT * FROM material_orders WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$p['id']]);
    if(!$order) Response::error('Materialbestellung nicht gefunden',404);
    $db->exec("UPDATE material_orders SET status=?, updated_at=NOW() WHERE tenant_id=? AND id=?",[$status,$tid,$p['id']]);
    try { log_project_event($db,$ctx['user'],$order['project_id'],'Materialbestellung '.$order['order_no'].' auf '.status_label_de($status).' gesetzt','material'); } catch(Throwable $e) {}
    Response::json(material_order_with_items($db,$tid,$p['id']));
  });

  $r->add('DELETE','/material-orders/{id}', function($req,$ctx,$p){
    require_permission($ctx['user'],'projects.write'); ensure_material_orders_table($ctx['db']);
    $tid=$ctx['user']['tenant_id']; $db=$ctx['db'];
    $order=$db->one("SELECT * FROM material_orders WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$p['id']]);
    if(!$order) Response::error('Materialbestellung nicht gefunden',404);
    $db->tx(function($db) use($tid,$p){
      soft_delete_where($db,'material_order_items',"tenant_id=? AND order_id=?",[$tid,$p['id']]);
      soft_delete_where($db,'material_orders',"tenant_id=? AND id=?",[$tid,$p['id']]);
    });
    try { log_project_event($db,$ctx['user'],$order['project_id'],'Materialbestellung gelöscht: '.($order['order_no']??''),'material'); } catch(Throwable $e) {}
    Response::json(['ok'=>true]);
  });

  $r->add('GET','/projects/{id}/file', function($req,$ctx,$p){
    require_permission($ctx['user'],'projects.read');
    $db=$ctx['db']; $tid=$ctx['user']['tenant_id']; $projectId=$p['id'];
    ensure_quality_checks_table($db);
    ensure_file_project_columns($db);
    $project=$db->one("SELECT * FROM projects WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$projectId]);
    if(!$project) Response::error('Auftrag nicht gefunden',404);
    // Each related dataset is loaded defensively. A single missing table or column
    // (e.g. on an older STRATO database) must never break the whole Bauakte.
    $sites=safe_query($db,"SELECT * FROM construction_sites WHERE tenant_id=? AND project_id=? AND deleted_at IS NULL ORDER BY site_no,name",[$tid,$projectId]);
    $siteIds=array_map(fn($s)=>$s['id'],$sites);
    $quality=[]; $shares=[]; $tasks=[]; $reports=[]; $inspections=[];
    // Documents & photos are scoped strictly to THIS Bauakte via project_id,
    // with a site-based fallback for legacy rows that predate the project_id column.
    if(count($siteIds)){
      $in=implode(',',array_fill(0,count($siteIds),'?'));
      $documents=safe_query($db,"SELECT * FROM documents WHERE tenant_id=? AND deleted_at IS NULL AND (project_id=? OR ((project_id IS NULL OR project_id='') AND site_id IN ($in))) ORDER BY created_at DESC LIMIT 300",array_merge([$tid,$projectId],$siteIds));
      $photos=safe_query($db,"SELECT * FROM photos WHERE tenant_id=? AND deleted_at IS NULL AND (project_id=? OR ((project_id IS NULL OR project_id='') AND site_id IN ($in))) ORDER BY taken_at DESC, created_at DESC LIMIT 300",array_merge([$tid,$projectId],$siteIds));
      $params=array_merge([$tid],$siteIds);
      $quality=safe_query($db,"SELECT * FROM quality_checks WHERE tenant_id=? AND site_id IN ($in) AND deleted_at IS NULL ORDER BY created_at DESC LIMIT 150",$params);
      $tasks=safe_query($db,"SELECT * FROM tasks WHERE tenant_id=? AND site_id IN ($in) AND deleted_at IS NULL ORDER BY due_date IS NULL,due_date ASC,created_at DESC LIMIT 200",$params);
      $reports=safe_query($db,"SELECT * FROM reports WHERE tenant_id=? AND site_id IN ($in) AND deleted_at IS NULL ORDER BY created_at DESC LIMIT 200",$params);
      $inspections=safe_query($db,"SELECT * FROM inspection_results WHERE tenant_id=? AND site_id IN ($in) AND deleted_at IS NULL ORDER BY created_at DESC LIMIT 100",$params);
    } else {
      $documents=safe_query($db,"SELECT * FROM documents WHERE tenant_id=? AND project_id=? AND deleted_at IS NULL ORDER BY created_at DESC LIMIT 300",[$tid,$projectId]);
      $photos=safe_query($db,"SELECT * FROM photos WHERE tenant_id=? AND project_id=? AND deleted_at IS NULL ORDER BY taken_at DESC, created_at DESC LIMIT 300",[$tid,$projectId]);
      $reports=safe_query($db,"SELECT * FROM reports WHERE tenant_id=? AND content LIKE ? AND deleted_at IS NULL ORDER BY created_at DESC LIMIT 200",[$tid,'%'.$projectId.'%']);
      $inspections=safe_query($db,"SELECT * FROM inspection_results WHERE tenant_id=? AND result_json LIKE ? AND deleted_at IS NULL ORDER BY created_at DESC LIMIT 100",[$tid,'%'.$projectId.'%']);
    }
    $presence=[];
    $docIds=array_map(fn($d)=>$d['id'],$documents);
    if(count($docIds)){
      $inDocs=implode(',',array_fill(0,count($docIds),'?'));
      $shares=safe_query($db,"SELECT * FROM document_shares WHERE tenant_id=? AND document_id IN ($inDocs) AND deleted_at IS NULL ORDER BY created_at DESC",array_merge([$tid],$docIds));
    }
    Response::json(['project'=>$project,'sites'=>$sites,'documents'=>$documents,'photos'=>$photos,'quality'=>$quality,'shares'=>$shares,'tasks'=>$tasks,'reports'=>$reports,'inspections'=>$inspections,'presence'=>$presence]);
  });

  $r->add('GET','/projects/{id}/history', function($req,$ctx,$p){
    require_permission($ctx['user'],'projects.read');
    // Audit wurde entfernt – Verlauf läuft jetzt über project_notes (Notizen-Tab).
    Response::json(['items'=>[]]);
  });

  $r->add('GET','/projects/{id}/presence', function($req,$ctx,$p){
    require_permission($ctx['user'],'projects.read');
    Response::json(['items'=>[]]);
  });

  $r->add('POST','/projects/{id}/presence', function($req,$ctx,$p){
    require_permission($ctx['user'],'projects.read');
    Response::json(['ok'=>true]);
  });

  // Assign a Bauakte to an employee (responsible person) WITHOUT creating a task.
  $r->add('PUT','/projects/{id}/assignee', function($req,$ctx,$p){
    require_permission($ctx['user'],'projects.write');
    $db=$ctx['db']; $tid=$ctx['user']['tenant_id'];
    foreach([['assigned_user_id','VARCHAR(255)'],['assigned_user_name','VARCHAR(255)']] as $col){
      $names=table_columns($db,'projects');
      if($names && !in_array($col[0],$names,true)){ try { $db->exec("ALTER TABLE projects ADD COLUMN `{$col[0]}` {$col[1]} NULL"); } catch(Throwable $e) {} }
    }
    $project=$db->one("SELECT * FROM projects WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$p['id']]);
    if(!$project) Response::error('Bauakte nicht gefunden',404);
    $userId=$req->body['assigned_user_id'] ?? null; $userName=null; $userEmail=null;
    if($userId){ $u=$db->one("SELECT id,name,email FROM users WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$userId]); if(!$u) Response::error('Mitarbeiter nicht gefunden',404); $userName=$u['name']; $userEmail=$u['email'] ?? null; }
    $db->exec("UPDATE projects SET assigned_user_id=?, assigned_user_name=?, updated_at=NOW() WHERE tenant_id=? AND id=?",[$userId,$userName,$tid,$p['id']]);
    $ctx['audit']->log($tid,$ctx['user']['id'],'projects',$p['id'],'update',['assigned_user_id'=>$project['assigned_user_id']??null],['assigned_user_id'=>$userId]);
    log_project_event($db,$ctx['user'],$p['id'], $userName ? ('Bauakte zugewiesen an '.$userName) : 'Zuweisung entfernt', 'assignment');
    // Benachrichtigung per E-Mail (nur bei tatsaechlich neuer Zuweisung)
    if($userId && $userEmail && (($project['assigned_user_id']??null) !== $userId)){
      $pNo=trim((string)($project['project_no'] ?? $project['number'] ?? ''));
      $pName=trim((string)($project['name'] ?? $project['title'] ?? ''));
      $bezeichnung=trim(($pNo!==''?$pNo.' ':'').$pName) ?: 'Bauakte';
      $by=htmlspecialchars((string)($ctx['user']['name'] ?? ''),ENT_QUOTES,'UTF-8');
      $html=email_layout('Ihnen wurde eine Bauakte zugewiesen',
        '<p>Hallo '.htmlspecialchars((string)$userName,ENT_QUOTES,'UTF-8').',</p>'
        .'<p>Ihnen wurde die folgende Bauakte zugewiesen:</p>'
        .'<p style="font-size:16px;font-weight:bold">'.htmlspecialchars($bezeichnung,ENT_QUOTES,'UTF-8').'</p>'
        .'<p>Bitte melden Sie sich in EstrichWeb an, um die Details einzusehen.</p>',
        'Zugewiesen von '.$by.' · Automatische Nachricht von EstrichWeb');
      notify_by_email($db,$tid,$userEmail,(string)$userName,'EstrichWeb – Neue Bauakte zugewiesen: '.$bezeichnung,$html);
    }
    Response::json(['ok'=>true,'assigned_user_id'=>$userId,'assigned_user_name'=>$userName]);
  });


  $r->add('POST','/projects/{id}/cancel', function($req,$ctx,$p){
    $tid=$ctx['user']['tenant_id']; $db=$ctx['db'];
    require_project_destructive_permission($db,$ctx['user']);
    require_current_password($db,$ctx['user'],(string)($req->body['password'] ?? ''));
    $reason=trim((string)($req->body['reason'] ?? ''));
    $project=$db->one("SELECT * FROM projects WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$p['id']]);
    if(!$project) Response::error('Bauakte nicht gefunden',404);
    // Stornogrund-Spalte idempotent sicherstellen (vor der Transaktion, da ALTER implizit committed).
    add_column_if_missing($db,'projects','cancel_reason','VARCHAR(255) NULL AFTER `status`');
    if(($project['status'] ?? '') === 'cancelled') Response::json(['ok'=>true,'id'=>$p['id'],'status'=>'cancelled','cancel_reason'=>$reason ?: ($project['cancel_reason'] ?? null)]);
    $db->tx(function($db) use($tid,$p,$project,$ctx,$reason){
      $db->exec("UPDATE projects SET status='cancelled', cancel_reason=?, updated_at=NOW() WHERE tenant_id=? AND id=?",[$reason ?: null,$tid,$p['id']]);
      // Damit stornierte Bauvorhaben wirklich aus aktiver Planung/Listen verschwinden,
      // werden die verknüpften Baustellen ebenfalls auf storniert gesetzt.
      try { $db->exec("UPDATE construction_sites SET status='cancelled', updated_at=NOW() WHERE tenant_id=? AND project_id=? AND deleted_at IS NULL",[$tid,$p['id']]); } catch(Throwable $e) { error_log('cancel sites: '.$e->getMessage()); }
      $ctx['audit']->log($tid,$ctx['user']['id'],'projects',$p['id'],'cancel',['status'=>$project['status'] ?? null],['status'=>'cancelled','reason'=>$reason]);
      log_project_event($db,$ctx['user'],$p['id'],'Bauakte storniert'.($reason ? ' – Grund: '.$reason : ''),'status');
    });
    Response::json(['ok'=>true,'id'=>$p['id'],'status'=>'cancelled','cancel_reason'=>$reason ?: null]);
  });

  $r->add('POST','/projects/{id}/delete', function($req,$ctx,$p){
    $tid=$ctx['user']['tenant_id']; $db=$ctx['db'];
    require_project_destructive_permission($db,$ctx['user']);
    require_current_password($db,$ctx['user'],(string)($req->body['password'] ?? ''));
    $project=$db->one("SELECT * FROM projects WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$p['id']]);
    if(!$project) Response::error('Bauakte nicht gefunden',404);
    // DDL-/Schema-Sicherungen unbedingt vor der Transaktion ausführen: MySQL
    // committed CREATE/ALTER TABLE implizit; innerhalb einer PDO-Transaktion
    // kann das sonst als 500er beim anschließenden commit() enden.
    ensure_file_project_columns($db);
    ensure_project_notes_table($db);
    try {
      $summary=$db->tx(function($db) use($ctx,$project,$p,$tid){
        $summary=delete_project_linked_data($db,$ctx['config'],$ctx['user'],$p['id']);
        purge_where($db,'projects',"tenant_id=? AND id=?",[$tid,$p['id']]);
        $ctx['audit']->log($tid,$ctx['user']['id'],'projects',$p['id'],'delete',$project,['linked'=>$summary]);
        return $summary;
      });
    } catch(Throwable $e) {
      error_log('project delete failed '.$p['id'].': '.$e->getMessage());
      Response::error('Bauakte konnte nicht gelöscht werden: '.$e->getMessage(),500);
    }
    Response::json(['ok'=>true,'id'=>$p['id'],'deleted'=>true,'linked'=>$summary]);
  });


  $r->add('GET','/planning/assignments', function($req,$ctx){
    require_permission($ctx['user'],'planning.read');
    $db=$ctx['db']; ensure_planning_columns($db);
    $tid=$ctx['user']['tenant_id']; $from=$req->query['from'] ?? date('Y-m-d',strtotime('-7 days')); $to=$req->query['to'] ?? date('Y-m-d',strtotime('+30 days'));
    $cols=table_columns($db,'planning_assignments');
    $projectExpr=in_array('project_id',$cols,true) ? 'pa.project_id' : 'cs.project_id';
    $vehiclePlate=vehicle_plate_expr($db);
    $rows=$db->all("SELECT pa.*, cs.name site_name, p.name project_name, cr.name crew_name, v.name vehicle_name, $vehiclePlate vehicle_plate, m.name machine_name FROM planning_assignments pa LEFT JOIN construction_sites cs ON cs.id=pa.site_id AND cs.tenant_id=pa.tenant_id LEFT JOIN projects p ON p.id=COALESCE($projectExpr, cs.project_id) AND p.tenant_id=pa.tenant_id LEFT JOIN crews cr ON cr.id=pa.crew_id AND cr.tenant_id=pa.tenant_id LEFT JOIN vehicles v ON v.id=pa.vehicle_id AND v.tenant_id=pa.tenant_id LEFT JOIN machines m ON m.id=pa.machine_id AND m.tenant_id=pa.tenant_id WHERE pa.tenant_id=? AND pa.deleted_at IS NULL AND pa.start_at < ? AND pa.end_at > ? ORDER BY pa.start_at",[$tid,$to.' 23:59:59',$from.' 00:00:00']);
    Response::json(['items'=>$rows]);
  });

  $r->add('POST','/planning/assignments/check-conflicts', function($req,$ctx){
    require_permission($ctx['user'],'planning.write'); $tid=$ctx['user']['tenant_id']; assert_required($req->body,['start_at','end_at']); $conflicts=[];
    foreach(['crew_id'=>'Kolonne','vehicle_id'=>'Fahrzeug','machine_id'=>'Maschine'] as $field=>$label){ if(!empty($req->body[$field])){
      $found=$ctx['db']->all("SELECT id,title,start_at,end_at FROM planning_assignments WHERE tenant_id=? AND deleted_at IS NULL AND `$field`=? AND NOT (end_at<=? OR start_at>=?)",[$tid,$req->body[$field],$req->body['start_at'],$req->body['end_at']]);
      foreach($found as $f) $conflicts[]=['type'=>$field,'label'=>$label,'assignment'=>$f];
    }}
    Response::json(['ok'=>count($conflicts)===0,'conflicts'=>$conflicts]);
  });

  // Streams a stored document inline (PDF, image, video, Office files).
  // Dokument fuer das Kundenportal per Klick freigeben / sperren
  $r->add('PUT','/documents/{id}/portal-visibility', function($req,$ctx,$p){
    require_portal_enabled($ctx['config']);
    require_permission($ctx['user'],'documents.write');
    $db=$ctx['db']; $tid=$ctx['user']['tenant_id'];
    add_column_if_missing($db,'documents','customer_visible','TINYINT(1) NOT NULL DEFAULT 0');
    $doc=$db->one("SELECT id FROM documents WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$p['id']]);
    if(!$doc) Response::error('Dokument nicht gefunden',404);
    $visible=truthy($req->body['customer_visible'] ?? false)?1:0;
    $db->exec("UPDATE documents SET customer_visible=?, updated_at=NOW() WHERE tenant_id=? AND id=?",[$visible,$tid,$p['id']]);
    Response::json(['ok'=>true,'customer_visible'=>$visible]);
  });

  // Auth works via Bearer header OR ?access_token= query param, so it can be
  // used directly in <img>/<iframe>/<video> src attributes in the Bauakte.
  $r->add('GET','/documents/{id}/view', function($req,$ctx,$p){
    require_permission($ctx['user'],'projects.read');
    $tid=$ctx['user']['tenant_id'];
    $doc=$ctx['db']->one("SELECT * FROM documents WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$p['id']]);
    if(!$doc) Response::error('Dokument nicht gefunden',404);
    $name=(string)($doc['original_name'] ?: $doc['name'] ?: 'dokument');
    $mime=reliable_mime((string)($doc['mime_type'] ?? ''),(string)$doc['storage_path']);
    $disposition=truthy($req->query['download'] ?? false) ? 'attachment' : 'inline';
    stream_private_file((string)$doc['storage_path'],$mime,$name,$disposition);
  });

  // Streams a photo. variant = main (compressed) | thumb | original.
  $r->add('GET','/photos/{id}/view', function($req,$ctx,$p){
    require_permission($ctx['user'],'projects.read');
    $tid=$ctx['user']['tenant_id'];
    $photo=$ctx['db']->one("SELECT * FROM photos WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$p['id']]);
    if(!$photo) Response::error('Foto nicht gefunden',404);
    $variant=(string)($req->query['variant'] ?? 'main');
    if($variant==='thumb') $path=$photo['thumbnail_path'] ?: $photo['compressed_path'] ?: $photo['original_path'];
    elseif($variant==='original') $path=$photo['original_path'] ?: $photo['compressed_path'] ?: $photo['thumbnail_path'];
    else $path=$photo['compressed_path'] ?: $photo['original_path'] ?: $photo['thumbnail_path'];
    stream_private_file((string)$path,mime_from_path((string)$path));
  });

  $r->add('POST','/documents/upload', function($req,$ctx){
    require_permission($ctx['user'],'documents.write'); if(empty($_FILES['file'])) Response::error('Datei fehlt',422); $file=$_FILES['file']; $tid=$ctx['user']['tenant_id'];
    if($file['size'] > (int)$ctx['config']['app']['upload_max_bytes']) Response::error('Datei zu groß',413);
    $allowed=['pdf','doc','docx','xls','xlsx','jpg','jpeg','png','webp','heic','mp4','webm','mov','m4v']; $ext=strtolower(pathinfo($file['name'],PATHINFO_EXTENSION)); if(!in_array($ext,$allowed,true)) Response::error('Dateityp nicht erlaubt',422);
    ensure_file_project_columns($ctx['db']);
    $siteId=$_POST['site_id'] ?? null;
    $projectId=$_POST['project_id'] ?? null;
    if((!$siteId || $siteId==='__auto__') && $projectId){
      $existing=$ctx['db']->one("SELECT id FROM construction_sites WHERE tenant_id=? AND project_id=? AND deleted_at IS NULL ORDER BY created_at ASC LIMIT 1",[$tid,$projectId]);
      if($existing){ $siteId=$existing['id']; }
      else {
        $project=$ctx['db']->one("SELECT * FROM projects WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$projectId]);
        if($project){
          $siteId=uuid();
          $ctx['db']->exec("INSERT INTO construction_sites (id,tenant_id,project_id,customer_id,name,site_no,status,address,zip,city,planned_start,planned_end,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,NOW(),NOW())",[
            $siteId,$tid,$projectId,$project['customer_id']??null,$project['name']??'Bauakte',$project['project_no']??null,$project['status']??'planned',$project['address']??null,$project['zip']??null,$project['city']??null,$project['start_date']??null,$project['end_date']??null
          ]);
        }
      }
    }
    $id=uuid(); $dir=rtrim($ctx['config']['app']['storage_path'],'/').'/documents/'.$tid; if(!is_dir($dir)) mkdir($dir,0775,true); $path=$dir.'/'.$id.'.'.$ext; if(!move_uploaded_file($file['tmp_name'],$path)) Response::error('Upload fehlgeschlagen',500);
    // Bilder gehoeren in die Foto-Galerie (photos), nicht in die Dokumentenliste.
    // So erscheinen im Foto-Tab hochgeladene Bilder dort und nicht unter "Dokumente".
    $category=$_POST['category']??'general';
    $isImage=in_array($ext,['jpg','jpeg','png','webp','heic'],true) || str_starts_with((string)($file['type']??''),'image/') || in_array($category,['foto','photo','bild'],true);
    if($isImage){
      // store_photo erwartet eine hochgeladene Datei; da move_uploaded_file bereits lief,
      // reichen wir den finalen Pfad als tmp_name weiter (store_photo kopiert/skaliert daraus).
      $photoFile=['name'=>$file['name'],'type'=>$file['type']??null,'size'=>$file['size']??null,'tmp_name'=>$path,'_already_moved'=>true];
      // Speicherschonend: standardmäßig KEIN Voll-Original behalten (nur komprimiert + Thumbnail).
      // Wer das Original braucht, sendet store_original=1 mit.
      $keepOriginal=truthy($_POST['store_original']??false);
      $meta=['site_id'=>$siteId,'project_id'=>$projectId,'room_id'=>$_POST['room_id']??null,'caption'=>$_POST['name']??null,'category'=>($category==='general'?'bauakte':$category),'store_original'=>$keepOriginal];
      $photo=store_photo_from_path($ctx['db'],$ctx['config'],$ctx['user'],$photoFile,$meta);
      @unlink($path);
      $ctx['audit']->log($tid,$ctx['user']['id'],'photos',$photo['id']??$id,'upload',null,$photo);
      Response::json($photo,201);
    }
    $ctx['db']->exec("INSERT INTO documents (id,tenant_id,site_id,project_id,room_id,name,original_name,mime_type,size_bytes,storage_path,category,created_by,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,NOW(),NOW())",[$id,$tid,$siteId,$projectId,$_POST['room_id']??null,$_POST['name']??$file['name'],$file['name'],$file['type']??null,$file['size'],$path,$category,$ctx['user']['id']]);
    $row=$ctx['db']->one("SELECT * FROM documents WHERE id=?",[$id]); $ctx['audit']->log($tid,$ctx['user']['id'],'documents',$id,'upload',null,$row); Response::json($row,201);
  });

  // ---- Notizen / Verlauf (Ticket-Stil) je Bauakte ----
  $r->add('GET','/projects/{id}/notes', function($req,$ctx,$p){
    require_permission($ctx['user'],'projects.read'); ensure_project_notes_table($ctx['db']);
    $tid=$ctx['user']['tenant_id'];
    $rows=safe_query($ctx['db'],"SELECT * FROM project_notes WHERE tenant_id=? AND project_id=? AND deleted_at IS NULL ORDER BY created_at ASC LIMIT 500",[$tid,$p['id']]);
    // Für Notizen mit Dokument prüfen, ob das Dokument noch existiert (sonst Hinweis im UI).
    foreach($rows as &$row){
      if(!empty($row['document_id'])){
        $doc=$ctx['db']->one("SELECT id,name,deleted_at FROM documents WHERE tenant_id=? AND id=?",[$tid,$row['document_id']]);
        $row['document_available']=($doc && empty($doc['deleted_at'])) ? 1 : 0;
        $row['document_name']=$doc['name'] ?? null;
      } else {
        $row['document_available']=0;
      }
    }
    unset($row);
    Response::json(['items'=>$rows]);
  });
  // Anmerkung anheften, loesen oder umfaerben.
  $r->add('PUT','/projects/{id}/notes/{noteId}', function($req,$ctx,$p){
    // Notizen darf jeder erfassen, der die Bauakte sehen darf. Der Austausch
    // zwischen Baustelle und Buero soll an keiner Rechtevergabe scheitern;
    // geaendert wird dabei nichts an der Akte selbst.
    require_permission($ctx['user'],'projects.read'); ensure_project_notes_table($ctx['db']);
    $tid=$ctx['user']['tenant_id'];
    $row=$ctx['db']->one("SELECT * FROM project_notes WHERE id=? AND tenant_id=? AND project_id=? AND deleted_at IS NULL",[$p['noteId'],$tid,$p['id']]);
    if(!$row) Response::error('Notiz nicht gefunden',404);
    $meta = json_decode((string)($row['meta_json'] ?? ''), true) ?: [];
    if(array_key_exists('pinned',$req->body)) $meta['pinned'] = (int)!empty($req->body['pinned']);
    if(!empty($req->body['color'])){
      $c=(string)$req->body['color'];
      $meta['color'] = in_array($c,['yellow','orange','red','green','blue'],true) ? $c : 'yellow';
    }
    if(array_key_exists('due_at',$req->body)) $meta['due_at'] = $req->body['due_at'] ?: null;
    if(array_key_exists('body',$req->body)){
      $ctx['db']->exec("UPDATE project_notes SET body=?, updated_at=NOW() WHERE id=? AND tenant_id=?",[trim((string)$req->body['body']),$p['noteId'],$tid]);
    }
    $ctx['db']->exec("UPDATE project_notes SET meta_json=?, updated_at=NOW() WHERE id=? AND tenant_id=?",[json_encode($meta,JSON_UNESCAPED_UNICODE),$p['noteId'],$tid]);
    Response::json($ctx['db']->one("SELECT * FROM project_notes WHERE id=?",[$p['noteId']]));
  });

  $r->add('POST','/projects/{id}/notes', function($req,$ctx,$p){
    // Notizen darf jeder erfassen, der die Bauakte sehen darf. Der Austausch
    // zwischen Baustelle und Buero soll an keiner Rechtevergabe scheitern;
    // geaendert wird dabei nichts an der Akte selbst.
    require_permission($ctx['user'],'projects.read'); ensure_project_notes_table($ctx['db']);
    $tid=$ctx['user']['tenant_id']; $projectId=$p['id'];
    $documentId=null;
    // Optional: Notiz mit Dokument (multipart). Dokument landet auch in der Dokumentenliste.
    if(!empty($_FILES['file'])){
      $file=$_FILES['file'];
      $allowed=['pdf','doc','docx','xls','xlsx','jpg','jpeg','png','webp','heic']; $ext=strtolower(pathinfo($file['name'],PATHINFO_EXTENSION));
      if(in_array($ext,$allowed,true) && $file['size'] <= (int)$ctx['config']['app']['upload_max_bytes']){
        ensure_file_project_columns($ctx['db']);
        $did=uuid(); $dir=rtrim($ctx['config']['app']['storage_path'],'/').'/documents/'.$tid; if(!is_dir($dir)) mkdir($dir,0775,true);
        $path=$dir.'/'.$did.'.'.$ext;
        if(move_uploaded_file($file['tmp_name'],$path)){
          $ctx['db']->exec("INSERT INTO documents (id,tenant_id,project_id,name,original_name,mime_type,size_bytes,storage_path,category,created_by,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?,NOW(),NOW())",[$did,$tid,$projectId,$_POST['name']??$file['name'],$file['name'],$file['type']??null,$file['size'],$path,'notiz',$ctx['user']['id']]);
          $documentId=$did;
        }
      }
    }
    $body=trim((string)($req->body['body'] ?? $_POST['body'] ?? ''));
    if($body==='' && !$documentId) Response::error('Leere Notiz',422);
    // Angeheftete Anmerkungen ("Post-it"): Farbe und optionale Faelligkeit
    // werden in meta_json abgelegt. Dadurch ist keine Schemaaenderung noetig.
    $pinned = (int)!empty($req->body['pinned'] ?? $_POST['pinned'] ?? 0);
    $color  = trim((string)($req->body['color'] ?? $_POST['color'] ?? 'yellow'));
    $dueAt  = trim((string)($req->body['due_at'] ?? $_POST['due_at'] ?? ''));
    $meta = null;
    if($pinned || $dueAt !== '' || ($color !== '' && $color !== 'yellow')){
      $meta = json_encode([
        'pinned' => $pinned ? 1 : 0,
        'color'  => in_array($color,['yellow','orange','red','green','blue'],true) ? $color : 'yellow',
        'due_at' => $dueAt !== '' ? $dueAt : null
      ], JSON_UNESCAPED_UNICODE);
    }
    $id=uuid();
    $ctx['db']->exec("INSERT INTO project_notes (id,tenant_id,project_id,entry_type,author_id,author_name,body,document_id,meta_json,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,NOW(),NOW())",[$id,$tid,$projectId,'note',$ctx['user']['id'],$ctx['user']['name']??null,$body,$documentId,$meta]);
    $row=$ctx['db']->one("SELECT * FROM project_notes WHERE id=?",[$id]);
    Response::json($row,201);
  });
  $r->add('DELETE','/projects/{id}/notes/{noteId}', function($req,$ctx,$p){
    require_permission($ctx['user'],'projects.write'); $tid=$ctx['user']['tenant_id'];
    $before=$ctx['db']->one("SELECT * FROM project_notes WHERE tenant_id=? AND id=? AND project_id=?",[$tid,$p['noteId'],$p['id']]); if(!$before) Response::error('Nicht gefunden',404);
    purge_where($ctx['db'],'project_notes',"tenant_id=? AND id=?",[$tid,$p['noteId']]);
    Response::json(['ok'=>true]);
  });

  // ---- Fahrzeug-Dokumente (Fahrzeugschein, TÜV, Versicherung, Wartung …) ----
  $r->add('GET','/vehicles/{id}/documents', function($req,$ctx,$p){
    require_permission($ctx['user'],'resources.read'); ensure_runtime_columns($ctx['db']);
    $rows=safe_query($ctx['db'],"SELECT * FROM documents WHERE tenant_id=? AND vehicle_id=? AND deleted_at IS NULL ORDER BY created_at DESC LIMIT 300",[$ctx['user']['tenant_id'],$p['id']]);
    Response::json(['items'=>$rows]);
  });
  $r->add('POST','/vehicles/{id}/documents', function($req,$ctx,$p){
    require_permission($ctx['user'],'resources.write'); ensure_runtime_columns($ctx['db']);
    if(empty($_FILES['file'])) Response::error('Datei fehlt',422); $file=$_FILES['file']; $tid=$ctx['user']['tenant_id'];
    if($file['size'] > (int)$ctx['config']['app']['upload_max_bytes']) Response::error('Datei zu groß',413);
    $allowed=['pdf','doc','docx','xls','xlsx','jpg','jpeg','png','webp','heic']; $ext=strtolower(pathinfo($file['name'],PATHINFO_EXTENSION)); if(!in_array($ext,$allowed,true)) Response::error('Dateityp nicht erlaubt',422);
    $id=uuid(); $dir=rtrim($ctx['config']['app']['storage_path'],'/').'/documents/'.$tid; if(!is_dir($dir)) mkdir($dir,0775,true); $path=$dir.'/'.$id.'.'.$ext; if(!move_uploaded_file($file['tmp_name'],$path)) Response::error('Upload fehlgeschlagen',500);
    $ctx['db']->exec("INSERT INTO documents (id,tenant_id,vehicle_id,name,original_name,mime_type,size_bytes,storage_path,category,created_by,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?,NOW(),NOW())",[$id,$tid,$p['id'],$_POST['name']??$file['name'],$file['name'],$file['type']??null,$file['size'],$path,$_POST['category']??'fahrzeug',$ctx['user']['id']]);
    $row=$ctx['db']->one("SELECT * FROM documents WHERE id=?",[$id]); $ctx['audit']->log($tid,$ctx['user']['id'],'documents',$id,'upload',null,$row); Response::json($row,201);
  });
  $r->add('DELETE','/vehicles/{id}/documents/{docId}', function($req,$ctx,$p){
    require_permission($ctx['user'],'resources.write'); $tid=$ctx['user']['tenant_id'];
    $before=$ctx['db']->one("SELECT * FROM documents WHERE tenant_id=? AND id=? AND vehicle_id=?",[$tid,$p['docId'],$p['id']]); if(!$before) Response::error('Nicht gefunden',404);
    // Dokument endgueltig entfernen - die Datei loescht purge_where mit.
    purge_where($ctx['db'],'document_shares',"tenant_id=? AND document_id=?",[$tid,$p['docId']]);
    purge_where($ctx['db'],'documents',"tenant_id=? AND id=?",[$tid,$p['docId']]);
    $ctx['audit']->log($tid,$ctx['user']['id'],'documents',$p['docId'],'delete',$before,null); Response::json(['ok'=>true]);
  });

  $r->add('POST','/daily-documentation', function($req,$ctx){
    require_permission($ctx['user'],'reports.write');
    require_permission($ctx['user'],'photos.write');
    $tid=$ctx['user']['tenant_id']; $uid=$ctx['user']['id'];
    $siteId=$_POST['site_id'] ?? null;
    $projectId=$_POST['project_id'] ?? null;
    if((!$siteId || $siteId==='__auto__') && $projectId){
      $existing=$ctx['db']->one("SELECT id FROM construction_sites WHERE tenant_id=? AND project_id=? AND deleted_at IS NULL ORDER BY created_at ASC LIMIT 1",[$tid,$projectId]);
      if($existing){ $siteId=$existing['id']; }
      else {
        $project=$ctx['db']->one("SELECT * FROM projects WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$projectId]);
        if($project){
          $siteId=uuid();
          $ctx['db']->exec("INSERT INTO construction_sites (id,tenant_id,project_id,customer_id,name,site_no,status,address,zip,city,planned_start,planned_end,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,NOW(),NOW())",[
            $siteId,$tid,$projectId,$project['customer_id']??null,$project['name']??'Bauakte',$project['project_no']??null,$project['status']??'planned',$project['address']??null,$project['zip']??null,$project['city']??null,$project['start_date']??null,$project['end_date']??null
          ]);
        }
      }
    }
    if(!$siteId) Response::error('Bauakte fehlt',422);
    // Ensure photos/videos are stored against the resolved project site,
    // not the raw posted "__auto__"/empty value. Otherwise media would be
    // attached to a bogus "unknown-site" and not appear in the correct Bauakte.
    $_POST['site_id']=$siteId;
    $title='Tagesdokumentation '.($_POST['report_date'] ?? date('Y-m-d'));
    $content=[
      'project_id'=>$_POST['project_id'] ?? null,
      'site_id'=>$siteId,
      'report_date'=>$_POST['report_date'] ?? date('Y-m-d'),
      'weather'=>$_POST['weather'] ?? null,
      'crew_text'=>$_POST['crew_text'] ?? null,
      'time_text'=>$_POST['time_text'] ?? null,
      'work_done'=>$_POST['work_done'] ?? null,
      'notes'=>$_POST['notes'] ?? null,
      'customer_visible'=>truthy($_POST['customer_visible'] ?? false),
      'storage_strategy'=>'client_compressed_plus_server_thumbnail_no_original_by_default'
    ];
    $reportId=uuid();
    $ctx['db']->exec("INSERT INTO reports (id,tenant_id,site_id,title,report_type,status,content,created_at,updated_at) VALUES (?,?,?,?,?,?,?,NOW(),NOW())",[$reportId,$tid,$siteId,$title,'daily','draft',json_encode($content,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES)]);
    $photos=[]; foreach(uploaded_files('photos') as $file) { $photos[] = store_photo($ctx['db'],$ctx['config'],$ctx['user'],$file,$_POST); }
    $videos=[]; foreach(uploaded_files('videos') as $file) { $videos[] = store_document_file($ctx['db'],$ctx['config'],$ctx['user'],$file, array_merge($_POST,['category'=>'daily_video'])); }
    $ctx['audit']->log($tid,$uid,'daily_documentation',$reportId,'create',null,['report_id'=>$reportId,'photo_count'=>count($photos),'video_count'=>count($videos)]);
    Response::json(['ok'=>true,'report_id'=>$reportId,'photos'=>$photos,'videos'=>$videos,'photo_count'=>count($photos),'video_count'=>count($videos)],201);
  }, ['reports.write','photos.write']);

  $r->add('POST','/inspections/initial', function($req,$ctx){
    require_permission($ctx['user'],'inspections.write');
    $body=$req->body;
    if(isset($body['payload']) && is_string($body['payload'])) { $decoded=json_decode($body['payload'],true); if(is_array($decoded)) $body=$decoded; }
    $context=$body['context'] ?? []; if(empty($context['site_id'])) Response::error('Baustelle fehlt',422);
    $tid=$ctx['user']['tenant_id']; $uid=$ctx['user']['id']; $id=uuid();
    ensure_file_project_columns($ctx['db']);
    add_column_if_missing($ctx['db'],'tasks','project_id','CHAR(32) NULL AFTER `tenant_id`');
    $projectId=$context['project_id'] ?? null;
    add_column_if_missing($ctx['db'],'construction_sites','site_email','VARCHAR(180) NULL AFTER `phone`');
    if(!$projectId){ $site=$ctx['db']->one("SELECT project_id FROM construction_sites WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$context['site_id']]); $projectId=$site['project_id'] ?? null; }
        $exactArea=$body['measurements']['exact_area_m2'] ?? $body['checklist']['exact_area_m2'] ?? null;
    $screedType=$body['checklist']['screed_type'] ?? null;
    $siteNotes=$body['checklist']['site_notes'] ?? null;
    $siteUpdates=[]; $siteParams=[];
    if($exactArea !== null && trim((string)$exactArea) !== '') { $siteUpdates[]='area_m2=?'; $siteParams[]=$exactArea; }
    if($screedType !== null && trim((string)$screedType) !== '') { $siteUpdates[]='screed_type=?'; $siteParams[]=$screedType; }
    if($siteNotes !== null && trim((string)$siteNotes) !== '') { $siteUpdates[]='hinweise=?'; $siteParams[]=$siteNotes; }
    if($siteUpdates){ $siteParams[]=$tid; $siteParams[]=$context['site_id']; $ctx['db']->exec('UPDATE construction_sites SET '.implode(',',$siteUpdates).', updated_at=NOW() WHERE tenant_id=? AND id=?', $siteParams); }
    // Gleiches Muster wie bei den Anrufen: erst mit Ersatz in eine Variable.
    $resultRaw = (string)($body['result'] ?? '');
    $outcome = in_array($resultRaw, ['passed','rejected'], true) ? $resultRaw : 'draft';
    $status=$outcome==='passed' ? 'completed' : ($outcome==='rejected' ? 'rejected' : 'draft');
    $reason=trim((string)($body['rejection_reason'] ?? ''));
    $notes=$reason !== '' ? $reason : ($body['checklist']['notes'] ?? null);
    $photoRows=[]; $videoRows=[];
    $_POST['site_id']=$context['site_id']; $_POST['project_id']=$projectId; $_POST['category']='initial_inspection'; $_POST['caption']='Erstbegehung';
    foreach(uploaded_files('photos') as $file) { $photoRows[] = store_photo($ctx['db'],$ctx['config'],$ctx['user'],$file,$_POST); }
    foreach(uploaded_files('videos') as $file) { $videoRows[] = store_document_file($ctx['db'],$ctx['config'],$ctx['user'],$file,array_merge($_POST,['category'=>'initial_inspection_video','name'=>'Erstbegehung Video'])); }
    $result=['context'=>$context,'checklist'=>$body['checklist'] ?? [],'measurements'=>$body['measurements'] ?? [],'photo_requirements'=>$body['photo_requirements'] ?? [],'media_groups'=>$body['media_groups'] ?? [],'export_options'=>$body['export_options'] ?? [],'photos'=>array_map(fn($p)=>$p['id'] ?? null,$photoRows),'videos'=>array_map(fn($v)=>$v['id'] ?? null,$videoRows),'result'=>$outcome,'rejection_reason'=>$reason,'type'=>'initial_site_inspection'];
    $ctx['db']->exec("INSERT INTO inspection_results (id,tenant_id,template_id,site_id,room_id,status,result_json,signed_by,signed_at,notes,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?,NOW(),NOW())",[$id,$tid,null,$context['site_id'],$context['room_id']??null,$status,json_encode($result,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES),$context['inspector_name']??null,$outcome==='passed'?date('Y-m-d H:i:s'):null,$notes]);
    // Nach erfolgreich durchgefuehrter (bestandener) Erstbegehung: Bauakte automatisch auf "in Arbeit".
    if($outcome==='passed' && $projectId){
      $ctx['db']->exec("UPDATE projects SET status='in_progress', updated_at=NOW() WHERE tenant_id=? AND id=? AND (status IS NULL OR LOWER(status) NOT IN ('completed','complete','done','closed','archived','cancelled','canceled','storniert','abgeschlossen'))",[$tid,$projectId]);
    }
    $taskId=null;
    if($outcome==='rejected' || trim((string)($body['checklist']['notes'] ?? '')) !== ''){
      $taskId=uuid();
      $taskTitle=$outcome==='rejected' ? 'Erstbegehung abgelehnt – Klärung erforderlich' : 'Offene Punkte aus Erstbegehung prüfen';
      $taskDesc=$outcome==='rejected' ? ('Erstbegehung wurde abgelehnt. Grund: '.($reason ?: 'kein Grund angegeben')) : 'Aus Erstbegehungs-Wizard erzeugt. Hinweise/Fotopflichten prüfen: '.trim((string)($body['checklist']['notes'] ?? ''));
      $taskPrio=$outcome==='rejected' ? 'high' : 'medium';
      $ctx['db']->exec("INSERT INTO tasks (id,tenant_id,project_id,site_id,room_id,title,description,priority,status,due_date,assigned_user_id,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?,?,NOW(),NOW())",[$taskId,$tid,$projectId,$context['site_id'],$context['room_id']??null,$taskTitle,$taskDesc,$taskPrio,'open',$context['inspection_date']??date('Y-m-d'),$uid]);
    }
    $ctx['audit']->log($tid,$uid,'initial_inspection',$id,'create',null,['inspection_result_id'=>$id,'task_id'=>$taskId,'result'=>$outcome,'project_id'=>$projectId,'photo_count'=>count($photoRows),'video_count'=>count($videoRows)]);
    Response::json(['ok'=>true,'inspection_result_id'=>$id,'task_id'=>$taskId,'result'=>$outcome,'status'=>$status,'photos'=>$photoRows,'videos'=>$videoRows,'photo_count'=>count($photoRows),'video_count'=>count($videoRows)],201);
  }, ['inspections.write']);

  $r->add('POST','/offline/sync', function($req,$ctx){
    require_permission($ctx['user'],'offline.write'); $ops=$req->body['operations'] ?? []; if(!is_array($ops)) Response::error('operations muss ein Array sein',422); $results=[];
    foreach($ops as $op){
      $id=uuid(); $ctx['db']->exec("INSERT INTO offline_sync_jobs (id,tenant_id,client_id,user_id,operation,entity_type,entity_id,payload_json,status,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,NOW(),NOW())",[$id,$ctx['user']['tenant_id'],$op['id']??null,$ctx['user']['id'],$op['operation']??'unknown',$op['moduleKey']??null,$op['entity_id']??null,json_encode($op,JSON_UNESCAPED_UNICODE|JSON_UNESCAPED_SLASHES),'queued']);
      $results[]=['id'=>$id,'status'=>'queued'];
    }
    Response::json(['ok'=>true,'results'=>$results]);
  });



  $r->add('GET','/admin/sso/microsoft', function($req,$ctx){
    require_permission($ctx['user'],'users.read');
    Response::json(sso_public_config($ctx['db'],$ctx['config'],$ctx['user']['tenant_id']));
  });

  $r->add('PUT','/admin/sso/microsoft', function($req,$ctx){
    require_permission($ctx['user'],'users.write');
    $db=$ctx['db']; $tid=$ctx['user']['tenant_id']; ensure_sso_tables($db);
    $current=$db->one("SELECT * FROM sso_settings WHERE tenant_id=? AND provider='microsoft' AND deleted_at IS NULL",[$tid]);
    $enabled=truthy($req->body['enabled'] ?? false)?1:0;
    $providerTenant=trim((string)($req->body['provider_tenant_id'] ?? $req->body['tenant_id'] ?? 'common')) ?: 'common';
    $clientId=trim((string)($req->body['client_id'] ?? ''));
    $clientSecret=trim((string)($req->body['client_secret'] ?? ''));
    $redirect=trim((string)($req->body['redirect_uri'] ?? '')) ?: app_base_url($ctx['config']).'/api/auth/microsoft/callback';
    $domains=trim((string)($req->body['allowed_domains'] ?? ''));
    $auto=truthy($req->body['auto_provision'] ?? false)?1:0; $roleId=$req->body['default_role_id'] ?? null;
    if(!$current){
      $db->exec("INSERT INTO sso_settings (id,tenant_id,provider,is_enabled,provider_tenant_id,client_id,client_secret_encrypted,redirect_uri,allowed_domains,auto_provision,default_role_id,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?,?,NOW(),NOW())",[uuid(),$tid,'microsoft',$enabled,$providerTenant,$clientId,$clientSecret!==''?secret_encrypt($clientSecret,$ctx['config']['app']['jwt_secret']):null,$redirect,$domains,$auto,$roleId ?: null]);
    } else {
      if($clientSecret!=='') $db->exec("UPDATE sso_settings SET is_enabled=?, provider_tenant_id=?, client_id=?, client_secret_encrypted=?, redirect_uri=?, allowed_domains=?, auto_provision=?, default_role_id=?, updated_at=NOW() WHERE id=?",[$enabled,$providerTenant,$clientId,secret_encrypt($clientSecret,$ctx['config']['app']['jwt_secret']),$redirect,$domains,$auto,$roleId ?: null,$current['id']]);
      else $db->exec("UPDATE sso_settings SET is_enabled=?, provider_tenant_id=?, client_id=?, redirect_uri=?, allowed_domains=?, auto_provision=?, default_role_id=?, updated_at=NOW() WHERE id=?",[$enabled,$providerTenant,$clientId,$redirect,$domains,$auto,$roleId ?: null,$current['id']]);
    }
    $ctx['audit']->log($tid,$ctx['user']['id'],'sso_settings',$current['id'] ?? null,'update',null,['provider'=>'microsoft','enabled'=>$enabled,'tenant'=>$providerTenant,'client_id'=>$clientId]);
    Response::json(sso_public_config($db,$ctx['config'],$tid));
  });

  $r->add('GET','/integrations/lexware/config', function($req,$ctx){
    require_permission($ctx['user'],'integrations.read');
    Response::json(lexware_get_config($ctx['db'],$ctx['config'],$ctx['user']['tenant_id']));
  });

  $r->add('PUT','/integrations/lexware/config', function($req,$ctx){
    require_permission($ctx['user'],'integrations.write');
    $db=$ctx['db']; $tid=$ctx['user']['tenant_id']; ensure_lexware_tables($db);
    $current=$db->one("SELECT * FROM lexware_connections WHERE tenant_id=? AND deleted_at IS NULL ORDER BY created_at DESC LIMIT 1",[$tid]);
    $apiBase=trim((string)($req->body['api_base_url'] ?? ($current['api_base_url'] ?? 'https://api.lexware.io')));
    if($apiBase==='') $apiBase='https://api.lexware.io';
    $isActive=truthy($req->body['is_active'] ?? false)?1:0; $autoCustomers=truthy($req->body['auto_sync_customers'] ?? true)?1:0; $autoProjects=truthy($req->body['auto_sync_projects'] ?? true)?1:0; $interval=max(1,(int)($req->body['auto_sync_interval_minutes'] ?? 4)); $syncContacts=truthy($req->body['sync_contacts'] ?? true)?1:0; $syncVouchers=truthy($req->body['sync_vouchers'] ?? true)?1:0; $syncInvoices=truthy($req->body['sync_invoices'] ?? true)?1:0; $syncQuotations=truthy($req->body['sync_quotations'] ?? true)?1:0;
    $apiKey=trim((string)($req->body['api_key'] ?? ''));
    if(!$current){
      $id=uuid(); $encrypted=$apiKey!==''?secret_encrypt($apiKey,$ctx['config']['app']['jwt_secret']):null;
      $db->exec("INSERT INTO lexware_connections (id,tenant_id,api_base_url,api_key_encrypted,is_active,auto_sync_customers,auto_sync_projects,auto_sync_interval_minutes,sync_contacts,sync_vouchers,sync_invoices,sync_quotations,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,NOW(),NOW())",[$id,$tid,$apiBase,$encrypted,$isActive,$autoCustomers,$autoProjects,$interval,$syncContacts,$syncVouchers,$syncInvoices,$syncQuotations]);
    } else {
      if($apiKey!=='') $db->exec("UPDATE lexware_connections SET api_base_url=?, api_key_encrypted=?, is_active=?, auto_sync_customers=?, auto_sync_projects=?, auto_sync_interval_minutes=?, sync_contacts=?, sync_vouchers=?, sync_invoices=?, sync_quotations=?, updated_at=NOW() WHERE id=?",[$apiBase,secret_encrypt($apiKey,$ctx['config']['app']['jwt_secret']),$isActive,$autoCustomers,$autoProjects,$interval,$syncContacts,$syncVouchers,$syncInvoices,$syncQuotations,$current['id']]);
      else $db->exec("UPDATE lexware_connections SET api_base_url=?, is_active=?, auto_sync_customers=?, auto_sync_projects=?, auto_sync_interval_minutes=?, sync_contacts=?, sync_vouchers=?, sync_invoices=?, sync_quotations=?, updated_at=NOW() WHERE id=?",[$apiBase,$isActive,$autoCustomers,$autoProjects,$interval,$syncContacts,$syncVouchers,$syncInvoices,$syncQuotations,$current['id']]);
    }
    $ctx['audit']->log($tid,$ctx['user']['id'],'lexware_connections',$current['id'] ?? null,'update',null,['api_base_url'=>$apiBase,'is_active'=>$isActive,'auto_sync_customers'=>$autoCustomers,'auto_sync_projects'=>$autoProjects]);
    Response::json(lexware_get_config($db,$ctx['config'],$tid));
  });

  $r->add('POST','/integrations/lexware/test', function($req,$ctx){
    require_permission($ctx['user'],'integrations.write'); $db=$ctx['db']; $tid=$ctx['user']['tenant_id']; ensure_lexware_tables($db);
    try { $result=lexware_api_request($db,$ctx['config'],$tid,'GET','/v1/contacts?page=0&size=1'); $db->exec("UPDATE lexware_connections SET last_test_at=NOW(), last_error=NULL, updated_at=NOW() WHERE tenant_id=? AND deleted_at IS NULL",[$tid]); lexware_log($db,$tid,'connection','success','Verbindung zur Lexware Office REST-API erfolgreich',null,$result); Response::json(['ok'=>true,'result'=>$result]); }
    catch(Throwable $e){ $db->exec("UPDATE lexware_connections SET last_test_at=NOW(), last_error=?, updated_at=NOW() WHERE tenant_id=? AND deleted_at IS NULL",[$e->getMessage(),$tid]); lexware_log($db,$tid,'connection','failed',$e->getMessage()); throw $e; }
  });


  $r->add('GET','/integrations/lexware/contacts', function($req,$ctx){
    require_permission($ctx['user'],'integrations.read');
    $page=max(0,(int)($req->query['page'] ?? 0)); $size=min(100,max(1,(int)($req->query['size'] ?? 25)));
    $query=trim((string)($req->query['q'] ?? ''));
    $path='/v1/contacts?page='.$page.'&size='.$size.($query!==''?'&email='.rawurlencode($query):'');
    Response::json(lexware_api_request($ctx['db'],$ctx['config'],$ctx['user']['tenant_id'],'GET',$path));
  });

  $r->add('POST','/integrations/lexware/sync-customers', function($req,$ctx){
    require_permission($ctx['user'],'integrations.write'); require_permission($ctx['user'],'customers.write');
    $db=$ctx['db']; $tid=$ctx['user']['tenant_id']; ensure_lexware_tables($db);
    @set_time_limit(0);
    $res=lexware_sync_contacts_full($db,$ctx['config'],$tid,300);
    Response::json(['ok'=>true,'linked'=>$res['linked'],'created'=>$res['created'],'updated'=>$res['updated'],'suppliers_created'=>$res['suppliers_created'],'suppliers_updated'=>$res['suppliers_updated']]);
  });

  // Chunk-Import fuer Fortschrittsanzeige: verarbeitet GENAU eine Seite (100 Kontakte) pro Aufruf.
  $r->add('POST','/integrations/lexware/sync-customers-chunk', function($req,$ctx){
    require_permission($ctx['user'],'integrations.write'); require_permission($ctx['user'],'customers.write');
    $db=$ctx['db']; $tid=$ctx['user']['tenant_id']; lexware_ensure_contact_tables($db);
    @set_time_limit(0);
    $page=max(0,(int)($req->body['page'] ?? 0));
    $size=100;
    $data=lexware_api_request_array($db,$ctx['config'],$tid,'GET','/v1/contacts?page='.$page.'&size='.$size);
    $items=lexware_items($data);
    $count=is_array($items)?count($items):0;
    $r=($count>0)?lexware_process_contacts($db,$tid,$items):['created'=>0,'updated'=>0,'linked'=>0,'suppliers_created'=>0,'suppliers_updated'=>0];
    $isLast=(isset($data['last']) && $data['last']===true) || $count < $size || $count===0;
    $totalPages=isset($data['totalPages'])?(int)$data['totalPages']:null;
    $totalElements=isset($data['totalElements'])?(int)$data['totalElements']:null;
    Response::json(['ok'=>true,'page'=>$page,'processed'=>$count,'created'=>$r['created'],'updated'=>$r['updated'],'suppliers_created'=>$r['suppliers_created'],'suppliers_updated'=>$r['suppliers_updated'],'has_more'=>!$isLast,'next_page'=>$page+1,'total_pages'=>$totalPages,'total_elements'=>$totalElements]);
  });

  $r->add('POST','/integrations/lexware/sync-projects', function($req,$ctx){
    require_permission($ctx['user'],'integrations.write'); require_permission($ctx['user'],'projects.write');
    $db=$ctx['db']; $tid=$ctx['user']['tenant_id']; ensure_lexware_tables($db);
    $projects=$db->all("SELECT * FROM projects WHERE tenant_id=? AND deleted_at IS NULL AND COALESCE(status,'') NOT IN ('completed','closed','archived') ORDER BY updated_at DESC LIMIT 100",[$tid]); $prepared=0;
    foreach($projects as $project){ $link=$db->one("SELECT id FROM lexware_entity_links WHERE tenant_id=? AND local_entity_type='project' AND local_entity_id=? AND lexware_entity_type='order'",[$tid,$project['id']]);
      if(!$link){ $db->exec("INSERT INTO lexware_entity_links (id,tenant_id,local_entity_type,local_entity_id,lexware_entity_type,lexware_id,lexware_number,sync_direction,status,last_synced_at,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,NULL,NOW(),NOW())",[uuid(),$tid,'project',$project['id'],'order',$project['lexware_order_id'] ?? null,$project['project_no'] ?? null,'outbound','prepared']); $prepared++; }
    }
    /* Erfolgreiche Auftrags-Syncs werden nicht einzeln geloggt. */
    Response::json(['ok'=>true,'prepared'=>$prepared,'note'=>'Aufträge wurden lokal verknüpft/vorbereitet. Belegerzeugung wird aus der Auftragsakte angestoßen, sobald Angebot/Rechnung fachlich freigegeben ist.']);
  });


  $r->add('GET','/integrations/lexware/quotations', function($req,$ctx){
    require_permission($ctx['user'],'integrations.read');
    ensure_lexware_tables($ctx['db']);
    $tid=$ctx['user']['tenant_id'];
    $items=$ctx['db']->all("SELECT * FROM lexware_synced_records WHERE tenant_id=? AND entity_type='quotations' ORDER BY document_date DESC, updated_at DESC LIMIT 250",[$tid]);
    Response::json(['items'=>$items,'total'=>count($items)]);
  });

  $r->add('POST','/integrations/lexware/link-quotation', function($req,$ctx){
    require_permission($ctx['user'],'integrations.write'); require_permission($ctx['user'],'projects.write');
    ensure_lexware_tables($ctx['db']);
    $tid=$ctx['user']['tenant_id'];
    $projectId=trim((string)($req->body['project_id'] ?? ''));
    $quotationId=trim((string)($req->body['quotation_id'] ?? ''));
    if($projectId==='' || $quotationId==='') Response::error('Auftrag und Angebot sind erforderlich',422);
    $project=$ctx['db']->one("SELECT id FROM projects WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$projectId]);
    if(!$project) Response::error('Auftrag nicht gefunden',404);
    $quotation=$ctx['db']->one("SELECT * FROM lexware_synced_records WHERE tenant_id=? AND id=? AND entity_type='quotations'",[$tid,$quotationId]);
    if(!$quotation) Response::error('Lexware-Angebot nicht gefunden',404);
    $ctx['db']->exec("UPDATE lexware_synced_records SET local_entity_type='project', local_entity_id=?, updated_at=NOW() WHERE id=? AND tenant_id=?",[$projectId,$quotationId,$tid]);
    $link=$ctx['db']->one("SELECT id FROM lexware_entity_links WHERE tenant_id=? AND local_entity_type='project' AND local_entity_id=? AND lexware_entity_type='quotation'",[$tid,$projectId]);
    if($link) $ctx['db']->exec("UPDATE lexware_entity_links SET lexware_id=?, lexware_number=?, status='linked', last_synced_at=NOW(), updated_at=NOW() WHERE id=?",[$quotation['lexware_id'],$quotation['lexware_number'],$link['id']]);
    else $ctx['db']->exec("INSERT INTO lexware_entity_links (id,tenant_id,local_entity_type,local_entity_id,lexware_entity_type,lexware_id,lexware_number,sync_direction,status,last_synced_at,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,NOW(),NOW(),NOW())",[uuid(),$tid,'project',$projectId,'quotation',$quotation['lexware_id'],$quotation['lexware_number'],'inbound','linked']);
    Response::json(['ok'=>true,'quotation'=>$quotation,'project_id'=>$projectId]);
  });

  $r->add('POST','/integrations/lexware/sync-documents', function($req,$ctx){
    require_permission($ctx['user'],'integrations.write');
    $summary=lexware_sync_documents($ctx['db'],$ctx['config'],$ctx['user']['tenant_id']);
    /* Erfolgreiche Beleg-Syncs werden nicht einzeln geloggt. */
    Response::json(['ok'=>true,'summary'=>$summary]);
  });

  $r->add('POST','/integrations/lexware/auto-sync', function($req,$ctx){
    require_permission($ctx['user'],'integrations.write');
    $force=truthy($req->body['force'] ?? false);
    Response::json(lexware_run_auto_sync($ctx['db'],$ctx['config'],$ctx['user']['tenant_id'],$force));
  });

  $r->add('GET','/integrations/lexware/synced-records', function($req,$ctx){
    require_permission($ctx['user'],'integrations.read'); ensure_lexware_tables($ctx['db']);
    $type=trim((string)($req->query['type'] ?? '')); $tid=$ctx['user']['tenant_id'];
    $params=[$tid]; $where="tenant_id=? AND deleted_at IS NULL"; if($type!==''){ $where.=" AND entity_type=?"; $params[]=$type; }
    $rows=$ctx['db']->all("SELECT * FROM lexware_synced_records WHERE $where ORDER BY COALESCE(last_synced_at,updated_at,created_at) DESC LIMIT 250",$params);
    Response::json(['items'=>$rows]);
  });

  $r->add('POST','/integrations/lexware/link', function($req,$ctx){
    require_permission($ctx['user'],'integrations.write'); $db=$ctx['db']; $tid=$ctx['user']['tenant_id']; ensure_lexware_tables($db);
    $customerId=$req->body['customer_id'] ?? null; $projectId=$req->body['project_id'] ?? null; if(!$customerId && !$projectId) Response::error('customer_id oder project_id fehlt',422);
    if($customerId){ $customer=$db->one("SELECT * FROM customers WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$customerId]); if(!$customer) Response::error('Kunde nicht gefunden',404); $link=$db->one("SELECT id FROM lexware_entity_links WHERE tenant_id=? AND local_entity_type='customer' AND local_entity_id=? AND lexware_entity_type='contact'",[$tid,$customerId]); if(!$link) $db->exec("INSERT INTO lexware_entity_links (id,tenant_id,local_entity_type,local_entity_id,lexware_entity_type,lexware_id,lexware_number,sync_direction,status,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,NOW(),NOW())",[uuid(),$tid,'customer',$customerId,'contact',$customer['lexware_contact_id'] ?? null,$customer['customer_no'] ?? null,'manual','linked']); }
    if($projectId){ $project=$db->one("SELECT * FROM projects WHERE tenant_id=? AND id=? AND deleted_at IS NULL",[$tid,$projectId]); if(!$project) Response::error('Auftrag nicht gefunden',404); $link=$db->one("SELECT id FROM lexware_entity_links WHERE tenant_id=? AND local_entity_type='project' AND local_entity_id=? AND lexware_entity_type='order'",[$tid,$projectId]); if(!$link) $db->exec("INSERT INTO lexware_entity_links (id,tenant_id,local_entity_type,local_entity_id,lexware_entity_type,lexware_id,lexware_number,sync_direction,status,created_at,updated_at) VALUES (?,?,?,?,?,?,?,?,?,NOW(),NOW())",[uuid(),$tid,'project',$projectId,'order',$project['lexware_order_id'] ?? null,$project['project_no'] ?? null,'manual','linked']); }
    lexware_log($db,$tid,'manual_link','success','Lokale Lexware-Verknüpfung gespeichert',null,['customer_id'=>$customerId,'project_id'=>$projectId]); Response::json(['ok'=>true]);
  });

  $r->dispatch($req,$ctx);
} catch (Throwable $e) {
  error_log($e->getMessage().' in '.$e->getFile().':'.$e->getLine());
  // Ursache mitgeben: In einer betriebsinternen Anwendung ist das die
  // schnellste Diagnose und spart den Weg ueber das Server-Protokoll.
  Response::error('Serverfehler: '.$e->getMessage().' ('.basename($e->getFile()).':'.$e->getLine().')',500);
}
