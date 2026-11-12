/**
 * „Team im Betrieb“ der Bauakte als Karten (gleiche Optik wie die Kontakte): zuständige Person zuerst, Rolle als Badge,
 * Kolonne aus den Einsätzen der Akte (nächster, sonst letzter), E-Mail als Link. Ohne UI5-Abhängigkeit (Vitest).
 */
export interface TeamMemberLike {
	userId: string;
	displayName: string;
	email: string | null;
	role: "responsible" | "member";
	isActive: boolean;
}

interface AssignmentLike {
	day: string;
	crewName: string | null;
	people: { id: string }[];
}

export interface TeamCard {
	userId: string;
	displayName: string;
	initials: string;
	subtitle: string;
	roleText: string;
	responsible: boolean;
	crewText: string;
	email: string;
	lines: { kind: string; icon: string; text: string; href: string | null; label: string }[];
}

export function personInitials(name: string): string {
	const parts = name.trim().split(/\s+/).filter(Boolean);
	if (!parts.length) return "?";
	return (parts.length === 1 ? parts[0].slice(0, 2) : parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
}

/** Kolonne je Person: nächster Einsatz ab heute, sonst der letzte vergangene. */
export function crewOf(userId: string, assignments: AssignmentLike[], today: string): string {
	const own = assignments.filter((a) => a.crewName && a.people.some((p) => p.id === userId)).sort((a, b) => a.day.localeCompare(b.day));
	return (own.find((a) => a.day >= today) ?? own[own.length - 1])?.crewName ?? "";
}

export function teamCards(members: TeamMemberLike[], assignments: AssignmentLike[], today: string): TeamCard[] {
	return [...members]
		.sort((a, b) => Number(b.role === "responsible") - Number(a.role === "responsible") || a.displayName.localeCompare(b.displayName, "de"))
		.map((m) => {
			const crew = crewOf(m.userId, assignments, today);
			const roleText = m.role === "responsible" ? "Zuständig" : m.isActive ? "Mitarbeiter" : "Gesperrt";
			return {
				userId: m.userId, displayName: m.displayName, initials: personInitials(m.displayName),
				subtitle: m.role === "responsible" ? "Zuständig für die Bauakte" : crew ? `Kolonne ${crew.replace(/^Kolonne\s+/i, "")}` : "Mitarbeiter im Betrieb",
				roleText, responsible: m.role === "responsible", crewText: crew && m.role === "responsible" ? crew : "", email: m.email ?? "",
				lines: m.email ? [{ kind: "E-Mail", icon: "sap-icon://email", text: m.email, href: `mailto:${m.email}`, label: "E-Mail" }] : [],
			};
		});
}

/** Text für „Kontaktdaten kopieren“. */
export function teamCardText(card: TeamCard): string {
	return [`${card.displayName} (${card.roleText})`, card.subtitle, card.email ? `E-Mail: ${card.email}` : ""].filter(Boolean).join("\n");
}
