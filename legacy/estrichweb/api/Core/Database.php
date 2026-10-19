<?php
namespace App\Core;
use PDO;
final class Database {
  private PDO $pdo;
  public function __construct(array $config) {
    $db = $config['db'];
    $dsn = "mysql:host={$db['host']};dbname={$db['name']};charset={$db['charset']}";
    $this->pdo = new PDO($dsn, $db['user'], $db['pass'], [
      PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
      PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
      PDO::ATTR_EMULATE_PREPARES => false,
    ]);
  }
  public function pdo(): PDO { return $this->pdo; }
  public function one(string $sql, array $params=[]): ?array {
    $stmt=$this->pdo->prepare($sql); $stmt->execute($params); $row=$stmt->fetch(); return $row ?: null;
  }
  public function all(string $sql, array $params=[]): array {
    $stmt=$this->pdo->prepare($sql); $stmt->execute($params); return $stmt->fetchAll();
  }
  public function exec(string $sql, array $params=[]): int {
    $stmt=$this->pdo->prepare($sql); $stmt->execute($params); return $stmt->rowCount();
  }
  public function insert(string $sql, array $params=[]): string {
    $stmt=$this->pdo->prepare($sql); $stmt->execute($params); return (string)$this->pdo->lastInsertId();
  }
  public function tx(callable $fn) {
    $this->pdo->beginTransaction();
    try { $res=$fn($this); $this->pdo->commit(); return $res; }
    catch (\Throwable $e) { $this->pdo->rollBack(); throw $e; }
  }
}
