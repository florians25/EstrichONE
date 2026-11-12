/** Seiten der Anwendung mit Pfad, View, Recht und Navigationseintrag. */
import type { RouteDef } from "./router";
import { adminAreas, adminGroups, companyTabKeys } from "./adminCatalog";

export const routes: RouteDef[] = [
	{ name: "login", pattern: "/anmelden", view: "auth.Login", public: true, title: "login.title" },
	{ name: "setup", pattern: "/einrichtung", view: "auth.Setup", public: true, title: "setup.title" },
	{ name: "setupAlias", pattern: "/einrichten", view: "auth.Setup", public: true, title: "setup.title" },
	{ name: "acceptancePublic", pattern: "/abnahme/:token", view: "public.Acceptance", public: true, title: "acceptance.title" },
	{ name: "orderPrint", pattern: "/bestellungen/:id/druck", view: "materials.OrderPrint", bare: true, permission: "materials.read", title: "orders.print" },
	{ name: "report", pattern: "/bauakten/:id/bericht", view: "bauakte.Report", bare: true, permission: "bauakten.read", title: "report.title" },
	{ name: "start", pattern: "/start", view: "Start", nav: "start", title: "nav.start" },
	{ name: "inbox", pattern: "/benachrichtigungen", view: "Inbox", nav: "inbox", title: "nav.inbox" },
	{ name: "chat", pattern: "/chat", view: "Chat", permission: "chat.use", nav: "chat", title: "nav.chat" },
	{ name: "chatConversation", pattern: "/chat/:id", view: "Chat", permission: "chat.use", nav: "chat", title: "nav.chat" },
	{ name: "messages", pattern: "/verwaltung/nachrichten", view: "admin.Messages", permission: "notifications.send", nav: "messages", title: "nav.messages" },
	{ name: "password", pattern: "/konto/passwort", view: "auth.ChangePassword", title: "password.title" },
	{ name: "security", pattern: "/konto/sicherheit", view: "auth.Security", title: "security.title" },
	{ name: "today", pattern: "/tagesuebersicht", view: "Today", permission: "bauakten.read", nav: "today", title: "nav.today" },
	{ name: "siteMap", pattern: "/bauakten/karte", view: "SiteMap", permission: "bauakten.read", nav: "siteMap", title: "nav.siteMap" },
	{ name: "bauakten", pattern: "/bauakten", view: "bauakte.Fcl", permission: "bauakten.read", nav: "bauakten", title: "nav.bauakten" },
	{ name: "bauakte", pattern: "/bauakten/:id", view: "bauakte.Fcl", permission: "bauakten.read", nav: "bauakten", title: "nav.bauakten" },
	{ name: "bauakteItem", pattern: "/bauakten/:id/:section/:item", view: "bauakte.Fcl", permission: "bauakten.read", nav: "bauakten", title: "nav.bauakten" },
	{ name: "documents", pattern: "/dokumente", view: "Documents", permission: "bauakten.read", nav: "documents", title: "nav.documents" },
	{ name: "calls", pattern: "/anrufe", view: "office.Calls", permission: "contacts.read", nav: "calls", title: "nav.calls" },
	{ name: "inquiries", pattern: "/anfragen", view: "office.Inquiries", permission: "contacts.read", nav: "inquiries", title: "nav.inquiries" },
	{ name: "orders", pattern: "/bestellungen", view: "materials.Orders", permission: "materials.read", nav: "orders", title: "nav.orders" },
	{ name: "materials", pattern: "/material", view: "materials.Materials", permission: "materials.read", nav: "materials", title: "nav.materials" },
	{ name: "suppliers", pattern: "/lieferanten", view: "materials.Suppliers", permission: "materials.read", nav: "suppliers", title: "nav.suppliers" },
	{ name: "customers", pattern: "/kunden", view: "Customers", permission: "contacts.read", nav: "customers", title: "nav.customers" },
	{ name: "customer", pattern: "/kunden/:id", view: "Customer", permission: "contacts.read", nav: "customers", title: "nav.customers" },
	{ name: "cockpit", pattern: "/planung/cockpit", view: "planning.Cockpit", permission: "planning.read", nav: "cockpit", title: "nav.cockpit" },
	{ name: "planning", pattern: "/planung", view: "planning.Planning", permission: "planning.read", nav: "planning", title: "nav.planning" },
	{ name: "calendar", pattern: "/planung/kalender", view: "planning.Calendar", permission: "planning.read", nav: "calendar", title: "nav.calendar" },
	{ name: "teamPlanner", pattern: "/planung/teamplaner", view: "planning.TeamPlanner", permission: "planning.read", nav: "teamPlanner", title: "nav.teamPlanner" },
	// „Wer fährt wohin“ gibt es nur noch in der App (API bleibt); Links aus Pushnachrichten führen ins Cockpit.
	{ name: "dispatch", pattern: "/planung/wer-faehrt-wohin", view: "planning.Cockpit", permission: "planning.read", nav: "cockpit", title: "nav.cockpit",
		redirect: (query) => `/planung/cockpit${query.get("tag") ? `?ansicht=day&datum=${encodeURIComponent(query.get("tag")!)}` : ""}` },
	{ name: "vacation", pattern: "/personal/urlaub", view: "staff.Vacation", permission: "absence.request", nav: "vacation", title: "nav.vacation" },
	{ name: "staff", pattern: "/personal", view: "staff.Staff", permission: "hr.read", nav: "staff", title: "nav.staff" },
	{ name: "employee", pattern: "/personal/:id", view: "staff.Employee", permission: "hr.read", nav: "staff", title: "nav.staff" },
	{ name: "dayPlan", pattern: "/planung/tageseinteilung", view: "planning.Planning", permission: "planning.read", nav: "planning", title: "nav.planning" },
	{ name: "workPlan", pattern: "/planung/arbeitsplan", view: "planning.WorkPlan", permission: "planning.read", nav: "workPlan", title: "nav.workPlan" },
	{ name: "crews", pattern: "/planung/kolonnen", view: "planning.Resources", permission: "planning.read", nav: "crews", title: "nav.crews" },
	{ name: "devices2", pattern: "/planung/geraete", view: "planning.Resources", permission: "planning.read", nav: "vehicles", title: "nav.vehicles" },
	{ name: "resources", pattern: "/planung/ressourcen", view: "planning.Resources", permission: "planning.read", nav: "crews", title: "nav.crews" },
	{ name: "adminHub", pattern: "/verwaltung", view: "admin.Overview", nav: "adminHub", title: "nav.adminHub" },
	{ name: "masterData", pattern: "/verwaltung/stammdaten", view: "admin.MasterData", permission: "bauakten.read", nav: "masterData", title: "nav.masterData" },
	{ name: "roles", pattern: "/verwaltung/rollen", view: "admin.Roles", permission: "users.manage", nav: "roles", title: "nav.roles" },
	{ name: "users", pattern: "/verwaltung/benutzer", view: "admin.Users", permission: "users.manage", nav: "users", title: "nav.users" },
	{ name: "devices", pattern: "/it-center/geraete", view: "admin.Devices", permission: "devices.read", nav: "devices", title: "nav.devices" },
	{ name: "appUpdates", pattern: "/it-center/app-updates", view: "admin.AppUpdates", permission: "devices.read", nav: "appUpdates", title: "nav.appUpdates" },
	{ name: "helpdeskSettings", pattern: "/it-center/helpdesk-einstellungen", view: "admin.HelpdeskSettings", permission: "helpdesk.agent", nav: "helpdeskSettings",
		title: "nav.helpdeskSettings" },
	{ name: "knowledge", pattern: "/wissen", view: "Knowledge", nav: "knowledge", title: "nav.knowledge" },
	{ name: "knowledgeArticle", pattern: "/wissen/:id", view: "Knowledge", nav: "knowledge", title: "nav.knowledge" },
	{ name: "helpdesk", pattern: "/it-center/helpdesk", view: "admin.Helpdesk", nav: "helpdesk", title: "nav.helpdesk" },
	{ name: "ticket", pattern: "/it-center/helpdesk/:id", view: "admin.Helpdesk", nav: "helpdesk", title: "nav.helpdesk" },
	{ name: "feedbackAdmin", pattern: "/it-center/meldungen", view: "Feedback", permission: "feedback.manage", nav: "feedbackAdmin", title: "nav.feedbackAdmin" },
	{ name: "feedbackAdminItem", pattern: "/it-center/meldungen/:id", view: "Feedback", permission: "feedback.manage", nav: "feedbackAdmin", title: "nav.feedbackAdmin" },
	{ name: "myFeedback", pattern: "/meine-meldungen", view: "Feedback", nav: "myFeedback", title: "nav.myFeedback" },
	{ name: "myFeedbackItem", pattern: "/meine-meldungen/:id", view: "Feedback", nav: "myFeedback", title: "nav.myFeedback" },
	{ name: "backup", pattern: "/system/sicherung", view: "admin.Backup", permission: "backup.manage", nav: "backup", title: "nav.backup" },
	{ name: "backupTargets", pattern: "/system/backup-ziele", view: "admin.BackupTargets", permission: "backup.manage", nav: "backupTargets", title: "nav.backupTargets" },
	{ name: "archive", pattern: "/system/archiv", view: "admin.Archive", permission: "settings.manage", nav: "archive", title: "nav.archive" },
	{ name: "offload", pattern: "/system/auslagerung", view: "admin.Offload", permission: "settings.manage", nav: "offload", title: "nav.offload" },
	{ name: "mail", pattern: "/system/e-mail", view: "admin.Mail", permission: "settings.manage", nav: "mail", title: "nav.mail" },
	{ name: "teamsSettings", pattern: "/system/microsoft-teams", view: "admin.Teams", permission: "settings.integrations", nav: "teamsSettings", title: "nav.teamsSettings" },
	{ name: "dossiers", pattern: "/system/aktenarchiv", view: "admin.Dossiers", permission: "bauakten.read", nav: "dossiers", title: "nav.dossiers" },
	{ name: "monitoring", pattern: "/system/ueberwachung", view: "admin.Monitoring", permission: "backup.manage", nav: "monitoring", title: "nav.monitoring" },
	{ name: "notices", pattern: "/system/hinweise", view: "admin.Notices", permission: "settings.manage", nav: "notices", title: "nav.notices" },
	{ name: "company", pattern: "/system/firmendaten", view: "admin.Company", permission: "settings.manage", nav: "company", title: "nav.company" },
	{ name: "audit", pattern: "/system/protokoll", view: "admin.Audit", permission: "audit.read", nav: "audit", title: "nav.audit" },
];

