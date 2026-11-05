// Baut ein Benutzerhandbuch als PDF: Kapitel (Markdown) → HTML mit Print-CSS → Chromium (Playwright) → PDF.
// Zwei Durchläufe: Der erste ermittelt mit pdftotext (Poppler) die Seite jeder Überschrift, der zweite setzt die Seitenzahlen
// ins Inhaltsverzeichnis. Aufruf: node docs/handbuch/scripts/pdf.mjs [web|app]
//   web (Standard) → docs/handbuch/EstrichONE-Handbuch-Web.pdf   (Kapitel und Bilder unter docs/handbuch/)
//   app            → docs/handbuch/EstrichONE-Handbuch-App.pdf   (Kapitel und Bilder unter docs/handbuch/app/)
//   schulung [name …] → docs/schulung/pdf/*.pdf: jede Kurzanleitung und jede Schulungsunterlage als eigene PDF und die
//                    Sammel-PDF aller Kurzanleitungen (Quellen unter docs/schulung/, Namen filtern nach Dateinamen; „sammel“)
// Umgebung: PLAYWRIGHT_CHROMIUM (Browser), HB_VERSION (Standard: neueste Versionsnotiz), HB_STAND (Standard: heute).
import { createRequire } from "node:module";
import { readFileSync, readdirSync, writeFileSync, existsSync, statSync, mkdirSync } from "node:fs";
import { createHash } from "node:crypto";
import { tmpdir } from "node:os";
import { execFileSync } from "node:child_process";
import { dirname, join, resolve, basename } from "node:path";
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
const target = process.argv[2] ?? process.env.HB_HANDBUCH ?? "web";
const variant = variants[target];
if (!variant && target !== "schulung") throw new Error(`Unbekanntes Handbuch: ${process.argv[2]} (web, app oder schulung)`);
const dir = variant?.dir;
const out = variant && join(base, variant.file);
const htmlFile = variant && join(dir, "handbuch.html");

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
      const pre = chapter.number === "" ? "" : `${chapter.number}.`;
      if (level === 2) { h2++; h3 = 0; number = `${pre}${h2}`; }
      if (level === 3) { h3++; number = `${pre}${h2}.${h3}`; }
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
    if ((m = /^> \*\*(Hinweis|Tipp|Wichtig|Achtung|Beispiel|Lösung):\*\* ?(.*)$/.exec(line))) {
      flush();
      const kind = { Hinweis: "note", Tipp: "tip", Wichtig: "important", Achtung: "warning", Beispiel: "example", Lösung: "loesung" }[m[1]];
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

if (target === "schulung") {
  await schulung(process.argv.slice(3));
  process.exit(0);
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

async function print(html, file, htmlPath = htmlFile) {
  writeFileSync(htmlPath, html);
  const executablePath = process.env.PLAYWRIGHT_CHROMIUM ?? ["/opt/pw-browsers/chromium-1194/chrome-linux/chrome"].find(existsSync);
  const browser = await chromium.launch({ executablePath });
  const page = await browser.newPage();
  await page.goto(pathToFileURL(htmlPath).href, { waitUntil: "load" });
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

// ---- Schulungsunterlagen und Kurzanleitungen (docs/schulung) ------------------------------------------------------------
// Jede Markdown-Datei unter docs/schulung/{kurzanleitungen,unterlagen} wird eine eigene PDF. Kopfzeilen „% schlüssel: wert“
// am Dateianfang: datei (PDF-Name ohne „EstrichONE-“ und „.pdf“), art (Kopfzeile, z. B. „Kurzanleitung · App“), fuer
// (Zielgruppe), stil (kurz | blatt | dokument), inhalt (ja: Inhaltsverzeichnis), felder (Namens-/Datumszeile, z. B.
// „Name, Datum“). Bilder: ![Alt](pfad "Unterschrift"){klassen ausschnitt=x,y,b,h breite=px} – ImageMagick schneidet aus,
// verkleinert und speichert als JPEG (kleine PDFs). Listenpunkte „[ ] …“ werden Ankreuzkästchen.
async function schulung(filter) {
  const sdir = join(root, "docs/schulung");
  const outDir = join(sdir, "pdf");
  const cache = join(tmpdir(), "estrichone-schulung-bilder");
  mkdirSync(outDir, { recursive: true });
  mkdirSync(cache, { recursive: true });
  const css = readFileSync(join(base, "vorlage/handbuch.css"), "utf8") + "\n" + readFileSync(join(base, "app/vorlage/app.css"), "utf8") +
    "\n" + readFileSync(join(sdir, "vorlage/schulung.css"), "utf8");
  const logo = readFileSync(join(root, "branding/logo-horizontal-white.svg"), "utf8");
  const fonts = [600, 700, 800].map((w) => `@font-face { font-family: "Inter Tight"; font-weight: ${w}; src: url("${
    pathToFileURL(join(root, `branding/fonts/inter-tight-latin-${w}-normal.woff`))}"); }`).join("\n");

  const bild = (src, from, opts) => {
    const file = resolve(from, src);
    if (!existsSync(file)) throw new Error(`Bild fehlt: ${file}`);
    const key = createHash("sha1").update(file + statSync(file).mtimeMs + JSON.stringify(opts)).digest("hex").slice(0, 16);
    const target = join(cache, `${key}.jpg`);
    if (!existsSync(target)) {
      const args = [file];
      if (opts.ausschnitt) { const [x, y, w, h] = opts.ausschnitt.split(",").map(Number); args.push("-crop", `${w}x${h}+${x}+${y}`, "+repage"); }
      args.push("-resize", `${opts.breite ?? 1300}x>`, "-strip", "-quality", "82", "-sampling-factor", "4:2:0", target);
      execFileSync("convert", args);
    }
    return pathToFileURL(target).href;
  };

  const docs = [];
  for (const sub of ["kurzanleitungen", "unterlagen"]) {
    for (const f of readdirSync(join(sdir, sub)).filter((f) => f.endsWith(".md")).sort()) {
      const path = join(sdir, sub, f);
      const meta = {};
      const body = [];
      for (const line of readFileSync(path, "utf8").replace(/\r/g, "").split("\n")) {
        const m = /^% ([a-z]+): (.*)$/.exec(line);
        if (m && !body.length) meta[m[1]] = m[2].trim(); else body.push(line);
      }
      let src = body.join("\n");
      const title = /^# (.+)$/m.exec(src)?.[1] ?? f;
      src = src.replace(/^# .+\n/m, "");
      src = src.replace(/^(!\[[^\]]*\]\()([^)\s]+)((?: "[^"]+")?\))(?:\{([^}]+)\})?$/gm, (_, a, p, b, attrs = "") => {
        const opts = {};
        const cls = attrs.split(/\s+/).filter((t) => { const m = /^(ausschnitt|breite)=(.+)$/.exec(t); if (m) opts[m[1]] = m[2]; return t && !m; });
        return `${a}${bild(p, dirname(path), opts)}${b}${cls.length ? `{${cls.join(" ")}}` : ""}`;
      });
      docs.push({ sub, name: f.replace(/\.md$/, ""), meta, title, src, stil: meta.stil ?? (sub === "kurzanleitungen" ? "kurz" : "blatt") });
    }
  }

  const fuss = (d) => (d.sub === "kurzanleitungen" ? "Kurzanleitung" : d.meta.art?.split("·")[0].trim() ?? "Schulung");
  function section(d, idx, sammel) {
    headings.length = 0;
    figure = 0;
    let html = markdown(d.src, { number: "" }).replace(/<li>\[ \] /g, '<li class="cb">').replace(/<li>\[x\] /g, '<li class="cb an">');
    const toc = d.meta.inhalt === "ja" ? `<nav class="s-toc"><h2>Inhalt</h2><ol>${headings.filter((h) => h.level === 2).map((h) =>
      `<li><a href="#${h.id}"><span class="tn">${h.number}</span><span class="tt">${inline(h.text)}</span><span class="tp" data-id="${h.id}">000</span></a></li>`).join("")}</ol></nav>` : "";
    const felder = d.meta.felder ? `<div class="felder">${d.meta.felder.split(",").map((x) => `<span><b>${esc(x.trim())}</b><i></i></span>`).join("")}</div>` : "";
    const kontakt = d.sub === "kurzanleitungen" ? `<div class="kontakt">Fragen? Ansprechpartner im Betrieb: <i></i> Telefon: <i class="kurz"></i></div>` : "";
    return `<section class="sdoc stil-${d.stil}${d.meta.klasse ? ` ${d.meta.klasse}` : ""}${sammel ? " imsammel" : ""}" id="d${idx}">
<header class="band"><div class="band-logo">${logo}</div><div class="band-text"><p class="kicker">${esc(d.meta.art ?? "Schulung")}</p>
<h1>${inline(d.title)}<span class="hbAnchor" data-id="d${idx}"></span></h1>${d.meta.fuer ? `<p class="band-sub">Für: ${inline(d.meta.fuer)}</p>` : ""}</div></header>
${felder}${toc}<div class="sbody">${html}</div>${kontakt}</section>`;
  }
  const page = (titel, inner, links) => `<!doctype html><html lang="de"><head><meta charset="utf-8"><title>EstrichONE – ${esc(titel)} ${version}</title>
<style>${fonts}
${css}
@page { @bottom-left { content: "EstrichONE ${version} · ${links} · Stand ${stand}"; } }
@page sdoc { @top-right { content: "${titel.replace(/"/g, "")}"; } }
@page sdoc:first { @top-right { content: none; } }
</style></head><body class="schulung">${inner}</body></html>`;

  const executablePath = process.env.PLAYWRIGHT_CHROMIUM ?? ["/opt/pw-browsers/chromium-1194/chrome-linux/chrome"].find(existsSync);
  const browser = await chromium.launch({ executablePath });
  const htmlPath = join(sdir, "schulung.html");
  async function pdf(html, file) {
    writeFileSync(htmlPath, html);
    const p = await browser.newPage();
    await p.goto(pathToFileURL(htmlPath).href, { waitUntil: "load" });
    // Schriftschnitte, die nur in Kopf-/Fußzeilen vorkommen, lädt Chromium sonst nicht.
    await p.evaluate(() => Promise.all([600, 700, 800].map((w) => document.fonts.load(`${w} 10pt "Inter Tight"`))));
    await p.evaluate(() => document.fonts.ready);
    await p.pdf({ path: file, preferCSSPageSize: true, printBackground: true, outline: true, tagged: true });
    await p.close();
  }
  // Zwei Durchläufe, wenn Seitenzahlen im Inhalt stehen: Marken im ersten Durchlauf, Seiten aus pdftotext.
  async function bauen(render, file, mitSeiten) {
    if (mitSeiten) {
      const probe = join(outDir, ".probe.pdf");
      await pdf(render(null).replace(/<span class="hbAnchor" data-id="([^"]+)"><\/span>/g, '<span class="hbAnchor">HBMARK$1HBEND</span>'), probe);
      const text = execFileSync("pdftotext", ["-raw", probe, "-"], { maxBuffer: 64 * 1024 * 1024 }).toString();
      const pages = {};
      text.split("\f").forEach((t, i) => { for (const m of t.matchAll(/HBMARK([A-Za-z0-9-]+)HBEND/g)) pages[m[1]] ??= i + 1; });
      execFileSync("rm", ["-f", probe]);
      await pdf(render(pages), file);
    } else await pdf(render(null), file);
    const n = Number(/Pages:\s+(\d+)/.exec(execFileSync("pdfinfo", [file]).toString())?.[1]);
    return n;
  }
  const seiten = (html, pages) => html.replace(/<span class="tp" data-id="([^"]+)">000<\/span>/g, (_, id) => `<span class="tp">${pages?.[id] ?? ""}</span>`);
  const wanted = (n) => !filter.length || filter.some((x) => n.includes(x));
  const ergebnis = [];
  for (const [i, d] of docs.entries()) {
    if (!d.meta.datei) throw new Error(`${d.sub}/${d.name}.md: Kopfzeile „% datei:“ fehlt`);
    if (!wanted(d.name)) continue;
    const file = join(outDir, `EstrichONE-${d.meta.datei}.pdf`);
    const html = section(d, i, false);
    const n = await bauen((pages) => page(d.title, seiten(html, pages), fuss(d)), file, d.meta.inhalt === "ja");
    ergebnis.push([file, n]);
    if (d.sub === "kurzanleitungen" && n > 2) console.log(`WARNUNG: ${d.name} hat ${n} Seiten (Kurzanleitung: höchstens 2)`);
  }
  // Sammel-PDF aller Kurzanleitungen mit Titelseite und Inhalt.
  if (wanted("sammel")) {
    const kurz = docs.map((d, i) => [d, i]).filter(([d]) => d.sub === "kurzanleitungen");
    const titel = "Kurzanleitungen";
    const render = (pages) => {
      const toc = kurz.map(([d, i]) => `<li class="l1"><a href="#d${i}"><span class="tn">${esc(d.meta.art?.split("·")[1]?.trim() ?? "")}</span>` +
        `<span class="tt">${inline(d.title)}</span><span class="tp">${pages ? pages[`d${i}`] ?? "" : "000"}</span></a></li>`).join("");
      return page(titel, `<section class="cover">
  <div class="cover-logo">${logo}</div>
  <div class="cover-title"><p class="kicker">Schulung</p><h1 class="cover-h">Alle Kurzanleitungen</h1>
  <p class="cover-sub">zum Ausdrucken und Aushängen – App für die Baustelle, Web für Büro, Bauleitung und Administration</p></div>
  <div class="cover-meta"><table><tr><th>Version</th><td>${version}</td></tr><tr><th>Stand</th><td>${stand}</td></tr>
  <tr><th>Gilt für</th><td>App für iPhone und iPad, Weboberfläche – Server ${serverLine}</td></tr></table></div>
</section>
<section class="toc"><h1 class="toc-h">Inhalt</h1><ol class="toc-list sammel-toc">${toc}</ol></section>
${kurz.map(([d, i]) => section(d, i, true)).join("\n")}`, "Kurzanleitungen");
    };
    const file = join(outDir, "EstrichONE-Kurzanleitungen-alle.pdf");
    ergebnis.push([file, await bauen(render, file, true)]);
  }
  await browser.close();
  if (!process.env.HB_HTML) execFileSync("rm", ["-f", htmlPath]);
  for (const [file, n] of ergebnis) console.log(`${basename(file)}: ${n} Seiten, ${(statSync(file).size / 1048576).toFixed(2)} MB`);
}
