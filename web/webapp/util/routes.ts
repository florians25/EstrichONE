/** Seiten der Anwendung mit Pfad, View, Recht und Navigationseintrag. */
import type { RouteDef } from "./router";

export const routes: RouteDef[] = [
	{ name: "login", pattern: "/anmelden", view: "auth.Login", public: true, title: "login.title" },
	{ name: "setup", pattern: "/einrichtung", view: "auth.Setup", public: true, title: "setup.title" },
	{ name: "setupAlias", pattern: "/einrichten", view: "auth.Setup", public: true, title: "setup.title" },
	{ name: "acceptancePublic", pattern: "/abnahme/:token", view: "public.Acceptance", public: true, title: "acceptance.title" },
	{ name: "orderPrint", pattern: "/bestellungen/:id/druck", view: "materials.OrderPrint", bare: true, permission: "materials.read", title: "orders.print" },
	{ name: "report", pattern: "/bauakten/:id/bericht", view: "bauakte.Report", bare: true, permission: "bauakten.read", title: "report.title" },
	{ name: "start", pattern: "/start", view: "Start", nav: "start", title: "nav.start" },
	{ name: "password", pattern: "/konto/passwort", view: "auth.ChangePassword", title: "password.title" },
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
	{ name: "dispatch", pattern: "/planung/wer-faehrt-wohin", view: "planning.Dispatch", permission: "planning.read", nav: "dispatch", title: "nav.dispatch" },
	{ name: "vacation", pattern: "/personal/urlaub", view: "staff.Vacation", permission: "absence.request", nav: "vacation", title: "nav.vacation" },
	{ name: "staff", pattern: "/personal", view: "staff.Staff", permission: "hr.read", nav: "staff", title: "nav.staff" },
	{ name: "employee", pattern: "/personal/:id", view: "staff.Employee", permission: "hr.read", nav: "staff", title: "nav.staff" },
	{ name: "dayPlan", pattern: "/planung/tageseinteilung", view: "planning.Planning", permission: "planning.read", nav: "planning", title: "nav.planning" },
	{ name: "workPlan", pattern: "/planung/arbeitsplan", view: "planning.WorkPlan", permission: "planning.read", nav: "workPlan", title: "nav.workPlan" },
	{ name: "crews", pattern: "/planung/kolonnen", view: "planning.Resources", permission: "planning.read", nav: "crews", title: "nav.crews" },
	{ name: "devices2", pattern: "/planung/geraete", view: "planning.Resources", permission: "planning.read", nav: "vehicles", title: "nav.vehicles" },
	{ name: "resources", pattern: "/planung/ressourcen", view: "planning.Resources", permission: "planning.read", nav: "crews", title: "nav.crews" },
	{ name: "masterData", pattern: "/verwaltung/stammdaten", view: "admin.MasterData", permission: "bauakten.read", nav: "masterData", title: "nav.masterData" },
	{ name: "roles", pattern: "/verwaltung/rollen", view: "admin.Roles", permission: "users.manage", nav: "roles", title: "nav.roles" },
	{ name: "users", pattern: "/verwaltung/benutzer", view: "admin.Users", permission: "users.manage", nav: "users", title: "nav.users" },
	{ name: "devices", pattern: "/it-center/geraete", view: "admin.Devices", permission: "devices.read", nav: "devices", title: "nav.devices" },
	{ name: "helpdesk", pattern: "/it-center/helpdesk", view: "admin.Helpdesk", nav: "helpdesk", title: "nav.helpdesk" },
	{ name: "ticket", pattern: "/it-center/helpdesk/:id", view: "admin.Ticket", nav: "helpdesk", title: "nav.helpdesk" },
	{ name: "backup", pattern: "/system/sicherung", view: "admin.Backup", permission: "backup.manage", nav: "backup", title: "nav.backup" },
	{ name: "backupTargets", pattern: "/system/backup-ziele", view: "admin.BackupTargets", permission: "backup.manage", nav: "backupTargets", title: "nav.backupTargets" },
	{ name: "archive", pattern: "/system/archiv", view: "admin.Archive", permission: "settings.manage", nav: "archive", title: "nav.archive" },
	{ name: "mail", pattern: "/system/e-mail", view: "admin.Mail", permission: "settings.manage", nav: "mail", title: "nav.mail" },
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
	children?: NavEntry[];
}

/** Gruppierte Seitennavigation (Texte: i18n „nav.<key>“). */
export const navigation: NavEntry[] = [
	{ key: "start", path: "/start", icon: "sap-icon://home" },
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
			{ key: "dispatch", path: "/planung/wer-faehrt-wohin", icon: "sap-icon://journey-arrive", permission: "planning.read" },
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
	{
		key: "groupIt", children: [
			{ key: "users", path: "/verwaltung/benutzer", icon: "sap-icon://employee", permission: "users.manage" },
			{ key: "masterData", path: "/verwaltung/stammdaten", icon: "sap-icon://database", permission: "settings.manage" },
			{ key: "roles", path: "/verwaltung/rollen", icon: "sap-icon://key-user-settings", permission: "users.manage" },
			{ key: "devices", path: "/it-center/geraete", icon: "sap-icon://iphone", permission: "devices.read" },
			{ key: "helpdesk", path: "/it-center/helpdesk", icon: "sap-icon://headset" },
		],
	},
	{
		key: "groupSystem", children: [
			{ key: "company", path: "/system/firmendaten", icon: "sap-icon://building", permission: "settings.manage" },
			{ key: "backup", path: "/system/sicherung", icon: "sap-icon://upload-to-cloud", permission: "backup.manage" },
			{ key: "backupTargets", path: "/system/backup-ziele", icon: "sap-icon://it-host", permission: "backup.manage" },
			{ key: "archive", path: "/system/archiv", icon: "sap-icon://inbox", permission: "settings.manage" },
			{ key: "mail", path: "/system/e-mail", icon: "sap-icon://email", permission: "settings.manage" },
			{ key: "dossiers", path: "/system/aktenarchiv", icon: "sap-icon://folder-blank", permission: "settings.manage" },
			{ key: "monitoring", path: "/system/ueberwachung", icon: "sap-icon://line-chart", permission: "backup.manage" },
			{ key: "notices", path: "/system/hinweise", icon: "sap-icon://message-warning", permission: "settings.manage" },
			{ key: "audit", path: "/system/protokoll", icon: "sap-icon://activities", permission: "audit.read" },
		],
	},
];

/** Personal (HR): Reiter der Urlaubsseite als eigene Navigationseinträge markieren. */
const vacationTabs: Record<string, string> = { plan: "vacationPlan", antraege: "vacationRequests", krank: "sickNotes", fristen: "hrReminders" };

export function navKeyFor(nav: string | undefined, query: URLSearchParams): string {
	if (nav === "crews" && query.get("reiter") === "geraete") return "vehicles";
	if (nav === "vacation") return vacationTabs[query.get("reiter") ?? ""] ?? "vacation";
	return nav ?? "";
}
