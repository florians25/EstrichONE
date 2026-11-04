// Szenen der Handbuch-Bilder: Name → Ablauf. Jede Szene beginnt angemeldet als Administratorin (Claudia Berger).
// Dateinamen unter docs/handbuch/bilder/ (ohne Endung) legt h.shot() fest. Einzelne Szenen: node bilder.mjs start bauakten …

const visible = (l) => l.filter({ visible: true }).first();
const tab = (page, name) => visible(page.getByRole("tab", { name, exact: true }));
const button = (page, name, exact = true) => visible(page.getByRole("button", { name, exact }));
const byId = (page, suffix) => visible(page.locator(`[id$="${suffix}"]`));

async function openAkte(h, title, collapse = true, list = "/bauakten") {
  await h.go(list);
  await visible(h.page.getByRole("link", { name: new RegExp(title) })).click();
  await h.idle();
  await h.wait(900);
  if (collapse) await collapseHeader(h);
}

/** Kopf der Objektseite einklappen, damit der Abschnitt mehr Platz hat. */
async function collapseHeader(h) {
  const toggle = visible(h.page.locator('[id$="-OPHeaderContent-collapseBtn"], [id$="collapseBtn"]'));
  if (await toggle.isVisible().catch(() => false)) {
    await toggle.click();
    await h.wait(500);
  }
}

async function section(h, name) {
  const t = tab(h.page, name);
  if (await t.isVisible().catch(() => false)) {
    await t.click();
  } else {
    await button(h.page, /^\+\d+$/, false).click();
    await visible(h.page.locator("[role=menuitem], [role=option], .sapMITBSelectList li").filter({ hasText: name })).click();
  }
  await h.idle();
}

async function cockpitWeek(h) {
  await h.go("/planung/cockpit");
  await h.wait(1200);
}

