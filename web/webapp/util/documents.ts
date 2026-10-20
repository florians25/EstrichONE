/** Dokumentablage: Fassungen (Versionsketten) und Ordnerbaum aus Dokumentart + Unterordner. Ohne UI5-Abhängigkeit. */
import { documentCategories } from "./labels";
import type { DocumentCategory, FileResponse } from "./models";

export interface DocumentRow extends FileResponse {
	/** Fassung in der Kette (1 = erste). */
	versionNumber: number;
	/** Anzahl Fassungen der Kette. */
	versionCount: number;
	/** Ältere Fassung, die durch eine neuere ersetzt wurde. */
	superseded: boolean;
	/** Alle Fassungen der Kette, neueste zuerst. */
	chain: string[];
	categoryKey: DocumentCategory;
	path: string;
}

/** Fassungen bestimmen: Eine Datei ist ersetzt, wenn eine andere auf sie als Vorgänger verweist. */
export function documentRows(files: FileResponse[]): DocumentRow[] {
	const docs = files.filter((f) => f.kind === "document");
	const byId = new Map(docs.map((d) => [d.id, d]));
	const successor = new Map<string, string>();
	for (const d of docs) {
		if (d.previousVersionId && byId.has(d.previousVersionId)) successor.set(d.previousVersionId, d.id);
	}
	const rows: DocumentRow[] = [];
	for (const d of docs) {
		// Kette rückwärts bis zur ersten Fassung, vorwärts bis zur neuesten.
		const back: string[] = [];
		let current: FileResponse | undefined = d;
		const seen = new Set<string>();
		while (current && !seen.has(current.id)) {
			seen.add(current.id);
			back.push(current.id);
			current = current.previousVersionId ? byId.get(current.previousVersionId) : undefined;
		}
		const forward: string[] = [];
		let next = successor.get(d.id);
		while (next && !seen.has(next)) {
			seen.add(next);
			forward.push(next);
			next = successor.get(next);
		}
		const chain = [...forward.reverse(), ...back];
		const categoryKey = ((d.category ?? "other") in documentCategories ? d.category ?? "other" : "other") as DocumentCategory;
		rows.push({
			...d, versionNumber: back.length, versionCount: chain.length, superseded: successor.has(d.id), chain, categoryKey,
			path: [categoryKey, d.folder].filter(Boolean).join("/"),
		});
	}
	return rows;
}

export interface FolderNode {
	key: string;
	text: string;
	icon: string;
	count: number;
	category: DocumentCategory | null;
	folder: string | null;
	nodes: FolderNode[];
}

/** Ordnerbaum: „Alle Dokumente“ → Dokumentarten → Unterordner (mit Anzahl aktueller Fassungen). */
export function folderTree(rows: DocumentRow[], extraFolders: string[] = []): FolderNode {
	const current = rows.filter((r) => !r.superseded);
	const root: FolderNode = { key: "", text: "Alle Dokumente", icon: "sap-icon://folder-full", count: current.length, category: null, folder: null, nodes: [] };
	for (const [key, label] of Object.entries(documentCategories) as [DocumentCategory, { text: string; icon: string }][]) {
		const inCategory = current.filter((r) => r.categoryKey === key);
		const node: FolderNode = { key, text: label.text, icon: label.icon, count: inCategory.length, category: key, folder: null, nodes: [] };
		const paths = new Set<string>(inCategory.map((r) => r.folder).filter((f): f is string => !!f));
		for (const extra of extraFolders) {
			if (extra.startsWith(`${key}/`)) paths.add(extra.slice(key.length + 1));
		}
		for (const path of [...paths].sort((a, b) => a.localeCompare(b, "de"))) {
			let parent = node;
			const segments = path.split("/");
			segments.forEach((segment, index) => {
				const folder = segments.slice(0, index + 1).join("/");
				let child = parent.nodes.find((n) => n.folder === folder);
				if (!child) {
					child = { key: `${key}/${folder}`, text: segment, icon: "sap-icon://folder-blank", count: 0, category: key, folder, nodes: [] };
					parent.nodes.push(child);
				}
				parent = child;
			});
		}
		const count = (n: FolderNode) => {
			n.count = current.filter((r) => r.categoryKey === key && n.folder !== null && (r.folder === n.folder || r.folder?.startsWith(`${n.folder}/`))).length;
			n.nodes.forEach(count);
		};
		node.nodes.forEach(count);
		root.nodes.push(node);
	}
	return root;
}

/** Liegt die Zeile im gewählten Ordner (inklusive Unterordnern)? */
export function inFolder(row: DocumentRow, category: DocumentCategory | null, folder: string | null): boolean {
	if (!category) return true;
	if (row.categoryKey !== category) return false;
	return !folder || row.folder === folder || !!row.folder?.startsWith(`${folder}/`);
}

export type DocumentSort = "newest" | "oldest" | "name" | "size" | "category";

export function sortRows(rows: DocumentRow[], sort: DocumentSort): DocumentRow[] {
	const copy = [...rows];
	switch (sort) {
		case "oldest": return copy.sort((a, b) => a.createdAt.localeCompare(b.createdAt));
		case "name": return copy.sort((a, b) => a.originalName.localeCompare(b.originalName, "de", { numeric: true }));
		case "size": return copy.sort((a, b) => b.sizeBytes - a.sizeBytes);
		case "category": return copy.sort((a, b) => a.path.localeCompare(b.path, "de") || a.originalName.localeCompare(b.originalName, "de"));
		default: return copy.sort((a, b) => b.createdAt.localeCompare(a.createdAt));
	}
}

/** Symbol je Dateityp. */
export function fileIcon(contentType: string, name: string): string {
	if (contentType === "application/pdf" || name.toLowerCase().endsWith(".pdf")) return "sap-icon://pdf-attachment";
	if (contentType.startsWith("image/")) return "sap-icon://attachment-photo";
	if (contentType.startsWith("video/")) return "sap-icon://attachment-video";
	if (/spreadsheet|excel|csv/.test(contentType) || /\.(xlsx?|csv)$/i.test(name)) return "sap-icon://excel-attachment";
	if (/word|document/.test(contentType) || /\.docx?$/i.test(name)) return "sap-icon://doc-attachment";
	if (/zip|compressed/.test(contentType)) return "sap-icon://attachment-zip-file";
	return "sap-icon://document";
}
