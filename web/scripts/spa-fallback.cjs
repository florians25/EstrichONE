// Entwicklungsserver: Dateien aus public/ (Logos, Favicons) wie im Betrieb unter / ausliefern; Pfade der
// Anwendung ohne Dateiendung (Deep-Links wie /bauakten/…) liefern index.html – wie try_files im Caddyfile.
const { existsSync, statSync, createReadStream } = require('node:fs');
const { join, normalize, extname } = require('node:path');

const types = { '.svg': 'image/svg+xml', '.png': 'image/png', '.ico': 'image/x-icon', '.webmanifest': 'application/manifest+json' };

module.exports = function () {
  const publicDir = join(__dirname, '..', 'public');
  return function (req, res, next) {
    const path = decodeURIComponent(req.url.split('?')[0]);
    const file = normalize(join(publicDir, path));
    if (file.startsWith(publicDir) && path !== '/' && existsSync(file) && statSync(file).isFile()) {
      res.setHeader('Content-Type', types[extname(file)] ?? 'application/octet-stream');
      createReadStream(file).pipe(res);
      return;
    }
    if (!path.startsWith('/api/') && !path.startsWith('/resources/') && !path.startsWith('/test-resources/')
      && path !== '/' && !/\.[a-z0-9]+$/i.test(path)) {
      req.url = '/index.html';
    }
    next();
  };
};
