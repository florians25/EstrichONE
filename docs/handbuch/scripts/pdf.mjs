// Baut ein Benutzerhandbuch als PDF: Kapitel (Markdown) → HTML mit Print-CSS → Chromium (Playwright) → PDF.
// Zwei Durchläufe: Der erste ermittelt mit pdftotext (Poppler) die Seite jeder Überschrift, der zweite setzt die Seitenzahlen
// ins Inhaltsverzeichnis. Aufruf: node docs/handbuch/scripts/pdf.mjs [web|app]
//   web (Standard) → docs/handbuch/EstrichONE-Handbuch-Web.pdf   (Kapitel und Bilder unter docs/handbuch/)
//   app            → docs/handbuch/EstrichONE-Handbuch-App.pdf   (Kapitel und Bilder unter docs/handbuch/app/)
// Umgebung: PLAYWRIGHT_CHROMIUM (Browser), HB_VERSION (Standard: neueste Versionsnotiz), HB_STAND (Standard: heute).
import { createRequire } from "node:module";
import { readFileSync, readdirSync, writeFileSync, existsSync, statSync } from "node:fs";
import { execFileSync } from "node:child_process";
import { dirname, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const base = join(here, "..");
const root = join(base, "../..");
const require = createRequire(join(root, "web/package.json"));
const { chromium } = require("playwright-core");

const version = process.env.HB_VERSION ?? readdirSync(join(root, "docs/releases")).filter((f) => /^v\d+\.\d+\.\d+\.md$/.test(f))
  .map((f) => f.slice(1, -3)).sort((a, b) => a.localeCompare(b, undefined, { numeric: true })).pop();
const stand = process.env.HB_STAND ?? new Date().toLocaleDateString("de-DE", { day: "numeric", month: "long", year: "numeric" });
const serverLine = version.replace(/\.\d+$/, ".x");
const variants = {
  web: {
    dir: base, file: "EstrichONE-Handbuch-Web.pdf", name: "Benutzerhandbuch Web", heading: "Die Weboberfläche von EstrichONE",
    sub: "für Büro, Bauleitung und Administration im Estrichbetrieb", scope: `Weboberfläche (Browser) – Server ${serverLine}`,
  },
  app: {
    dir: join(base, "app"), file: "EstrichONE-Handbuch-App.pdf", name: "Benutzerhandbuch App", heading: "Die App EstrichONE für iPhone und iPad",
    sub: "für Bauleitung, Kolonnen und Büro – auf der Baustelle und unterwegs",
    scope: `iPhone und iPad ab iOS 17 – Server ${serverLine}`, css: "vorlage/app.css", cover: "bilder/titel.jpg",
  },
};
const variant = variants[process.argv[2] ?? process.env.HB_HANDBUCH ?? "web"];
if (!variant) throw new Error(`Unbekanntes Handbuch: ${process.argv[2]} (web oder app)`);
const dir = variant.dir;
const out = join(base, variant.file);
const htmlFile = join(dir, "handbuch.html");

// ---- Markdown (bewusst kleine, eindeutige Teilmenge) -------------------------------------------------------------------
const esc = (s) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
function inline(text) {
  // Rohes HTML (<kbd>, <br>) bleibt erhalten; sonst escapen.
  const parts = text.split(/(<\/?(?:kbd|br|span|sup|sub)[^>]*>)/g);
  return parts.map((p, i) => (i % 2 ? p : esc(p)
    .replace(/`([^`]+)`/g, "<code>$1</code>")
    .replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>")
    .replace(/(^|[\s(„])_([^_]+)_(?=[\s.,;:!?)“]|$)/g, "$1<em>$2</em>")
    .replace(/\[([^\]]+)\]\((#[^)]+)\)/g, '<a href="$2">$1</a>')
    .replace(/\[([^\]]+)\]\(([^)]+)\)/g, '<a href="$2">$1</a>')
    .replace(/ -- /g, " – ")
    .replace(/→/g, '<span class="arrow">→</span>'))).join("");
}
const slug = (s) => s.toLowerCase().replace(/<[^>]+>/g, "").replace(/[äöüß]/g, (c) => ({ ä: "ae", ö: "oe", ü: "ue", ß: "ss" })[c])
  .replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");

const headings = [];
let figure = 0;
function markdown(src, chapter) {
  const lines = src.replace(/\r/g, "").split("\n");
  const html = [];
  let i = 0;
  let h2 = 0;
  let h3 = 0;
  const para = [];
  const flush = () => {
    if (para.length) html.push(`<p>${inline(para.join(" "))}</p>`);
    para.length = 0;
  };
  while (i < lines.length) {
    const line = lines[i];
    let m;
    if (!line.trim()) { flush(); i++; continue; }
    if ((m = /^(#{1,3}) (.+?)(?: \{#([a-z0-9-]+)\})?$/.exec(line))) {
      flush();
      const level = m[1].length;
      let number = `${chapter.number}`;
      if (level === 2) { h2++; h3 = 0; number = `${chapter.number}.${h2}`; }
      if (level === 3) { h3++; number = `${chapter.number}.${h2}.${h3}`; }
      const id = m[3] ?? `k${number.replace(/\./g, "-").toLowerCase()}-${slug(m[2])}`;
      if (level <= 2) headings.push({ id, level, number: chapter.appendix && level === 1 ? chapter.label : number, text: m[2] });
      const label = chapter.appendix && level === 1 ? chapter.label : number;
      html.push(`<h${level} id="${id}"><span class="num">${label}</span> ${inline(m[2])}<span class="hbAnchor" data-id="${id}"></span></h${level}>`);
      i++; continue;
    }
    const image = /^!\[([^\]]*)\]\(([^)\s]+)(?: "([^"]+)")?\)(?:\{([^}]+)\})?$/;
    if (image.test(line)) {
      // Bildzeilen direkt untereinander (ohne Leerzeile) stehen nebeneinander in einer Reihe.
      flush();
      const row = [];
      while (i < lines.length && (m = image.exec(lines[i]))) {
        figure++;
        const cls = m[4] ? ` class="${m[4]}"` : "";
        row.push(`<figure${cls}><img src="${m[2]}" alt="${esc(m[1])}"><figcaption><span>Abb. ${figure}</span> ${inline(m[3] ?? m[1])}</figcaption></figure>`);
        i++;
      }
      html.push(row.length > 1 ? `<div class="figrow n${row.length}">${row.join("")}</div>` : row[0]);
      continue;
    }
    if ((m = /^> \*\*(Hinweis|Tipp|Wichtig|Achtung|Beispiel):\*\* ?(.*)$/.exec(line))) {
      flush();
      const kind = { Hinweis: "note", Tipp: "tip", Wichtig: "important", Achtung: "warning", Beispiel: "example" }[m[1]];
      const body = [m[2]];
      i++;
      while (i < lines.length && lines[i].startsWith(">")) { body.push(lines[i].replace(/^> ?/, "")); i++; }
      html.push(`<aside class="box ${kind}"><strong>${m[1]}</strong> ${body.join("\n").split(/\n\s*\n/).map((b) => inline(b.replace(/\n/g, " "))).join("<br>")}</aside>`);
      continue;
    }
    if (/^\|/.test(line)) {
      flush();
      const rows = [];
      while (i < lines.length && /^\|/.test(lines[i])) { rows.push(lines[i]); i++; }
      const cells = (r) => r.replace(/^\||\|$/g, "").split("|").map((c) => c.trim());
      const head = cells(rows[0]);
      const body = rows.slice(2).map(cells);
      html.push(`<table><thead><tr>${head.map((c) => `<th>${inline(c)}</th>`).join("")}</tr></thead><tbody>${
        body.map((r) => `<tr>${r.map((c) => `<td>${inline(c)}</td>`).join("")}</tr>`).join("")}</tbody></table>`);
      continue;
    }
    if (/^\s*([-*]|\d+\.) /.test(line)) {
      flush();
      const items = [];
      const ordered = /^\s*\d+\./.test(line);
      const start = ordered ? Number(/^\s*(\d+)\./.exec(line)[1]) : 1;
      while (i < lines.length && (/^\s*([-*]|\d+\.) /.test(lines[i]) || (/^ {2,}\S/.test(lines[i]) && items.length))) {
        const l = lines[i];
        if (/^ {2,}([-*]|\d+\.) /.test(l)) {
          items[items.length - 1].sub.push(l.replace(/^\s*([-*]|\d+\.) /, ""));
        } else if (/^\s*([-*]|\d+\.) /.test(l)) {
          items.push({ text: l.replace(/^\s*([-*]|\d+\.) /, ""), sub: [] });
        } else {
          items[items.length - 1].text += " " + l.trim();
        }
        i++;
      }
      const tag = ordered ? "ol" : "ul";
      html.push(`<${tag}${start > 1 ? ` start="${start}"` : ""}>${items.map((it) => `<li>${inline(it.text)}${it.sub.length ? `<ul>${it.sub.map((s) => `<li>${inline(s)}</li>`).join("")}</ul>` : ""}</li>`).join("")}</${tag}>`);
      continue;
    }
    if (/^<(div|table|dl|section)/.test(line)) {
      flush();
      const block = [];
      while (i < lines.length && lines[i].trim()) { block.push(lines[i]); i++; }
      html.push(block.join("\n"));
      continue;
    }
    para.push(line.trim());
    i++;
  }
  flush();
  return html.join("\n");
}

// ---- Kapitel ---------------------------------------------------------------------------------------------------------
const files = readdirSync(join(dir, "kapitel")).filter((f) => f.endsWith(".md")).sort();
const chapters = [];
let n = 0;
let appendix = 0;
for (const f of files) {
  const src = readFileSync(join(dir, "kapitel", f), "utf8");
  const isAppendix = f.startsWith("a");
  const chapter = isAppendix ? { number: String.fromCharCode(65 + appendix++), appendix: true } : { number: String(++n) };
  if (isAppendix) chapter.label = `Anhang ${chapter.number}`;
  const title = /^# (.+?)(?: \{#.*\})?$/m.exec(src)?.[1] ?? f;
  chapters.push({ ...chapter, title, html: markdown(src, chapter) });
}

function render(pages) {
  const toc = headings.map((h) => `<li class="l${h.level}"><a href="#${h.id}"><span class="tn">${h.number}</span><span class="tt">${inline(h.text)}</span>` +
    `<span class="tp">${pages ? pages[h.id] ?? "" : "000"}</span></a></li>`).join("\n");
  const css = readFileSync(join(base, "vorlage/handbuch.css"), "utf8") + (variant.css ? "\n" + readFileSync(join(dir, variant.css), "utf8") : "");
  const pageRules = chapters.map((c, i) => `@page kap${i} { @top-right { content: "${c.appendix ? c.label : c.number} · ${c.title.replace(/"/g, "")}"; } }`).join("\n");
  const logo = readFileSync(join(root, "branding/logo-horizontal-white.svg"), "utf8");
  return `<!doctype html><html lang="de"><head><meta charset="utf-8"><title>EstrichONE – ${variant.name} ${version}</title>
<style>
@font-face { font-family: "Inter Tight"; font-weight: 600; src: url("${pathToFileURL(join(root, "branding/fonts/inter-tight-latin-600-normal.woff"))}"); }
@font-face { font-family: "Inter Tight"; font-weight: 700; src: url("${pathToFileURL(join(root, "branding/fonts/inter-tight-latin-700-normal.woff"))}"); }
@font-face { font-family: "Inter Tight"; font-weight: 800; src: url("${pathToFileURL(join(root, "branding/fonts/inter-tight-latin-800-normal.woff"))}"); }
${css}
@page { @bottom-left { content: "EstrichONE ${version} · ${variant.name}"; } }
${pageRules}
</style></head><body>
<section class="cover">
  <div class="cover-logo">${logo}</div>
  ${variant.cover ? `<img class="cover-device" src="${variant.cover}" alt="">` : ""}
  <div class="cover-title">
    <p class="kicker">Benutzerhandbuch</p>
    <h1 class="cover-h">${variant.heading}</h1>
    <p class="cover-sub">${variant.sub}</p>
  </div>
  <div class="cover-meta">
    <table><tr><th>Version</th><td>${version}</td></tr><tr><th>Stand</th><td>${stand}</td></tr>
    <tr><th>Gilt für</th><td>${variant.scope}</td></tr></table>
  </div>
</section>
<section class="toc"><h1 class="toc-h">Inhalt</h1><ol class="toc-list">${toc}</ol></section>
${chapters.map((c, i) => `<section class="chapter${c.appendix ? " appendix" : ""}" style="page: kap${i}">${c.html}</section>`).join("\n")}
</body></html>`;
}

async function print(html, file) {
  writeFileSync(htmlFile, html);
  const executablePath = process.env.PLAYWRIGHT_CHROMIUM ?? ["/opt/pw-browsers/chromium-1194/chrome-linux/chrome"].find(existsSync);
  const browser = await chromium.launch({ executablePath });
  const page = await browser.newPage();
  await page.goto(pathToFileURL(htmlFile).href, { waitUntil: "load" });
  await page.evaluate(() => document.fonts.ready);
  await page.pdf({ path: file, preferCSSPageSize: true, printBackground: true, outline: true, tagged: true });
  await browser.close();
}

// Durchlauf 1: Seiten der Überschriften über unsichtbare Marken ermitteln.
const marked = render(null).replace(/<span class="hbAnchor" data-id="([^"]+)"><\/span>/g, '<span class="hbAnchor">HBMARK$1HBEND</span>');
const probe = join(dir, ".probe.pdf");
await print(marked, probe);
const text = execFileSync("pdftotext", ["-raw", probe, "-"], { maxBuffer: 64 * 1024 * 1024 }).toString();
const pages = {};
text.split("\f").forEach((pageText, index) => {
  for (const m of pageText.matchAll(/HBMARK([A-Za-z0-9-]+)HBEND/g)) pages[m[1]] ??= index + 1;
});
const missing = headings.filter((h) => !pages[h.id]).map((h) => h.id);
if (missing.length) console.log("Ohne Seitenzahl:", missing.join(", "));
// Durchlauf 2: endgültig.
await print(render(pages), out);
execFileSync("rm", ["-f", probe]);
const info = execFileSync("pdfinfo", [out]).toString();
console.log(`${out}: ${/Pages:\s+(\d+)/.exec(info)?.[1]} Seiten, ${(statSync(out).size / 1048576).toFixed(1)} MB`);
