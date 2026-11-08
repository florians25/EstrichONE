/**
 * Verwaltung: alle Verwaltungsbereiche mit Gruppe, Pfad, Recht, Kurzbeschreibung und Suchbegriffen.
 * Eine Quelle für Seitennavigation (Gruppen), Verwaltungsübersicht (/verwaltung, mit Suche) und globale Suche.
 * Ohne UI5-Abhängigkeit (mit Vitest geprüft).
 */

export interface AdminGroup {
	key: string;
	title: string;
	description: string;
}

export interface AdminArea {
	/** Navigationsschlüssel (Text: i18n „nav.<key>“, hier zusätzlich als title). */
	key: string;
	group: string;
	title: string;
	path: string;
	icon: string;
	permission?: string;
	description: string;
	/** Weitere Suchbegriffe (Kleinschreibung egal). */
	keywords?: string;
}

/** Gruppen der Verwaltung in Navigationsreihenfolge (Texte: i18n „nav.<key>“). */
export const adminGroups: AdminGroup[] = [
	{ key: "groupAdminOrg", title: "Organisation", description: "Firmendaten, Vorgaben für alle Benutzer, Stammdaten und Module" },
	{ key: "groupAdminUsers", title: "Benutzer & Geräte", description: "Konten, Rollen und Rechte, registrierte Geräte, Helpdesk, Fehler & Wünsche" },
	{ key: "groupAdminData", title: "Datensicherung & Archiv", description: "Backup Center, Backup-Ziele, revisionssicheres Archiv und Aktenarchiv" },
	{ key: "groupAdminSystem", title: "System & Betrieb", description: "Überwachung, Speicher, Hinweise und Wartung, Protokoll" },
	{ key: "groupAdminIntegrations", title: "Integrationen", description: "E-Mail-Versand und Pushnachrichten" },
];

