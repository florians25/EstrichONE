<?php
return [
  'db' => ['host' => 'localhost', 'name' => 'datenbank', 'user' => 'benutzer', 'pass' => 'passwort', 'charset' => 'utf8mb4'],
  'app' => ['name' => 'EstrichWeb Enterprise', 'env' => 'production', 'url' => '', 'jwt_secret' => 'CHANGE_ME', 'jwt_ttl_minutes' => 120, 'refresh_ttl_days' => 30, 'upload_max_bytes' => 52428800, 'storage_path' => __DIR__ . '/../../storage']
];
