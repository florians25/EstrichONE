import BaseController from "../BaseController";
import type Event from "sap/ui/base/Event";
import type Control from "sap/ui/core/Control";
import Filter from "sap/ui/model/Filter";
import FilterOperator from "sap/ui/model/FilterOperator";
import type ListBinding from "sap/ui/model/ListBinding";
import type Table from "sap/m/Table";
import { api, get, post, postForBlob, put } from "../../util/api";
import MessageBox from "sap/m/MessageBox";
import { openForm } from "../../util/forms";
import type { RoleResponse, UserListItem, UserWithPassword } from "../../util/models";

/** Verwaltung › Benutzer: Einmalpasswort und Zwangswechsel, keine Passwörter per E-Mail. @namespace estrichone.controller.admin */
export default class Users extends BaseController {
	private roles: RoleResponse[] = [];

	onInit(): void {
		this.local({ items: [], busy: false, oneTime: null });
	}

	onRouteMatched(): void {
		void this.load();
		void get("chat/admin/settings").then(() => this.model().setProperty("/chatAdmin", true)).catch(() => this.model().setProperty("/chatAdmin", false));
	}

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

	/** Zugangsbrief nach DIN 5008 mit dem gerade vergebenen Einmalpasswort (neuer Tab). */
	async onLetter(): Promise<void> {
		const one = this.model().getProperty("/oneTime") as UserWithPassword & { reset?: boolean } | null;
		if (!one) return;
		try {
			const blob = await postForBlob(`users/${one.user.id}/access-letter`, { password: one.oneTimePassword, reset: one.reset === true });
			const url = URL.createObjectURL(blob);
			const a = document.createElement("a");
			a.href = url;
			a.download = `Zugangsbrief ${one.user.userName}.pdf`;
			a.click();
			setTimeout(() => URL.revokeObjectURL(url), 60_000);
		} catch (error) {
			this.showError(error);
		}
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
		if (result) { this.model().setProperty("/oneTime", { ...result, reset: true }); void this.load(); }
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
		if (result) { this.model().setProperty("/oneTime", { ...result, reset: true }); void this.load(); }
	}

	async onLock(e: Event): Promise<void> {
		const u = this.row(e);
		if (!(await this.confirm("Die Person wird sofort auf allen Geräten abgemeldet.", { title: `${u.displayName} sperren?`, action: "Sperren", danger: true }))) return;
		await this.run(() => post(`users/${u.id}/deactivate`));
		void this.load();
	}

	/** Betroffenenrechte: Chatdaten einer Person als JSON ausgeben oder vollständig löschen (Audit ohne Inhalte). */
	async onChatData(e: Event): Promise<void> {
		const u = this.row(e);
		const action = await new Promise<string>((resolve) => {
			MessageBox.show(`Chatdaten von ${u.displayName}: Auskunft (Art. 15 DSGVO) als JSON-Datei oder Löschung aller Nachrichten, Einzelchats und Mitgliedschaften (Art. 17 DSGVO).`, {
				title: "Chatdaten", actions: ["Exportieren", "Alles löschen", MessageBox.Action.CANCEL], emphasizedAction: "Exportieren",
				onClose: (a: string) => resolve(a),
			});
		});
		if (action === "Exportieren") {
			const data = await this.run(() => get<unknown>(`chat/admin/users/${u.id}/export`));
			if (!data) return;
			const url = URL.createObjectURL(new Blob([JSON.stringify(data, null, 2)], { type: "application/json" }));
			const a = Object.assign(document.createElement("a"), { href: url, download: `chatdaten-${u.userName}-${new Date().toISOString().slice(0, 10)}.json` });
			a.click();
			setTimeout(() => URL.revokeObjectURL(url), 10_000);
		} else if (action === "Alles löschen") {
			if (!(await this.confirm("Alle Nachrichten der Person, ihre Einzelchats und Gruppenmitgliedschaften werden endgültig gelöscht.",
				{ title: `Chatdaten von ${u.displayName} löschen?`, action: "Endgültig löschen", danger: true }))) return;
			const result = await this.run(() => api<{ deletedMessages: number; removedMemberships: number }>(`chat/admin/users/${u.id}/messages`, { method: "DELETE" }));
			if (result) this.toast(`${result.deletedMessages} Nachrichten und ${result.removedMemberships} Mitgliedschaften gelöscht`);
		}
	}

	async onUnlock(e: Event): Promise<void> {
		await this.run(() => post(`users/${this.row(e).id}/activate`));
		void this.load();
	}
}
