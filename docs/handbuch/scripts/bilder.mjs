// Bildschirmfotos für das Handbuch (Playwright, 1440×900, Horizon hell) gegen die Demo-Instanz aus umgebung.sh.
// Aufruf: node bilder.mjs einrichtung   (Ersteinrichtung im Browser, danach demodaten.py)
//         node bilder.mjs [name …]      (alle bzw. ausgewählte Bilder nach docs/handbuch/bilder/)
// Umgebung: HB_BASE (Standard http://localhost:4291), HB_WORK (Einrichtungscode), PLAYWRIGHT_CHROMIUM (Browser).
import { createRequire } from "node:module";
import { readFileSync, existsSync, mkdirSync } from "node:fs";
import { execFileSync } from "node:child_process";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, "../../..");
const require = createRequire(join(root, "web/package.json"));
const { chromium } = require("playwright-core");
const BASE = process.env.HB_BASE ?? "http://localhost:4291";
const WORK = process.env.HB_WORK ?? "/tmp/estrichone-handbuch";
const OUT = join(here, "../bilder");
const ADMIN = { login: "c.berger", password: "Estrich-Muster-2026!" };
mkdirSync(OUT, { recursive: true });

const executablePath = process.env.PLAYWRIGHT_CHROMIUM ?? ["/opt/pw-browsers/chromium-1194/chrome-linux/chrome"].find(existsSync);
const browser = await chromium.launch({ executablePath });
const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: "de-DE", timezoneId: "Europe/Berlin",
  deviceScaleFactor: 1, colorScheme: "light" });
const page = await context.newPage();
page.on("pageerror", (e) => console.log("Seitenfehler:", e.message));

const wait = (ms) => page.waitForTimeout(ms);
async function idle() {
  await page.waitForLoadState("networkidle").catch(() => undefined);
  await page.locator(".sapUiLocalBusyIndicator, .sapMBusyIndicator").first().waitFor({ state: "hidden", timeout: 8000 }).catch(() => undefined);
  await wait(700);
}

/** Markierungen (nummerierte Kreise, Rahmen) auf die Seite legen; Ziel als Locator oder Rechteck. */
async function mark(items) {
  const boxes = [];
  for (const it of items) {
    const box = it.box ?? (await it.target.first().boundingBox().catch(() => null));
    if (box) boxes.push({ ...box, n: it.n ?? null, frame: it.frame !== false, pos: it.pos ?? "left" });
  }
  await page.evaluate((list) => {
    document.querySelectorAll(".hbMark").forEach((e) => e.remove());
    for (const b of list) {
      if (b.frame) {
        const f = document.createElement("div");
        f.className = "hbMark";
        Object.assign(f.style, { position: "fixed", left: b.x - 3 + "px", top: b.y - 3 + "px", width: b.width + 6 + "px", height: b.height + 6 + "px",
          border: "2px solid #E8590C", borderRadius: "6px", zIndex: 99999, pointerEvents: "none", boxShadow: "0 0 0 3px rgba(232,89,12,.18)" });
        document.body.appendChild(f);
      }
      if (b.n !== null) {
        const c = document.createElement("div");
        c.className = "hbMark";
        c.textContent = String(b.n);
        const x = b.pos === "right" ? b.x + b.width + 4 : b.pos === "top" ? b.x + b.width / 2 - 12 : b.pos === "bottom" ? b.x + b.width / 2 - 12 : b.x - 28;
        const y = b.pos === "top" ? b.y - 30 : b.pos === "bottom" ? b.y + b.height + 6 : b.y + b.height / 2 - 12;
        Object.assign(c.style, { position: "fixed", left: Math.max(2, x) + "px", top: Math.max(2, y) + "px", width: "24px", height: "24px",
          borderRadius: "50%", background: "#E8590C", color: "#fff", font: "700 13px/24px Arial, sans-serif", textAlign: "center",
          zIndex: 100000, pointerEvents: "none", boxShadow: "0 1px 3px rgba(0,0,0,.35)" });
        document.body.appendChild(c);
      }
    }
  }, boxes);
}
const unmark = () => page.evaluate(() => document.querySelectorAll(".hbMark").forEach((e) => e.remove()));

