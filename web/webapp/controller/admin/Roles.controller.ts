import BaseController from "../BaseController";
import Table from "sap/m/Table";
import Column from "sap/m/Column";
import ColumnListItem from "sap/m/ColumnListItem";
import GroupHeaderListItem from "sap/m/GroupHeaderListItem";
import CheckBox from "sap/m/CheckBox";
import Text from "sap/m/Text";
import Label from "sap/m/Label";
import VBox from "sap/m/VBox";
import HBox from "sap/m/HBox";
import Button from "sap/m/Button";
import type Control from "sap/ui/core/Control";
import { del, get, put } from "../../util/api";
import { openForm } from "../../util/forms";
import { uuidv7 } from "../../util/ids";
import type { RoleResponse } from "../../util/models";

interface PermissionInfo { code: string; group: string; description: string }

/**
 * Verwaltung › Rollen und Rechte: Rechtematrix (Zeilen Rechte nach Bereich, Spalten Rollen), eigene Rollen anlegen,
 * umbenennen und löschen. Gespeichert wird je geänderter Rolle.
 * @namespace estrichone.controller.admin
 */
export default class Roles extends BaseController {
	private roles: RoleResponse[] = [];
	private catalog: PermissionInfo[] = [];
	private draft = new Map<string, Set<string>>();

	onInit(): void {
		this.local({ dirty: false, filter: "", canManage: false });
	}

	onRouteMatched(): void {
		this.model().setProperty("/canManage", this.has("roles.manage"));
		void this.load();
	}

	private async load(): Promise<void> {
		const [roles, catalog] = await Promise.all([
			this.run(() => get<RoleResponse[]>("access/roles")),
			this.has("roles.manage") ? this.run(() => get<PermissionInfo[]>("access/permissions")) : Promise.resolve(undefined),
		]);
		this.roles = (roles ?? []).sort((a, b) => Number(b.isSystem) - Number(a.isSystem) || a.name.localeCompare(b.name, "de"));
		this.catalog = catalog ?? [...new Set(this.roles.flatMap((r) => r.permissions))].map((code) => ({ code, group: code.split(".")[0], description: code }));
		this.draft = new Map(this.roles.map((r) => [r.id, new Set(r.permissions)]));
		this.model().setProperty("/dirty", false);
		this.render();
	}

	onFilter(): void { this.render(); }

	private render(): void {
		const host = this.byId("matrixHost") as VBox;
		host.destroyItems();
		const canManage = this.model().getProperty("/canManage") === true;
		const q = String(this.model().getProperty("/filter") ?? "").trim().toLowerCase();
		const table = new Table({ fixedLayout: false, sticky: ["ColumnHeaders"], ariaLabelledBy: [] });
		table.addColumn(new Column({ width: "22rem", header: new Text({ text: "Recht" }) }));
		for (const role of this.roles) {
			const header = new VBox({ alignItems: "Center", items: [
				new Label({ text: role.name, design: "Bold", wrapping: true, textAlign: "Center" }),
				new Text({ text: role.isSystem ? "Standardrolle" : "eigene Rolle" }).addStyleClass("eoMuted"),
				...(canManage ? [new HBox({ justifyContent: "Center", items: [
					new Button({ icon: "sap-icon://edit", type: "Transparent", tooltip: `${role.name} umbenennen`, press: () => void this.edit(role) }),
					...(role.isSystem ? [] : [new Button({ icon: "sap-icon://delete", type: "Transparent", tooltip: `${role.name} löschen`, press: () => void this.remove(role) })]),
				] })] : []),
			] });
			table.addColumn(new Column({ hAlign: "Center", width: "9rem", header }));
		}
		let group = "";
		for (const p of this.catalog.filter((x) => !q || x.description.toLowerCase().includes(q) || x.code.includes(q))) {
			if (p.group !== group) {
				group = p.group;
				table.addItem(new GroupHeaderListItem({ title: group }));
			}
			const cells: Control[] = [new VBox({ items: [new Text({ text: p.description }), new Text({ text: p.code }).addStyleClass("eoMuted eoTabular")] })];
			for (const role of this.roles) {
				const admin = role.isSystem && role.name === "Administrator";
				cells.push(new CheckBox({
					selected: this.draft.get(role.id)?.has(p.code) ?? false,
					enabled: canManage && !admin,
					tooltip: `${role.name}: ${p.description}`,
					select: (e) => {
						const set = this.draft.get(role.id)!;
						if (e.getParameter("selected")) set.add(p.code); else set.delete(p.code);
						this.model().setProperty("/dirty", this.changedRoles().length > 0);
					},
				}));
			}
			table.addItem(new ColumnListItem({ cells }));
		}
		host.addItem(table);
	}

	private changedRoles(): RoleResponse[] {
		return this.roles.filter((r) => {
			const now = this.draft.get(r.id)!;
			return now.size !== r.permissions.length || r.permissions.some((p) => !now.has(p));
		});
	}

	async onSave(): Promise<void> {
		const changed = this.changedRoles();
		const done = await this.run(async () => {
			for (const role of changed) {
				await put(`access/roles/${role.id}`, { name: role.name, description: role.description, permissions: [...this.draft.get(role.id)!], version: role.version });
			}
			return true;
		});
		if (done) {
			this.toast(`${changed.length} ${changed.length === 1 ? "Rolle" : "Rollen"} gespeichert`);
			await this.load();
		}
	}

	onDiscard(): void { void this.load(); }

	async onNew(): Promise<void> {
		const created = await openForm<RoleResponse>({
			title: "Rolle anlegen", submitText: "Anlegen",
			intro: "Rechte danach in der Matrix ankreuzen. Optional die Rechte einer vorhandenen Rolle übernehmen.",
			initial: { copyFrom: "" },
			fields: [
				{ key: "name", label: "Name", required: true, maxLength: 100, placeholder: "z. B. Disposition" },
				{ key: "description", label: "Beschreibung", type: "textarea", rows: 2, maxLength: 500 },
				{ key: "copyFrom", label: "Rechte übernehmen von", type: "select", options: [{ key: "", text: "– keine –" }, ...this.roles.map((r) => ({ key: r.id, text: r.name }))] },
			],
			submit: (v) => put<RoleResponse>(`access/roles/${uuidv7()}`, {
				name: String(v.name).trim(), description: (v.description as string) || null,
				permissions: this.roles.find((r) => r.id === v.copyFrom)?.permissions ?? [],
			}),
		});
		if (created) {
			this.toast("Rolle angelegt");
			await this.load();
		}
	}

	private async edit(role: RoleResponse): Promise<void> {
		const saved = await openForm<RoleResponse>({
			title: "Rolle bearbeiten", submitText: "Speichern", initial: { name: role.name, description: role.description ?? "" },
			fields: [
				{ key: "name", label: "Name", required: true, maxLength: 100 },
				{ key: "description", label: "Beschreibung", type: "textarea", rows: 2, maxLength: 500 },
			],
			submit: (v) => put<RoleResponse>(`access/roles/${role.id}`, {
				name: String(v.name).trim(), description: (v.description as string) || null, permissions: role.permissions, version: role.version,
			}),
		});
		if (saved) await this.load();
	}

	private async remove(role: RoleResponse): Promise<void> {
		if (!(await this.confirm(`Rolle „${role.name}“ löschen? Benutzer mit dieser Rolle verlieren deren Rechte.`, { action: "Löschen", danger: true }))) return;
		if (await this.run(async () => { await del(`access/roles/${role.id}`); return true; })) {
			this.toast("Rolle gelöscht");
			await this.load();
		}
	}
}
