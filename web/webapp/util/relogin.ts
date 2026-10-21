/**
 * Sitzung abgelaufen, während jemand arbeitet: Anmeldedialog über der aktuellen Seite statt Sprung zur Anmeldeseite.
 * Nach erfolgreicher Anmeldung wiederholt der API-Client die abgelehnte Anfrage – Eingaben in offenen Dialogen
 * bleiben erhalten. Mehrere gleichzeitig abgelehnte Anfragen teilen sich einen Dialog.
 */
import { openForm } from "./forms";
import { session } from "./session";

const REMEMBER_KEY = "estrichone.remember";

export function readRemember(): boolean {
	try {
		return localStorage.getItem(REMEMBER_KEY) === "1";
	} catch {
		return false;
	}
}

export function writeRemember(value: boolean): void {
	try {
		localStorage.setItem(REMEMBER_KEY, value ? "1" : "0");
	} catch {
		// ohne Speicher: nur Komfort
	}
}

let open: Promise<boolean> | null = null;

/** Liefert true, wenn derselbe Benutzer wieder angemeldet ist. */
export function relogin(): Promise<boolean> {
	const user = session.data.user;
	if (!user) {
		return Promise.resolve(false);
	}
	open ??= openForm<boolean>({
		title: "Erneut anmelden",
		submitText: "Anmelden",
		width: "26rem",
		intro: "Die Sitzung ist abgelaufen. Bitte das Passwort eingeben, um weiterzuarbeiten – nicht gespeicherte Eingaben bleiben erhalten.",
		initial: { login: user.userName, password: "", remember: readRemember() },
		fields: [
			{ key: "login", label: "Anmeldename", type: "text", required: true },
			{ key: "password", label: "Passwort", type: "password", required: true },
			{ key: "remember", label: "Angemeldet bleiben (dieses Gerät)", type: "checkbox" },
		],
		submit: async (v) => {
			const login = String(v.login).trim();
			writeRemember(v.remember === true);
			await session.signIn(login, String(v.password), v.remember === true);
			if (session.data.user?.id !== user.id) {
				// Anderer Benutzer: Seite frisch laden, damit nichts Fremdes sichtbar bleibt.
				window.location.reload();
			}
			return true;
		},
	}).then((result) => result === true).finally(() => {
		open = null;
	});
	return open;
}
