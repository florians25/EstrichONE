<?php
namespace App\Core;
final class Request {
  public string $method; public string $path; public array $query; public array $body; public array $headers;
  public function __construct() {
    $this->method = $_SERVER['REQUEST_METHOD'] ?? 'GET';
    $uri = parse_url($_SERVER['REQUEST_URI'] ?? '/', PHP_URL_PATH) ?: '/';
    $scriptDir = rtrim(str_replace('\\','/', dirname($_SERVER['SCRIPT_NAME'] ?? '/api/index.php')), '/');
    $path = $uri;
    if ($scriptDir && $scriptDir !== '/' && str_starts_with($path, $scriptDir)) $path = substr($path, strlen($scriptDir));
    $path = preg_replace('#^/api#', '', $path) ?: '/';
    $this->path = '/' . ltrim($path, '/');
    $this->query = $_GET;
    $this->headers = function_exists('getallheaders') ? getallheaders() : [];
    $raw = file_get_contents('php://input') ?: '';
    $json = json_decode($raw, true);
    $this->body = is_array($json) ? $json : $_POST;
  }
  public function bearer(): ?string {
    $headers = $this->headers;
    if (function_exists('apache_request_headers')) {
      $apacheHeaders = apache_request_headers();
      if (is_array($apacheHeaders)) $headers = array_merge($apacheHeaders, $headers);
    }
    $candidates = [
      $headers['Authorization'] ?? null,
      $headers['authorization'] ?? null,
      $headers['X-Authorization'] ?? null,
      $headers['x-authorization'] ?? null,
      $headers['X-EW-Authorization'] ?? null,
      $headers['x-ew-authorization'] ?? null,
      $_SERVER['HTTP_AUTHORIZATION'] ?? null,
      $_SERVER['REDIRECT_HTTP_AUTHORIZATION'] ?? null,
      $_SERVER['Authorization'] ?? null,
    ];
    foreach ($candidates as $h) {
      $h = trim((string)$h);
      if ($h === '') continue;
      if (preg_match('/Bearer\s+(.+)/i', $h, $m)) return trim($m[1]);
      if (substr_count($h, '.') === 2) return $h;
    }
    return null;
  }
}
