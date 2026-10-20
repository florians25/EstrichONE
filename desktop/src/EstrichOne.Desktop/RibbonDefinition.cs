using EstrichOne.Client;
using EstrichOne.Desktop.Controls;

namespace EstrichOne.Desktop;

/// <summary>Register und Befehle nach docs/reengineering/06, Abschnitt 2.3 – je nach Rechten des Benutzers.</summary>
internal static class RibbonDefinition
{
    // Segoe Fluent Icons
    private const string Today = "";
    private const string List = "";
    private const string Add = "";
    private const string Refresh = "";
    private const string Globe = "";
    private const string Calendar = "";
    private const string Phone = "";
    private const string Cart = "";
    private const string People = "";
    private const string Map = "";
    private const string Help = "";
    private const string Device = "";
    private const string Shield = "";
    private const string Note = "";
    private const string Task = "";
    private const string Room = "";
    private const string Check = "";
    private const string Print = "";
    private const string Route = "";
    private const string Box = "";
    private const string Inbox = "";

    public static IReadOnlyList<RibbonTab> Build(MainWindow window, EstrichOneClient client)
    {
        var tabs = new List<RibbonTab>
        {
            new("start", "Start",
            [
                new("Ansicht", [
                    new("Tagesübersicht", Today, () => window.Navigate("today"), "T"),
                    new("Bauakten", List, () => window.Navigate("bauakten"), "B"),
                    new("Aktualisieren", Refresh, () => _ = window.RefreshCurrentAsync(), "A", "Aktuelle Ansicht neu laden (F5)"),
                ]),
                new("Neu", Only(client, "bauakten.create", new RibbonCommand("Neue Bauakte", Add, () => MainWindow.OpenInBrowser("/bauakten?neu=bauakte"), "N",
                    "Neue Bauakte im Browser anlegen"))
                    .Concat(Only(client, "contacts.edit", new RibbonCommand("Anruf erfassen", Phone, () => window.Navigate("calls"), "R"))).ToList()),
                new("Hilfe", [new("Problem melden", Help, () => window.Navigate("helpdesk"), "P")]),
            ]),
            new("bauakten", "Bauakten",
            [
                new("Bauakten", [
                    new("Bauaktensuche", List, () => window.Navigate("bauakten"), "S"),
                    new("Baustellenkarte", Map, () => MainWindow.OpenInBrowser("/bauakten/karte"), "K", "Karte im Browser öffnen"),
                ]),
                new("Büro", Only(client, "contacts.read",
                    new RibbonCommand("Anfragen", Inbox, () => MainWindow.OpenInBrowser("/anfragen"), "F", "Anfragen im Browser bearbeiten"),
                    new RibbonCommand("Kunden", People, () => MainWindow.OpenInBrowser("/kunden"), "U", "Kundenstamm im Browser")).ToList()),
            ]),
        };

        if (client.Has("planning.read"))
        {
            tabs.Add(new("planung", "Planung",
            [
                new("Disposition", [
                    new("Kolonnenplanung", Calendar, () => window.Navigate("planning"), "K"),
                    new("Planung bearbeiten", Globe, () => MainWindow.OpenInBrowser("/planung"), "W",
                        "Einsätze per Ziehen und Ablegen im Browser planen"),
                    new("Arbeitsplan", List, () => MainWindow.OpenInBrowser("/planung/arbeitsplan"), "A"),
                ]),
                new("Ressourcen", [new("Kolonnen und Geräte", People, () => MainWindow.OpenInBrowser("/planung/ressourcen"), "R")]),
            ]));
        }

        var office = new List<RibbonGroup>();
        if (client.Has("contacts.read"))
        {
            office.Add(new("Telefon", [
                new("Anrufe", Phone, () => window.Navigate("calls"), "A"),
                new("Anfragen", Inbox, () => MainWindow.OpenInBrowser("/anfragen"), "F"),
            ]));
        }

        if (client.Has("materials.read"))
        {
            office.Add(new("Material", [
                new("Bestellungen", Cart, () => window.Navigate("orders"), "B"),
                new("Materialstamm", Box, () => MainWindow.OpenInBrowser("/material"), "M"),
                new("Lieferanten", People, () => MainWindow.OpenInBrowser("/lieferanten"), "L"),
            ]));
        }

        if (office.Count > 0)
        {
            tabs.Add(new("buero", "Büro", office));
        }

        tabs.Add(new("it", "IT & Support",
        [
            new("Support", [new("Helpdesk", Help, () => window.Navigate("helpdesk"), "H")]),
            new("Verwaltung im Browser",
                Only(client, "devices.read", new RibbonCommand("Geräte", Device, () => MainWindow.OpenInBrowser("/it-center/geraete"), "G"))
                .Concat(Only(client, "users.manage", new RibbonCommand("Benutzer", People, () => MainWindow.OpenInBrowser("/verwaltung/benutzer"), "U")))
                .Concat(Only(client, "backup.manage", new RibbonCommand("Sicherung", Shield, () => MainWindow.OpenInBrowser("/system/sicherung"), "S")))
                .Concat([new RibbonCommand("Web öffnen", Globe, () => MainWindow.OpenInBrowser("/start"), "W")]).ToList()),
        ]));

        // Kontextregister, solange eine Bauakte offen ist.
        tabs.Add(new("bauakte", "Bauakte",
        [
            new("Dokumentieren", [
                new("Notiz", Note, () => window.CurrentBauakte?.ShowTab("notizen"), "N"),
                new("Aufgabe / Mangel", Task, () => window.CurrentBauakte?.ShowTab("aufgaben"), "M"),
                new("Räume", Room, () => window.CurrentBauakte?.ShowTab("raeume"), "R"),
            ]),
            new("Status", Only(client, "bauakten.edit",
                new RibbonCommand("Als Auftrag übernehmen", Check, () => window.CurrentBauakte?.SetStatus(BauakteStatus.Planned), "U"),
                new RibbonCommand("Abschließen", Check, () => window.CurrentBauakte?.SetStatus(BauakteStatus.Completed), "S")).ToList()),
            new("Ausgabe", [
                new("Route planen", Route, () => window.CurrentBauakte?.OpenRoute(), "P"),
                new("Im Browser öffnen", Globe, () => window.CurrentBauakte?.OpenInBrowser(), "W"),
                new("Drucken", Print, () => window.CurrentBauakte?.OpenInBrowser(), "D", "Bauaktenblatt im Browser drucken"),
            ]),
        ], IsContextual: true));

        return tabs.Where(t => t.Groups.Any(g => g.Commands.Count > 0))
            .Select(t => t with { Groups = t.Groups.Where(g => g.Commands.Count > 0).ToList() }).ToList();
    }

    private static IEnumerable<RibbonCommand> Only(EstrichOneClient client, string permission, params RibbonCommand[] commands) =>
        client.Has(permission) ? commands : [];
}
