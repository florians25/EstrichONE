// Kleiner Webserver für die Handbuch-Bilder: liefert den UI5-Build (web/dist) aus und leitet /api an die API weiter –
// wie Caddy im Betrieb (unbekannte Pfade → index.html). Aufruf: node server.mjs <dist> <port> <api-port>
import { createServer, request } from "node:http";
import { readFile, stat } from "node:fs/promises";
import { extname, join, normalize } from "node:path";

const [dist, port = "4291", apiPort = "5191"] = process.argv.slice(2);
const types = { ".html": "text/html; charset=utf-8", ".js": "text/javascript", ".css": "text/css", ".json": "application/json",
  ".svg": "image/svg+xml", ".png": "image/png", ".ico": "image/x-icon", ".woff2": "font/woff2", ".woff": "font/woff",
  ".webmanifest": "application/manifest+json", ".properties": "text/plain; charset=utf-8", ".xml": "application/xml", ".ttf": "font/ttf" };

createServer(async (req, res) => {
  const url = new URL(req.url ?? "/", "http://localhost");
  if (url.pathname.startsWith("/api/")) {
    const proxied = request({ host: "127.0.0.1", port: apiPort, path: req.url, method: req.method,
      headers: { ...req.headers, "x-forwarded-for": "127.0.0.1" } }, (answer) => {
      res.writeHead(answer.statusCode ?? 502, answer.headers);
      answer.pipe(res);
    });
    proxied.on("error", () => { res.writeHead(502); res.end(); });
    req.pipe(proxied);
    return;
  }
  let file = join(dist, normalize(decodeURIComponent(url.pathname)).replace(/^(\.\.[/\\])+/, ""));
  try {
    if (!(await stat(file)).isFile()) throw new Error();
  } catch {
    file = join(dist, "index.html");
  }
  res.writeHead(200, { "content-type": types[extname(file)] ?? "application/octet-stream" });
  res.end(await readFile(file));
}).listen(Number(port), "127.0.0.1");
