/** Bauakte speichern: immer der vollständige Stand mit der gelesenen Version (If-Match). */
import EventBus from "sap/ui/core/EventBus";
import { ApiError, put } from "./api";
import type { BauakteDetail, BauakteStatus } from "./models";

export interface BauakteChanges {
	title?: string;
	status?: BauakteStatus;
	customerId?: string | null;
	street?: string | null;
	postalCode?: string | null;
	city?: string | null;
	latitude?: number | null;
	longitude?: number | null;
	responsibleUserId?: string | null;
	description?: string | null;
	plannedStart?: string | null;
	plannedEnd?: string | null;
	siteContactName?: string | null;
	siteContactPhone?: string | null;
	siteContactEmail?: string | null;
	siteNotes?: string | null;
	cancellationReason?: string | null;
}

export const VERSION_CONFLICT =
	"Die Bauakte wurde inzwischen von jemand anderem geändert. Bitte neu laden und die Änderung erneut vornehmen.";

export async function saveBauakte(b: BauakteDetail, changes: BauakteChanges): Promise<BauakteDetail> {
	try {
		const updated = await put<BauakteDetail>(`bauakten/${b.id}`, {
			title: b.title, status: b.status, street: b.street, postalCode: b.postalCode, city: b.city, latitude: b.latitude, longitude: b.longitude,
			description: b.description, customerId: b.customerId, responsibleUserId: b.responsibleUserId, plannedStart: b.plannedStart,
			plannedEnd: b.plannedEnd, siteContactName: b.siteContactName, siteContactPhone: b.siteContactPhone, siteContactEmail: b.siteContactEmail,
			siteNotes: b.siteNotes, cancellationReason: b.cancellationReason,
			...changes,
		}, { "If-Match": `"${b.version}"` });
		EventBus.getInstance().publish("bauakte", "updated", { bauakte: updated });
		return updated;
	} catch (error) {
		if (error instanceof ApiError && error.code === "VERSION_CONFLICT") {
			throw new ApiError(409, error.code, VERSION_CONFLICT);
		}
		throw error;
	}
}
