import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import Filter from "sap/ui/model/Filter";
import FilterOperator from "sap/ui/model/FilterOperator";
import type ListBinding from "sap/ui/model/ListBinding";
import type Table from "sap/m/Table";
import { get, post, put } from "../../util/api";
import { openForm } from "../../util/forms";
import type { RoleResponse, UserListItem, UserWithPassword } from "../../util/models";

/** Verwaltung › Benutzer: Einmalpasswort und Zwangswechsel, keine Passwörter per E-Mail. @namespace estrichone.controller.admin */
export default class Users extends BaseController {
	private roles: RoleResponse[] = [];

	onInit(): void {
		this.local({ items: [], busy: false, oneTime: null });
	}

	onRouteMatched(): void { void this.load(); }

	private async load(): Promise<void> {
		this.model().setProperty("/busy", true);
		const [users, roles] = await Promise.all([
			this.run(() => get<UserListItem[]>("users")),
			this.run(() => get<RoleResponse[]>("access/roles")),
		]);
		this.roles = roles ?? [];
		const name = (id: string) => this.roles.find((r) => r.id === id)?.name ?? "?";
		this.model().setProperty("/items", (users ?? []).map((u) => ({ ...u, roleText: u.roleIds.map(name).join(", ") || "–" })));
		this.model().setProperty("/busy", false);
	}

	private row(e: Event): UserListItem {
		return (e.getSource() as Control).getBindingContext("view")?.getObject() as UserListItem;
	}

	onSearch(e: Event): void {
		const q = String(e.getParameter("newValue" as never) ?? "").trim();
		const binding = (this.byId("table") as Table).getBinding("items") as ListBinding;
		binding.filter(q ? [new Filter({ filters: ["displayName", "userName", "email"].map((p) => new Filter(p, FilterOperator.Contains, q)), and: false })] : []);
	}

	async onCopy(): Promise<void> {
		const pw = String(this.model().getProperty("/oneTime/oneTimePassword") ?? "");
		try { await navigator.clipboard.writeText(pw); this.toast("Kopiert"); } catch { this.toast("Kopieren nicht möglich – bitte markieren und kopieren."); }
	}

	onCloseOneTime(): void { this.model().setProperty("/oneTime", null); }

	private roleField() {
		return { key: "roleIds", label: "Rollen", type: "multi" as const, options: this.roles.map((r) => ({ key: r.id, text: r.name, additionalText: r.description ?? "" })) };
	}

	private async ensureRoles(): Promise<void> {
		if (this.roles.length === 0) this.roles = (await this.run(() => get<RoleResponse[]>("access/roles"))) ?? [];
	}

	async onNew(): Promise<void> {
		await this.ensureRoles();
		const result = await openForm<UserWithPassword>({
			title: "Benutzer anlegen", submitText: "Anlegen", initial: { roleIds: [] },
			fields: [
				{ key: "displayName", label: "Name", required: true, maxLength: 200 },
				{ key: "userName", label: "Anmeldename", required: true, maxLength: 100, help: "Für Mitarbeiter ohne E-Mail-Adresse, z. B. „m.huber“." },
				{ key: "email", label: "E-Mail (optional)", type: "email" },
				{ key: "phone", label: "Telefon (optional)" },
				this.roleField(),
			],
			submit: (v) => post<UserWithPassword>("users", {
				displayName: String(v.displayName).trim(), userName: String(v.userName).trim(),
				email: v.email || null, phone: v.phone || null, roleIds: v.roleIds ?? [],
			}),
		});
		if (result) { this.model().setProperty("/oneTime", result); void this.load(); }
	}

	async onEdit(e: Event): Promise<void> {
		const u = this.row(e);
		await this.ensureRoles();
		const ok = await openForm<boolean>({
			title: "Benutzer bearbeiten", submitText: "Speichern",
			initial: { displayName: u.displayName, userName: u.userName, email: u.email ?? "", phone: u.phone ?? "", roleIds: u.roleIds },
			fields: [
				{ key: "displayName", label: "Name", required: true, maxLength: 200 },
				{ key: "userName", label: "Anmeldename", editable: false },
				{ key: "email", label: "E-Mail (optional)", type: "email" },
				{ key: "phone", label: "Telefon (optional)" },
				this.roleField(),
			],
			submit: async (v) => {
				await put(`users/${u.id}`, { displayName: String(v.displayName).trim(), email: v.email || null, phone: v.phone || null });
				await put(`access/users/${u.id}/roles`, { roleIds: v.roleIds ?? [] });
				return true;
			},
		});
		if (ok) { this.toast("Benutzer gespeichert"); void this.load(); }
	}

	async onReset(e: Event): Promise<void> {
		const u = this.row(e);
		if (!(await this.confirm("Alle Sitzungen der Person werden beendet. Sie erhalten ein Einmalpasswort zum Weitergeben.",
			{ title: `Passwort von ${u.displayName} zurücksetzen?`, action: "Zurücksetzen" }))) return;
		const result = await this.run(() => post<UserWithPassword>(`users/${u.id}/reset-password`));
		if (result) { this.model().setProperty("/oneTime", result); void this.load(); }
	}

	async onLock(e: Event): Promise<void> {
		const u = this.row(e);
		if (!(await this.confirm("Die Person wird sofort auf allen Geräten abgemeldet.", { title: `${u.displayName} sperren?`, action: "Sperren", danger: true }))) return;
		await this.run(() => post(`users/${u.id}/deactivate`));
		void this.load();
	}

	async onUnlock(e: Event): Promise<void> {
		await this.run(() => post(`users/${this.row(e).id}/activate`));
		void this.load();
	}
}
