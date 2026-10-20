// Verpackt das UI5-Build-Ergebnis für die Konsole (wwwroot):
// - Skripte, Stile und Themes liegen unter /ui-<hash>/ und dürfen dauerhaft zwischengespeichert werden;
//   index.html wird nie zwischengespeichert, ein Update kommt daher sofort an.
// - Nicht benötigte Dateien (Theme-Quellen, Source-Maps, Debug-Varianten, fremde Sprachen) entfallen.
// - Logo-Dateien bleiben unter /brand/ (auch für die Wartungsseite des Proxys).
// Aufruf: node scripts/package.mjs <build-ordner> <ziel-ordner>
import { createHash } from 'node:crypto';
import { cpSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { join, relative, sep } from 'node:path';

const [source = 'build', target = 'dist'] = process.argv.slice(2);

function walk(dir) {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    return statSync(path).isDirectory() ? walk(path) : [path];
  });
}

function unwanted(path) {
  const name = path.split(sep).pop();
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
const isBrand = (file) => relative(source, file).startsWith(`brand${sep}`);
const files = walk(source).filter((f) => !unwanted(f) && !f.endsWith('index.html') && !isBrand(f));
const hash = createHash('sha256');
for (const file of files.sort()) {
  hash.update(relative(source, file));
  hash.update(readFileSync(file));
}
const version = `ui-${hash.digest('hex').slice(0, 12)}`;
for (const file of files) {
  cpSync(file, join(target, version, relative(source, file)));
}
cpSync(join(source, 'brand'), join(target, 'brand'), { recursive: true });

const html = readFileSync(join(source, 'index.html'), 'utf8')
  .replaceAll('src="resources/', `src="/${version}/resources/`)
  .replaceAll('"estrichone.hosting": "./"', `"estrichone.hosting": "/${version}/"`)
  .replaceAll('href="boot.css"', `href="/${version}/boot.css"`)
  .replaceAll('src="theme-init.js"', `src="/${version}/theme-init.js"`)
  .replaceAll('"brand/', '"/brand/');
for (const marker of ['src="resources/', '"./"', 'href="boot.css"', 'src="theme-init.js"']) {
  if (html.includes(marker)) {
    throw new Error(`index.html enthält noch unversionierte Pfade (${marker}).`);
  }
}
writeFileSync(join(target, 'index.html'), html);
console.log(`Verpackt: ${files.length} Dateien unter /${version}/`);