export const adminAreas: AdminArea[] = [
	{ key: "company", group: "groupAdminOrg", title: "Systemeinstellungen", path: "/system/firmendaten", icon: "sap-icon://building", permission: "settings.manage",
		description: "Firmendaten und Logo, Nummernkreis, Vorgaben für Bauakten, Freigaben, Anmeldung, Darstellung und PDF-Layout",
		keywords: "firma firmendaten logo briefkopf nummernkreis aktennummer bauakte freigaben anmeldung sitzung zwei-faktor 2fa darstellung theme karte pdf layout fußzeile" },
	{ key: "masterData", group: "groupAdminOrg", title: "Stammdaten", path: "/verwaltung/stammdaten", icon: "sap-icon://database", permission: "settings.manage",
		description: "Auswahllisten, Formulare für Basisdaten und Erstbegehung, Textbausteine, Ordner- und Bauakten-Vorlagen",
		keywords: "wertelisten auswahllisten formulare erstbegehung basisdaten textbausteine vorlagen ordnervorlagen" },
	{ key: "modules", group: "groupAdminOrg", title: "Module", path: "/system/firmendaten?bereich=module", icon: "sap-icon://puzzle", permission: "settings.manage",
		description: "Material und Chat ein- oder ausschalten; Löschfrist und Datenschutz des Chats",
		keywords: "module material chat betriebsrat löschfrist" },
	{ key: "users", group: "groupAdminUsers", title: "Benutzer", path: "/verwaltung/benutzer", icon: "sap-icon://employee", permission: "users.manage",
		description: "Konten anlegen, sperren, Passwort und Zwei-Faktor zurücksetzen, Zugangsbriefe",
		keywords: "konten benutzer passwort zugangsbrief einmalpasswort sperren 2fa" },
	{ key: "roles", group: "groupAdminUsers", title: "Rollen und Rechte", path: "/verwaltung/rollen", icon: "sap-icon://key-user-settings", permission: "users.manage",
		description: "Rollen mit fein abgestuften Rechten, Rechtematrix bearbeiten, eigene Rollen anlegen",
		keywords: "rollen rechte rechtematrix berechtigungen" },
	{ key: "devices", group: "groupAdminUsers", title: "Geräte", path: "/it-center/geraete", icon: "sap-icon://iphone", permission: "devices.read",
		description: "iPhones, iPads, Android-Geräte und Windows-Arbeitsplätze einrichten und sperren",
		keywords: "geräte iphone ipad android windows qr-code einrichten pool" },
	{ key: "helpdesk", group: "groupAdminUsers", title: "Helpdesk", path: "/it-center/helpdesk", icon: "sap-icon://headset",
		description: "Tickets der Mitarbeiter: Probleme melden, beantworten, erledigen",
		keywords: "tickets support hilfe problem" },
	{ key: "feedbackAdmin", group: "groupAdminUsers", title: "Fehler & Wünsche", path: "/it-center/meldungen", icon: "sap-icon://quality-issue",
		permission: "feedback.manage", description: "Fehlermeldungen und Funktionswünsche der Benutzer: prüfen, einplanen, beantworten",
		keywords: "feedback fehler bug wunsch funktionswunsch idee meldung verbesserung" },
	{ key: "backup", group: "groupAdminData", title: "Sicherung", path: "/system/sicherung", icon: "sap-icon://upload-to-cloud", permission: "backup.manage",
		description: "Backup Center: tägliche Vollsicherung, Zeitplan, Aufbewahrung, Wiederherstellung und Notfallblatt",
		keywords: "backup center sicherung wiederherstellen restore notfallblatt zeitplan aufbewahrung" },
	{ key: "backupTargets", group: "groupAdminData", title: "Backup-Ziele", path: "/system/backup-ziele", icon: "sap-icon://it-host", permission: "backup.manage",
		description: "Zusätzliche Sicherung auf NAS, PC oder Raspberry Pi; nur anhängend, Schutz vor Verschlüsselungstrojanern",
		keywords: "backup-ziel nas kombiserver koppeln offsite" },
	{ key: "archive", group: "groupAdminData", title: "Archiv", path: "/system/archiv", icon: "sap-icon://inbox", permission: "settings.manage",
		description: "Revisionssichere Aufbewahrung (GoBD): Archivserver, Fristen je Dokumentart, Nachweise",
		keywords: "archiv gobd worm aufbewahrung fristen archivserver" },
	{ key: "dossiers", group: "groupAdminData", title: "Aktenarchiv", path: "/system/aktenarchiv", icon: "sap-icon://folder-blank", permission: "settings.manage",
		description: "Komplett ausgelagerte Bauakten: liegen dauerhaft auf dem Archivserver, beim Abruf 5 Tage lesbar",
		keywords: "aktenarchiv bauakte archivieren abrufen auslagern" },
	{ key: "monitoring", group: "groupAdminSystem", title: "Überwachung", path: "/system/ueberwachung", icon: "sap-icon://line-chart", permission: "backup.manage",
		description: "Zustand von Server, Datenbank, Backup- und Archivserver auf einen Blick",
		keywords: "überwachung monitoring zustand server cpu speicherplatz" },
	{ key: "storage", group: "groupAdminSystem", title: "Speicher", path: "/system/firmendaten?bereich=speicher", icon: "sap-icon://it-system", permission: "settings.manage",
		description: "Fotos und Dokumente speicherschonend ablegen, Belegung, Nachverdichten",
		keywords: "speicher belegung fotos jpeg qualität pdf verdichten platz" },
	{ key: "offload", group: "groupAdminSystem", title: "Speicher-Auslagerung", path: "/system/auslagerung", icon: "sap-icon://upload-to-cloud",
		permission: "settings.manage", description: "Wird der Platz knapp, Originale verschlüsselt auf den Storage (Kombiserver) auslagern, Rückholen, Belegung",
		keywords: "auslagerung auslagern storage kombiserver speicher knapp platz schwelle zurückholen originale" },
	{ key: "notices", group: "groupAdminSystem", title: "Hinweise & Wartung", path: "/system/hinweise", icon: "sap-icon://message-warning", permission: "settings.manage",
		description: "Ankündigungen an alle Benutzer und Wartungsmodus dieser Instanz",
		keywords: "hinweise ankündigung banner wartung wartungsmodus" },
	{ key: "audit", group: "groupAdminSystem", title: "Protokoll", path: "/system/protokoll", icon: "sap-icon://activities", permission: "audit.read",
		description: "Protokoll aller Änderungen (wer, wann, was); Einträge sind unveränderbar",
		keywords: "protokoll audit log ereignisse nachvollziehen" },
	{ key: "mail", group: "groupAdminIntegrations", title: "E-Mail", path: "/system/e-mail", icon: "sap-icon://email", permission: "settings.manage",
		description: "E-Mail-Konto (SMTP) für Benachrichtigungen und Zugangsbriefe, Vorlagen, Versandprotokoll",
		keywords: "e-mail mail smtp vorlagen versandprotokoll testmail" },
	{ key: "push", group: "groupAdminIntegrations", title: "Pushnachrichten", path: "/system/firmendaten?bereich=benachrichtigungen", icon: "sap-icon://bell",
		permission: "settings.manage", description: "Welche Benachrichtigungen per E-Mail gehen, Web Push und Apple Push (APNs)",
		keywords: "push benachrichtigungen apns web push ios" },
];

/** Systemeinstellungen-Reiter, die in der Navigation als eigener Eintrag erscheinen (Query „bereich“). */
export const companyTabKeys: Record<string, string> = { module: "modules", speicher: "storage", benachrichtigungen: "push" };

const fold = (value: string) => value.toLocaleLowerCase("de-DE").normalize("NFD").replace(/[̀-ͯ]/g, "").replace(/ß/g, "ss");

/** Treffer für die Suche: alle Wörter müssen in Titel, Beschreibung, Gruppe oder Suchbegriffen vorkommen. */
export function matchesArea(area: AdminArea, query: string): boolean {
	const words = fold(query).split(/\s+/).filter(Boolean);
	if (!words.length) return true;
	const group = adminGroups.find((g) => g.key === area.group)?.title ?? "";
	const text = fold([area.title, area.description, group, area.keywords ?? ""].join(" "));
	return words.every((w) => text.includes(w));
}

/** Sichtbare Bereiche (Rechte) gruppiert, gefiltert nach Suchtext. */
export function adminSections(has: (permission: string) => boolean, query = ""): (AdminGroup & { areas: AdminArea[] })[] {
	return adminGroups
		.map((g) => ({ ...g, areas: adminAreas.filter((a) => a.group === g.key && (!a.permission || has(a.permission)) && matchesArea(a, query)) }))
		.filter((g) => g.areas.length > 0);
}
