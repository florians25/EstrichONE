<?php
namespace App\Core;
final class Jwt {
  private static function b64(string $s): string { return rtrim(strtr(base64_encode($s), '+/', '-_'), '='); }
  private static function ub64(string $s): string { return base64_decode(strtr($s, '-_', '+/')); }
  public static function sign(array $payload, string $secret, int $ttlMinutes): string {
    $header=['typ'=>'JWT','alg'=>'HS256']; $payload['iat']=time(); $payload['exp']=time()+$ttlMinutes*60;
    $a=self::b64(json_encode($header)); $b=self::b64(json_encode($payload));
    $sig=hash_hmac('sha256', "$a.$b", $secret, true); return "$a.$b.".self::b64($sig);
  }
  public static function verify(?string $token, string $secret): ?array {
    if (!$token || substr_count($token,'.')!==2) return null;
    [$a,$b,$c]=explode('.',$token); $sig=self::b64(hash_hmac('sha256', "$a.$b", $secret, true));
    if (!hash_equals($sig,$c)) return null;
    $payload=json_decode(self::ub64($b), true); if (!is_array($payload)) return null;
    if (($payload['exp'] ?? 0) < time()) return null; return $payload;
  }
}