export interface NavEntry {
	key: string;
	path?: string;
	icon?: string;
	permission?: string;
	/** Sichtbar, wenn mindestens eines dieser Rechte vorliegt. */
	anyPermission?: string[];
	children?: NavEntry[];
}

const adminPermissions = [...new Set(adminAreas.map((a) => a.permission).filter((p): p is string => !!p))];

/** Gruppierte Seitennavigation (Texte: i18n „nav.<key>“). */
export const navigation: NavEntry[] = [
	{ key: "start", path: "/start", icon: "sap-icon://home" },
	{ key: "chat", path: "/chat", icon: "sap-icon://discussion-2", permission: "chat.use" },
	// Microsoft Teams: öffnet Teams (Team „Baustellen“) in neuem Fenster; sichtbar im Chat-Modus „Teams“ oder „beides“.
	{ key: "teams", icon: "sap-icon://collaborate" },
	{
		key: "groupOrders", children: [
			{ key: "today", path: "/tagesuebersicht", icon: "sap-icon://date-time", permission: "bauakten.read" },
			{ key: "bauakten", path: "/bauakten", icon: "sap-icon://folder-full", permission: "bauakten.read" },
			{ key: "documents", path: "/dokumente", icon: "sap-icon://documents", permission: "bauakten.read" },
			{ key: "inquiries", path: "/anfragen", icon: "sap-icon://inbox", permission: "contacts.read" },
			{ key: "siteMap", path: "/bauakten/karte", icon: "sap-icon://map-2", permission: "bauakten.read" },
			{ key: "oldFiles", path: "/bauakten?status=old", icon: "sap-icon://attachment-zip-file", permission: "bauakten.read" },
		],
	},
	{
		key: "groupPlanning", children: [
			{ key: "cockpit", path: "/planung/cockpit", icon: "sap-icon://manager-insight", permission: "planning.read" },
			{ key: "planning", path: "/planung", icon: "sap-icon://gantt-bars", permission: "planning.read" },
			{ key: "teamPlanner", path: "/planung/teamplaner", icon: "sap-icon://time-entry-request", permission: "planning.read" },
			{ key: "calendar", path: "/planung/kalender", icon: "sap-icon://calendar", permission: "planning.read" },
			{ key: "workPlan", path: "/planung/arbeitsplan", icon: "sap-icon://list", permission: "planning.read" },
		],
	},
	{
		key: "groupMaterial", children: [
			{ key: "orders", path: "/bestellungen", icon: "sap-icon://cart", permission: "materials.read" },
			{ key: "materials", path: "/material", icon: "sap-icon://product", permission: "materials.read" },
			{ key: "suppliers", path: "/lieferanten", icon: "sap-icon://supplier", permission: "materials.read" },
		],
	},
	{
		key: "groupOffice", children: [
			{ key: "customers", path: "/kunden", icon: "sap-icon://customer", permission: "contacts.read" },
			{ key: "calls", path: "/anrufe", icon: "sap-icon://call", permission: "contacts.read" },
			{ key: "messages", path: "/verwaltung/nachrichten", icon: "sap-icon://paper-plane", permission: "notifications.send" },
			{ key: "crews", path: "/planung/kolonnen", icon: "sap-icon://group", permission: "planning.read" },
			{ key: "vehicles", path: "/planung/geraete", icon: "sap-icon://shipping-status", permission: "planning.read" },
		],
	},
	{
		key: "groupHr", children: [
			{ key: "staff", path: "/personal", icon: "sap-icon://employee-lookup", permission: "hr.read" },
			{ key: "vacationPlan", path: "/personal/urlaub?reiter=plan", icon: "sap-icon://calendar", permission: "hr.read" },
			{ key: "vacationRequests", path: "/personal/urlaub?reiter=antraege", icon: "sap-icon://approvals", permission: "absence.approve" },
			{ key: "sickNotes", path: "/personal/urlaub?reiter=krank", icon: "sap-icon://stethoscope", permission: "hr.manage" },
			{ key: "hrReminders", path: "/personal/urlaub?reiter=fristen", icon: "sap-icon://lateness", permission: "hr.read" },
			{ key: "vacation", path: "/personal/urlaub", icon: "sap-icon://general-leave-request", permission: "absence.request" },
		],
	},
	// IT-Service/Helpdesk: eigene Gruppe (sichtbar mit Modul Helpdesk; Einstellungen nur für die Bearbeitung).
	{
		key: "groupHelpdesk", children: [
			...adminAreas.filter((a) => a.group === "groupHelpdesk" && a.key === "helpdesk")
				.map((a) => ({ key: a.key, path: a.path, icon: a.icon, permission: a.permission })),
			{ key: "helpdeskNew", path: "/it-center/helpdesk?neu=1", icon: "sap-icon://add-activity" },
			...adminAreas.filter((a) => a.group === "groupHelpdesk" && a.key !== "helpdesk")
				.map((a) => ({ key: a.key, path: a.path, icon: a.icon, permission: a.permission })),
		],
	},
	// Verwaltung: Gruppen und Einträge aus dem Verwaltungskatalog; vorneweg die Übersicht mit Suche.
	...adminGroups.filter((g) => g.key !== "groupHelpdesk").map((g, i): NavEntry => ({
		key: g.key,
		children: [
			...(i === 0 ? [{ key: "adminHub", path: "/verwaltung", icon: "sap-icon://action-settings", anyPermission: adminPermissions }] : []),
			...adminAreas.filter((a) => a.group === g.key).map((a) => ({ key: a.key, path: a.path, icon: a.icon, permission: a.permission })),
		],
	})),
];

/** Personal (HR): Reiter der Urlaubsseite als eigene Navigationseinträge markieren. */
const vacationTabs: Record<string, string> = { plan: "vacationPlan", antraege: "vacationRequests", krank: "sickNotes", fristen: "hrReminders" };

export function navKeyFor(nav: string | undefined, query: URLSearchParams): string {
	if (nav === "crews" && query.get("reiter") === "geraete") return "vehicles";
	if (nav === "company") return companyTabKeys[query.get("bereich") ?? ""] ?? "company";
	if (nav === "vacation") return vacationTabs[query.get("reiter") ?? ""] ?? "vacation";
	if (nav === "bauakten" && query.get("status") === "old") return "oldFiles";
	if (nav === "helpdesk" && query.get("neu")) return "helpdeskNew";
	return nav ?? "";
}
