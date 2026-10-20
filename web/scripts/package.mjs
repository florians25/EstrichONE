// Verpackt das UI5-Build-Ergebnis für Caddy (docker/web.Dockerfile):
// - Alle Skripte, Stile und Themes liegen unter /ui-<hash>/ – Caddy liefert *.js/*.css als „immutable“ aus,
//   der Inhalts-Hash im Pfad sorgt dafür, dass ein Update sofort ankommt. index.html wird nie zwischengespeichert.
// - Nicht benötigte Dateien (Quelltexte der Themes, Source-Maps, Debug-Varianten, fremde Sprachen) entfallen.
// Aufruf: node scripts/package.mjs <build-ordner> <ziel-ordner>
import { createHash } from 'node:crypto';
import { cpSync, existsSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { join, relative } from 'node:path';

const [source = 'build', target = 'dist'] = process.argv.slice(2);

function walk(dir) {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    return statSync(path).isDirectory() ? walk(path) : [path];
  });
}

function unwanted(path) {
  const name = path.split('/').pop();
  if (/\.(less|map|ts)$/.test(name) || /-dbg\.js$/.test(name) || /-dbg\.[a-z]+\.js$/.test(name) || /-RTL\.css$/.test(name)) {
    return true;
  }
  if (name.endsWith('.properties')) {
    const match = name.match(/_([a-z]{2,3})(?:_[A-Za-z0-9]+)?\.properties$/);
    return match !== null && !['de', 'en'].includes(match[1]);
  }
  return false;
}

rmSync(target, { recursive: true, force: true });
const files = walk(source).filter((f) => !unwanted(f) && !f.endsWith('index.html'));
const hash = createHash('sha256');
for (const file of files.sort()) {
  hash.update(relative(source, file));
  hash.update(readFileSync(file));
}
const version = `ui-${hash.digest('hex').slice(0, 12)}`;
const base = join(target, version);
for (const file of files) {
  cpSync(file, join(base, relative(source, file)));
}

const html = readFileSync(join(source, 'index.html'), 'utf8')
  .replaceAll('"/resources/', `"/${version}/resources/`)
  .replaceAll('"estrichone": "/"', `"estrichone": "/${version}/"`)
  .replaceAll('href="/boot.css"', `href="/${version}/boot.css"`);
if (html.includes('"/resources/')) {
  throw new Error('index.html enthält noch unversionierte Pfade.');
}
writeFileSync(join(target, 'index.html'), html);
if (existsSync('public')) {
  cpSync('public', target, { recursive: true });
}
console.log(`Verpackt: ${files.length} Dateien unter /${version}/`);