/** Bild speichern: PNG → komprimiert (Palette, wenn verlustarm möglich) per ImageMagick, sonst JPEG. */
async function shot(name, options = {}) {
  await wait(options.delay ?? 400);
  const file = join(OUT, `${name}.png`);
  const clip = options.clip ? await resolveClip(options.clip) : undefined;
  await page.screenshot({ path: file, clip, fullPage: false });
  await unmark();
  try {
    execFileSync("convert", [file, "-strip", "-define", "png:compression-level=9", "-colors", "256", "-dither", "None", file]);
  } catch {
    // ImageMagick fehlt: unkomprimiert lassen.
  }
  console.log("Bild:", name);
}
async function resolveClip(clip) {
  if (clip.x !== undefined) return clip;
  const b = await clip.first().boundingBox();
  const pad = 12;
  return { x: Math.max(0, b.x - pad), y: Math.max(0, b.y - pad), width: Math.min(1440 - Math.max(0, b.x - pad), b.width + 2 * pad), height: Math.min(900 - Math.max(0, b.y - pad), b.height + 2 * pad) };
}

const field = (id) => page.locator(`input[id$="${id}-inner"], textarea[id$="${id}-inner"]`).first();
const dialog = () => page.locator(".sapMDialog:visible").last();
async function closeDialogs() {
  for (let i = 0; i < 3; i++) {
    const d = page.locator(".sapMDialog:visible");
    if (!(await d.count())) return;
    await page.keyboard.press("Escape");
    await wait(400);
  }
}
async function go(path) {
  await closeDialogs();
  await page.goto(BASE + path);
  await idle();
}

async function signIn() {
  await page.goto(BASE + "/anmelden");
  await field("loginUser").waitFor();
  await wait(800);
  await field("loginUser").fill(ADMIN.login);
  await field("loginPassword").fill(ADMIN.password);
  await page.getByRole("button", { name: "Anmelden", exact: true }).click();
  await page.waitForURL((u) => !u.pathname.startsWith("/anmelden"), { timeout: 20000 });
  await idle();
}

const phase = process.argv[2] ?? "alle";
if (phase === "einrichtung") {
  const code = readFileSync(join(WORK, "setup-code"), "utf8").trim();
  await page.goto(BASE + "/einrichtung");
  await page.getByLabel("Einrichtungscode").waitFor();
  await wait(800);
  await page.getByLabel("Einrichtungscode").fill(code);
  await page.getByLabel("Firmenname").fill("Estrichbau Muster GmbH");
  await page.getByLabel("Name", { exact: true }).fill("Claudia Berger");
  await page.getByLabel("Anmeldename").fill(ADMIN.login);
  await page.getByLabel("E-Mail (optional)").fill("c.berger@estrichbau-muster.de");
  await page.getByLabel("Passwort", { exact: true }).fill(ADMIN.password);
  await shot("einrichtung");
  await page.getByRole("button", { name: "Einrichten und anmelden" }).click();
  await page.waitForURL((u) => u.pathname.startsWith("/start"), { timeout: 30000 });
  await browser.close();
  process.exit(0);
}

const wanted = new Set(process.argv.slice(2).filter((a) => a !== "alle"));
const scenes = (await import(join(here, "szenen.mjs"))).default;
await page.goto(BASE + "/anmelden");
await field("loginUser").waitFor();
await wait(600);
await shot("anmeldung");
await signIn();
const helpers = { page, go, idle, wait, shot, mark, unmark, field, dialog, closeDialogs, signIn, ADMIN, BASE };
let failed = 0;
for (const [name, run] of Object.entries(scenes)) {
  if (wanted.size && !wanted.has(name)) continue;
  try {
    await run(helpers);
  } catch (error) {
    failed++;
    console.log(`FEHLER ${name}: ${String(error.message ?? error).split("\n")[0]}`);
    await page.screenshot({ path: join(WORK, `fehler-${name}.png`) }).catch(() => undefined);
    await closeDialogs().catch(() => undefined);
  }
}
await browser.close();
process.exit(failed ? 1 : 0);
