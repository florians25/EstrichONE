import type { BauakteDetail, BauakteMember } from "./models";

/** Was der angemeldete Benutzer an einer Bauakte darf (Kopf, Hinweis-Streifen, Abschnitte). */
export interface AkteAccess {
	/** Zugeteilt oder „Alle Bauakten bearbeiten“ – Änderungen erlaubt (Server prüft mit 403 BAUAKTE_NOT_ASSIGNED). */
	writable: boolean;
	/** „Nur ansehen“-Hinweis zeigen. */
	readOnly: boolean;
	/** Bearbeiten-Aktionen im Kopf (zusätzlich Recht „Bauakten bearbeiten“). */
	canEdit: boolean;
	/** „Mich als Beteiligten eintragen“. */
	canJoin: boolean;
	/** „Als Beteiligter austragen“ (nur selbst eingetragene Beteiligte, nicht die zuständige Person). */
	canLeave: boolean;
}

/**
 * Alle sehen alle Bauakten; ändern darf nur, wer zugeteilt ist (zuständig, Beteiligter, eingeplant) oder
 * „Alle Bauakten bearbeiten“ hat. Ältere Server ohne <c>canEdit</c> gelten als bearbeitbar.
 */
export function akteAccess(b: Pick<BauakteDetail, "status" | "responsibleUserId" | "isAssigned" | "canEdit"> | null | undefined,
	members: Pick<BauakteMember, "userId" | "role">[], me: string | null | undefined, hasEditPermission: boolean): AkteAccess {
	const writable = !!b && b.canEdit !== false;
	const joined = !!me && !!b && (b.responsibleUserId === me || members.some((x) => x.userId === me));
	return {
		writable,
		readOnly: !!b && !writable,
		canEdit: writable && hasEditPermission,
		canJoin: !!b && !!me && b.status !== "cancelled" && !joined && b.isAssigned !== true,
		canLeave: !!b && !!me && b.responsibleUserId !== me && members.some((x) => x.userId === me && x.role === "member"),
	};
}
