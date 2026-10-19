<?php
namespace App\Core;
final class Router {
  private array $routes=[];
  public function add(string $method, string $pattern, callable $handler, array $permissions=[]): void {
    $regex = '#^'.preg_replace('#\{([a-zA-Z_][a-zA-Z0-9_]*)\}#','(?P<$1>[^/]+)', $pattern).'$#';
    $this->routes[] = [$method,$regex,$handler,$permissions,$pattern];
  }
  public function dispatch(Request $req, array $ctx): void {
    foreach ($this->routes as [$method,$regex,$handler,$permissions,$pattern]) {
      if ($method !== $req->method) continue;
      if (preg_match($regex, $req->path, $m)) {
        $params=[]; foreach ($m as $k=>$v) if (!is_int($k)) $params[$k]=$v;
        if ($permissions) {
          $userPerms=$ctx['user']['permissions'] ?? [];
          foreach ($permissions as $p) if (!in_array($p,$userPerms,true) && !in_array('*',$userPerms,true)) Response::error('Keine Berechtigung: '.$p,403);
        }
        $handler($req,$ctx,$params); return;
      }
    }
    Response::error('Route nicht gefunden: '.$req->method.' '.$req->path,404);
  }
}