export default {
  async start(h) {
    const p = h.page;
    await h.go("/start");
    await h.wait(1500);
    await h.mark([
      { target: byId(p, "--globalSearch"), n: 1, pos: "bottom" },
      { target: byId(p, "--newBauakteButton"), n: 2, pos: "bottom" },
      { target: byId(p, "shellBar-notifications"), n: 3, pos: "bottom", frame: false },
      { target: byId(p, "--chatButton"), n: 4, pos: "bottom", frame: false },
      { target: byId(p, "--phoneButton"), n: 5, pos: "bottom", frame: false },
      { target: byId(p, "--profileAvatar"), n: 6, pos: "bottom", frame: false },
      { target: byId(p, "--sideNavigation"), n: 7, pos: "right", frame: false },
      { target: visible(p.locator(".sapFCard")), n: 8, pos: "left", frame: false },
    ]);
    await h.shot("start");
  },
  async suche(h) {
    await h.go("/start");
    const input = h.page.locator('input[id$="--globalSearch-I"]');
    await input.click();
    await input.pressSequentially("sonn", { delay: 80 });
    await h.wait(1500);
    await h.shot("suche", { clip: { x: 300, y: 0, width: 900, height: 420 } });
    await h.page.keyboard.press("Escape");
  },
  async benutzermenue(h) {
    await h.go("/start");
    await byId(h.page, "--profileAvatar").click();
    await h.wait(800);
    await h.shot("benutzermenue", { clip: { x: 990, y: 0, width: 450, height: 420 } });
    await h.page.keyboard.press("Escape");
  },
  async einstellungen(h) {
    await h.go("/start");
    await byId(h.page, "--profileAvatar").click();
    await visible(h.page.getByRole("menuitem", { name: "Einstellungen" })).click();
    await h.wait(1200);
    await h.shot("einstellungen-dialog");
    await h.closeDialogs();
  },
  async sicherheit(h) {
    await h.go("/konto/sicherheit");
    await h.shot("sicherheit");
    await button(h.page, "Einrichten").click();
    await h.wait(800);
    const pw = h.dialog().locator("input[type=password]").first();
    if (await pw.isVisible().catch(() => false)) {
      await pw.fill(h.ADMIN.password);
      await visible(h.dialog().getByRole("button", { name: /Weiter|Fortfahren|Einrichten|QR/ })).click().catch(() => undefined);
      await h.wait(1200);
    }
    await h.shot("zwei-faktor-einrichten");
    await h.closeDialogs();
  },
  async bauakten(h) {
    await h.go("/bauakten");
    const p = h.page;
    await h.mark([
      { target: visible(p.locator('[id*="search"] input, .sapMSF')).locator(".."), n: 1, pos: "left", frame: false },
      { target: visible(p.getByRole("tablist")), n: 2, pos: "left", frame: false },
      { target: button(p, "Neue Bauakte").last(), n: 3, pos: "bottom", frame: false },
    ]).catch(() => undefined);
    await h.shot("bauakten-liste");
  },
  async altakten(h) {
    await h.go("/bauakten?status=old");
    await h.wait(800);
    await h.shot("altakten");
  },
  async neueBauakte(h) {
    const p = h.page;
    await h.go("/start");
    await byId(p, "--newBauakteButton").click();
    await h.wait(1200);
    const fill = async (id, value) => {
      await p.locator(`input[id$="${id}-inner"]`).fill(value);
      await h.wait(250);
    };
    await fill("newCustomerName", "Petra Oberländer");
    await fill("newCustomerStreet", "Lindenweg 4");
    await fill("newCustomerPostalCode", "84144");
    await fill("newCustomerCity", "Geisenhausen");
    await fill("newCustomerPhone", "08743 912 34");
    await visible(h.dialog().locator(".sapMWizardStepTitle, .sapMTitle")).click().catch(() => undefined);
    await h.wait(600);
    await h.shot("bauakte-neu-kunde");
    await button(p, "Weiter").click();
    await h.wait(1500);
    await h.shot("bauakte-neu-dublette");
    await visible(p.locator('[id$="newCustomerDuplicates"] li')).click().catch(() => undefined);
    await h.wait(800);
    const use = button(p, "Diesen Kunden verwenden");
    if (await use.isVisible().catch(() => false)) await use.click();
    await h.wait(600);
    await button(p, "Weiter").click();
    await h.wait(1000);
    await p.locator('input[id$="newTitle-inner"]').fill("Einfamilienhaus Oberländer – Anbau Wintergarten");
    await h.wait(500);
    await h.shot("bauakte-neu-bauvorhaben");
    await h.closeDialogs();
  },
  async bauakteUebersicht(h) {
    const p = h.page;
    await openAkte(h, "Sonnenhof", false);
    await h.mark([
      { target: visible(p.locator(".sapUxAPObjectPageHeaderTitle, .sapFDynamicPageTitle")), n: 1, pos: "left", frame: false },
      { target: visible(p.locator(".sapMObjStatus, .sapMGenericTag").first()), n: 2, pos: "left", frame: false },
      { target: visible(p.getByRole("tablist")), n: 3, pos: "left", frame: false },
      { target: button(p, "Bearbeiten"), n: 4, pos: "bottom", frame: false },
    ]).catch(() => undefined);
    await h.shot("bauakte-uebersicht");
  },
  async bauakteAktionen(h) {
    const p = h.page;
    await openAkte(h, "Sonnenhof", false);
    await visible(p.locator('[id*="overflowButton"]')).click();
    await h.wait(500);
    await visible(p.getByRole("button", { name: /Aktionen/ })).click();
    await h.wait(800);
    await h.shot("bauakte-aktionen");
    await p.keyboard.press("Escape");
  },
  async bauakteKontakte(h) {
    await openAkte(h, "Sonnenhof");
    await section(h, "Kontakte");
    await h.shot("bauakte-kontakte");
  },
  async bauakteDokumente(h) {
    await openAkte(h, "Sonnenhof");
    await section(h, "Dokumente");
    await h.shot("bauakte-dokumente");
  },
  async bauakteFotos(h) {
    const p = h.page;
    await openAkte(h, "Sonnenhof");
    await section(h, "Fotos & Videos");
    await h.wait(1500);
    await h.shot("bauakte-fotos");
    await visible(p.locator(".sapMTile, .eoPhotoTile, img").filter({ has: p.locator("xpath=.") })).click().catch(() => undefined);
  },
  async bauakteDoku(h) {
    await openAkte(h, "Sonnenhof");
    await section(h, "Dokumentation");
    await h.shot("bauakte-dokumentation");
    await visible(h.page.getByRole("tab", { name: /Prüfungen/ })).click().catch(() => undefined);
    await h.idle();
    await h.shot("bauakte-pruefungen");
  },
  async bautagebuchDialog(h) {
    await openAkte(h, "Sonnenhof");
    await section(h, "Dokumentation");
    await button(h.page, "Tagesbericht erfassen").click();
    await h.wait(1000);
    await h.shot("bautagebuch-dialog");
    await h.closeDialogs();
  },
  async erstbegehung(h) {
    await openAkte(h, "Oberländer");
    await section(h, "Dokumentation");
    await visible(h.page.getByRole("tab", { name: /Prüfungen/ })).click();
    await h.idle();
    await visible(h.page.getByRole("button", { name: /Erstbegehung/ })).click();
    await h.wait(1500);
    await h.shot("erstbegehung-dialog");
    await h.closeDialogs();
  },
  async abnahme(h) {
    const p = h.page;
    await openAkte(h, "Oberländer");
    await section(h, "Dokumentation");
    await visible(p.getByRole("tab", { name: /Abnahmen/ })).click();
    await h.idle();
    await visible(p.getByRole("button", { name: /Abnahme/ })).click();
    await h.wait(1500);
    await h.shot("abnahme-dialog");
    for (let i = 0; i < 5; i++) {
      const next = button(p, "Weiter");
      if (!(await next.isVisible().catch(() => false)) || !(await next.isEnabled().catch(() => false))) break;
      await next.click();
      await h.wait(700);
    }
    const pads = p.locator(".sapMDialog canvas").filter({ visible: true });
    for (let i = 0; i < (await pads.count()); i++) {
      const box = await pads.nth(i).boundingBox();
      if (!box) continue;
      await p.mouse.move(box.x + 30, box.y + box.height * 0.7);
      await p.mouse.down();
      for (let k = 0; k <= 24; k++) {
        await p.mouse.move(box.x + 30 + k * (box.width - 60) / 24, box.y + box.height * (0.5 + 0.25 * Math.sin(k / 2.2 + i)), { steps: 2 });
      }
      await p.mouse.up();
    }
    await h.wait(500);
    await h.shot("abnahme-unterschrift");
    await h.closeDialogs();
  },
  async kinderhausAbnahme(h) {
    await openAkte(h, "Kinderhaus", true, "/bauakten?status=old");
    await section(h, "Dokumentation");
    await visible(h.page.getByRole("tab", { name: /Abnahmen/ })).click();
    await h.idle();
    await h.shot("bauakte-abnahmen");
  },
  async bauakteRaeume(h) {
    await openAkte(h, "Sonnenhof");
    await section(h, "Räume & Flächen");
    await h.shot("bauakte-raeume");
  },
  async bauakteAufgaben(h) {
    await openAkte(h, "Sonnenhof");
    await section(h, "Aufgaben & Mängel");
    await h.shot("bauakte-aufgaben");
  },
  async bauakteEinsaetze(h) {
    await openAkte(h, "Sonnenhof");
    await section(h, "Einsätze");
    await h.shot("bauakte-einsaetze");
  },
  async bauakteMaterial(h) {
    await openAkte(h, "Sonnenhof");
    await section(h, "Material");
    await h.shot("bauakte-material");
  },
  async bauakteVerlauf(h) {
    await openAkte(h, "Sonnenhof");
    await section(h, "Notizen & Verlauf");
    await h.shot("bauakte-verlauf");
  },
  async kunde(h) {
    await h.go("/kunden");
    await visible(h.page.getByText("Bauträger Sonnenhof GmbH")).click();
    await h.idle();
    await h.shot("kunde-detail");
  },
  async cockpit(h) {
    const p = h.page;
    await cockpitWeek(h);
    await h.mark([
      { target: visible(p.locator(".eoCkBacklog, [id$='--backlog']")), n: 1, pos: "right", frame: false },
      { target: visible(p.getByRole("button", { name: /Konflikte/ })), n: 2, pos: "bottom" },
      { target: visible(p.getByRole("button", { name: /freigeben/ })), n: 3, pos: "bottom" },
      { target: visible(p.getByRole("button", { name: "Woche", exact: true })), n: 4, pos: "bottom", frame: false },
    ]).catch(() => undefined);
    await h.shot("cockpit");
  },
  async cockpitFreigabe(h) {
    await cockpitWeek(h);
    await visible(h.page.getByRole("button", { name: /freigeben/ })).click();
    await h.wait(1200);
    await h.shot("cockpit-freigabe");
    await h.closeDialogs();
  },
  async cockpitKonflikte(h) {
    await cockpitWeek(h);
    await visible(h.page.getByRole("button", { name: /Konflikte/ })).click();
    await h.wait(1000);
    await h.shot("cockpit-konflikte");
    await h.closeDialogs();
    await h.page.keyboard.press("Escape");
  },
  async cockpitTasten(h) {
    await cockpitWeek(h);
    await h.page.locator("body").press("?");
    await h.wait(900);
    await h.shot("cockpit-tastatur");
    await h.closeDialogs();
  },
  async helpdesk(h) {
    await h.go("/it-center/helpdesk");
    await visible(h.page.getByText("Anmeldung am Pool-iPad")).click();
    await h.idle();
    await h.shot("helpdesk");
  },
  async chat(h) {
    const p = h.page;
    await h.go("/chat");
    const ok = button(p, "Verstanden");
    if (await ok.isVisible().catch(() => false)) await ok.click();
    await h.wait(500);
    await visible(p.getByText("Baustelle Sonnenhof")).click();
    await h.idle();
    await h.shot("chat");
  },
  async benutzerAnlegen(h) {
    await h.go("/verwaltung/benutzer");
    await button(h.page, "Benutzer anlegen").click();
    await h.wait(1000);
    await h.shot("benutzer-anlegen");
    await h.closeDialogs();
  },
  async geraetEinrichten(h) {
    await h.go("/it-center/geraete");
    await button(h.page, "Gerät einrichten").click();
    await h.wait(1200);
    await h.shot("geraet-einrichten");
    const next = visible(h.dialog().getByRole("button", { name: /Weiter|Code erzeugen|QR/ }));
    if (await next.isVisible().catch(() => false)) {
      await next.click();
      await h.wait(1500);
      await h.shot("geraet-qr");
    }
    await h.closeDialogs();
  },
  async systemeinstellungen(h) {
    const p = h.page;
    // Bereiche links in der Liste; direkt über die Adresse (?bereich=…) öffnen.
    for (const [key, file] of [["firma", "systemeinstellungen"], ["bauakte", "einstellungen-bauakten"], ["sitzungen", "einstellungen-anmeldung"],
      ["benachrichtigungen", "einstellungen-push"], ["speicher", "einstellungen-speicher"], ["module", "einstellungen-module"], ["system", "einstellungen-system"]]) {
      await h.go(`/system/firmendaten?bereich=${key}`);
      await h.idle();
      if (key === "benachrichtigungen") await visible(p.getByText("Apple Push (APNs) für die iOS-App")).scrollIntoViewIfNeeded().catch(() => undefined);
      await h.shot(file);
    }
  },
  async sicherung(h) {
    const p = h.page;
    await h.go("/system/sicherung");
    await h.shot("sicherung");
    for (const [name, file] of [["Sicherungen", "sicherung-staende"], ["Zeitplan und Meldungen", "sicherung-zeitplan"], ["Notfall", "sicherung-notfall"]]) {
      await visible(p.getByRole("tab", { name: new RegExp(name) })).click();
      await h.idle();
      await h.shot(file);
    }
    await visible(p.getByRole("tab", { name: /Sicherungen/ })).click();
    await h.idle();
    await button(p, "Wiederherstellen").click();
    await h.wait(1200);
    await h.shot("sicherung-wiederherstellen");
    await h.closeDialogs();
  },
  async wartung(h) {
    await h.go("/system/hinweise");
    await h.shot("hinweise-wartung");
    await visible(h.page.getByRole("button", { name: /Wartung planen/ })).click();
    await h.wait(1000);
    await h.shot("wartung-planen");
    await h.closeDialogs();
  },
  async seiten(h) {
    for (const [path, name] of [["/tagesuebersicht", "tagesuebersicht"], ["/bauakten/karte", "baustellenkarte"], ["/dokumente", "dokumente"],
      ["/dokumente?reiter=freigaben", "freigaben"], ["/anfragen", "anfragen"], ["/kunden", "kunden"], ["/anrufe", "anrufe"],
      ["/planung", "kolonnenplanung"], ["/planung/teamplaner", "teamplaner"], ["/planung/kalender", "kalender"],
      ["/planung/arbeitsplan", "arbeitsplan"], ["/planung/kolonnen", "kolonnen"], ["/planung/geraete", "geraete-fahrzeuge"],
      ["/personal", "personal"], ["/personal/urlaub?reiter=plan", "urlaubsplan"], ["/personal/urlaub?reiter=antraege", "urlaubsantraege"],
      ["/personal/urlaub", "urlaub"], ["/bestellungen", "bestellungen"], ["/material", "material"], ["/lieferanten", "lieferanten"],
      ["/benachrichtigungen", "benachrichtigungen"], ["/verwaltung/nachrichten", "nachrichten"],
      ["/verwaltung", "verwaltung"], ["/verwaltung/benutzer", "benutzer"], ["/verwaltung/rollen", "rollen"], ["/verwaltung/stammdaten", "stammdaten"], ["/it-center/geraete", "geraete"],
      ["/system/backup-ziele", "backup-ziele"], ["/system/archiv", "archiv"], ["/system/e-mail", "e-mail"], ["/system/aktenarchiv", "aktenarchiv"],
      ["/system/ueberwachung", "ueberwachung"], ["/system/protokoll", "protokoll"]]) {
      await h.go(path);
      await h.wait(600);
      await h.shot(name);
    }
  },
};
