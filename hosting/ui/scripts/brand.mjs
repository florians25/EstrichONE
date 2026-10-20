// Übernimmt die Logo-Dateien unverändert aus branding/ (Quelle: Logo-Paket) nach webapp/brand/.
// Kein eigenes Logo, kein SAP-Branding: ShellBar, Anmeldeseite, Favicon und Wartungsseite nutzen nur diese Dateien.
import { copyFileSync, existsSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const source = process.env.BRANDING_DIR ?? join(root, '..', '..', 'branding');
const target = join(root, 'webapp', 'brand');
const files = [
  'logo.svg',
  'logo-small.svg',
  'logo-mark-white.svg',
  'logo-mark-black.svg',
  'logo-horizontal.svg',
  'logo-horizontal-white.svg',
  'logo-horizontal-mono-white.svg',
  'logo-horizontal-mono-black.svg',
];
mkdirSync(target, { recursive: true });
for (const file of files) {
  const from = join(source, file);
  if (!existsSync(from)) {
    throw new Error(`Logo-Datei fehlt: ${from}`);
  }
  copyFileSync(from, join(target, file));
}
console.log(`Logo: ${files.length} Dateien aus ${source}`);
