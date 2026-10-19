<?php
namespace App\Core;
final class Response {
  public static function json($data, int $status=200): void {
    http_response_code($status);
    header('Content-Type: application/json; charset=utf-8');
    header('X-Content-Type-Options: nosniff');
    echo json_encode($data, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
    exit;
  }
  public static function error(string $message, int $status=400, array $meta=[]): void {
    self::json(['error'=>['message'=>$message,'status'=>$status,'meta'=>$meta]], $status);
  }
}
