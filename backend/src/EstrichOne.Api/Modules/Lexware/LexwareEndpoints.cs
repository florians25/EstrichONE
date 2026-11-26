using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Lexware;

/// <summary>Was die Oberfläche über die Lexware-Office-Anbindung wissen muss (Kunde, Bauakte, Verwaltung).</summary>
/// <param name="Enabled">Modul eingeschaltet (Verwaltung › Module).</param>
/// <param name="Configured">API-Schlüssel hinterlegt.</param>
/// <param name="CanRead">Recht <c>invoices.read</c>.</param>
/// <param name="CanAssign">Recht <c>invoices.assign</c>.</param>
/// <param name="CanManage">Recht <c>settings.integrations</c>.</param>
/// <param name="SyncDefault">Vorgabe für „Mit Lexware Office abgleichen“ bei neuen Kunden (Modul an und Einstellung gesetzt).</param>
public sealed record LexwareStatusResponse(bool Enabled, bool Configured, bool CanRead, bool CanAssign, bool CanManage, bool SyncDefault = false);

/// <param name="HasApiKey">Schlüssel hinterlegt; der Schlüssel selbst wird nie zurückgegeben.</param>
/// <param name="BaseUrl">Wirksame Basis-URL der API.</param>
/// <param name="CustomBaseUrl">Abweichende Basis-URL eingestellt (sonst die Vorgabe).</param>
/// <param name="KeyHelpUrl">Seite in Lexware Office, auf der der Schlüssel erzeugt wird.</param>
public sealed record LexwareSettingsResponse(
    bool ModuleEnabled, bool HasApiKey, string BaseUrl, bool CustomBaseUrl, string? OrganizationName,
    DateTimeOffset? LastTestAt, bool? LastTestOk, string? LastTestError, int LinkedCustomers, int LinkedInvoices, string KeyHelpUrl, long Version,
    bool NewCustomersSync = true, LexwareChangeSync ChangeSync = LexwareChangeSync.Both, DateTimeOffset? LastChangeSyncAt = null,
    string? LastChangeSyncError = null, int CustomersWithDeviations = 0, bool AutoCreate = true);

/// <param name="ApiKey">Neuer Schlüssel; <c>null</c> oder leer lässt den gespeicherten unverändert.</param>
/// <param name="ClearApiKey">Gespeicherten Schlüssel entfernen.</param>
/// <param name="BaseUrl">Leer = Vorgabe <c>https://api.lexware.io</c>.</param>
/// <param name="NewCustomersSync">Neue Kunden standardmäßig mit Lexware Office abgleichen; ohne Angabe unverändert.</param>
/// <param name="ChangeSync">Richtung des Änderungsabgleichs verknüpfter Kunden; ohne Angabe unverändert.</param>
/// <param name="AutoCreate">Neue Kunden ohne Treffer automatisch in Lexware Office anlegen; ohne Angabe unverändert.</param>
public sealed record SaveLexwareSettingsRequest(
    [property: MaxLength(500)] string? ApiKey = null, bool ClearApiKey = false, [property: MaxLength(LexwareSettingsRow.MaxUrl)] string? BaseUrl = null,
    long? Version = null, bool? NewCustomersSync = null, LexwareChangeSync? ChangeSync = null, bool? AutoCreate = null);

public sealed record LexwareTestResponse(bool Ok, string? OrganizationName, string? Error, DateTimeOffset At);

/// <summary>Kontakt in Lexware Office.</summary>
/// <param name="Url">Adresse des Kontakts in der Lexware-Oberfläche.</param>
/// <param name="LinkedContactId">EstrichONE-Kunde, mit dem der Kontakt bereits verknüpft ist.</param>
public sealed record LexwareContactSummary(
    string Id, string Name, bool IsCompany, string? CustomerNumber, string? Street, string? PostalCode, string? City, string? Email, string? Phone,
    string Url, Guid? LinkedContactId, string? LinkedContactName);

/// <summary>Möglicher Treffer in Lexware Office mit Übereinstimmungsgründen (Score 0 = nur Suchtreffer).</summary>
public sealed record LexwareContactCandidate(LexwareContactSummary Contact, int Score, IReadOnlyList<string> Reasons, bool Strong);

/// <summary>Kunde in EstrichONE.</summary>
public sealed record LexwareCustomerSummary(
    Guid Id, string Name, bool IsCompany, string? CustomerNumber, string? Street, string? PostalCode, string? City, string? Email, string? Phone,
    bool Archived, string? LinkedLexwareContactId, bool SyncEnabled = false, string? Creation = null, string? CreationError = null);

/// <summary>Möglicher Treffer in EstrichONE mit Übereinstimmungsgründen.</summary>
public sealed record LexwareCustomerCandidate(LexwareCustomerSummary Customer, int Score, IReadOnlyList<string> Reasons, bool Strong);

public sealed record LexwareContactLinkResponse(
    string LexwareContactId, string? Name, string? Number, string Url, LexwareLinkOrigin Origin, DateTimeOffset LinkedAt);

/// <summary>Abweichung in einem abgeglichenen Feld mit den Werten beider Seiten.</summary>
/// <param name="Field"><c>name</c>, <c>salutation</c>, <c>street</c>, <c>postalCode</c>, <c>city</c>, <c>email</c>, <c>phone</c> oder <c>vatId</c>.</param>
public sealed record LexwareDeviationField(string Field, string Label, string? EstrichOne, string? Lexware);

/// <summary>Stand des Änderungsabgleichs einer Verknüpfung.</summary>
/// <param name="State">
/// <c>synced</c> (abgeglichen), <c>deviations</c> (offene Abweichungen), <c>pending</c> (Abgleich steht aus), <c>error</c>
/// (letzter Abgleich fehlgeschlagen), <c>missing</c> bzw. <c>archived</c> (Kontakt in Lexware Office nicht mehr vorhanden
/// bzw. archiviert), <c>disabled</c> („Mit Lexware Office abgleichen“ aus), <c>paused</c> (Kunde archiviert), <c>off</c>
/// (Änderungsabgleich in den Einstellungen aus).
/// </param>
/// <param name="SyncedAt">Zeitpunkt des letzten erfolgreichen Abgleichs.</param>
public sealed record LexwareSyncState(string State, DateTimeOffset? SyncedAt, IReadOnlyList<LexwareDeviationField> Deviations, string? Error);

/// <summary>Verknüpfungsstatus eines Kunden.</summary>
/// <param name="SyncEnabled">„Mit Lexware Office abgleichen“ ist für den Kunden eingeschaltet (sonst bleibt er nur in EstrichONE).</param>
/// <param name="Sync">Stand des Änderungsabgleichs; nur bei verknüpften Kunden.</param>
/// <param name="Creation">Stand der automatischen Anlage in Lexware Office; nur bei nicht verknüpften Kunden mit eingeschaltetem Abgleich.</param>
public sealed record CustomerLexwareResponse(
    Guid ContactId, LexwareContactLinkResponse? Link, bool CanEdit, bool SyncEnabled = false, LexwareSyncState? Sync = null, LexwareCreationState? Creation = null);

/// <summary>Stand der automatischen Anlage eines Kunden in Lexware Office.</summary>
/// <param name="State">
/// <c>pending</c> (Anlage steht aus), <c>duplicates</c> (ähnliche Kontakte gefunden, nicht automatisch angelegt), <c>failed</c>
/// (Anlage fehlgeschlagen, <paramref name="Error"/> nennt den Grund) oder <c>manual</c> (wird nicht automatisch angelegt).
/// </param>
public sealed record LexwareCreationState(string State, string? Error, DateTimeOffset? At);

/// <summary>Entscheidung zu Abweichungen: <c>estrichOne</c> oder <c>lexware</c>, für alle Felder oder je Feld.</summary>
/// <param name="Source">Für alle offenen Abweichungen dieselbe Seite übernehmen.</param>
/// <param name="Fields">Je Feld die Seite, deren Wert übernommen wird; nicht genannte Felder bleiben offen.</param>
public sealed record ResolveLexwareDeviationsRequest([property: MaxLength(20)] string? Source = null, IReadOnlyDictionary<string, string>? Fields = null);

/// <summary>Kunde mit offenen Abweichungen zum verknüpften Lexware-Kontakt.</summary>
public sealed record LexwareDeviationItem(
    LexwareCustomerSummary Customer, string LexwareContactId, string? LexwareName, string LexwareUrl, IReadOnlyList<LexwareDeviationField> Fields,
    DateTimeOffset? SyncedAt);

public sealed record SetLexwareSyncRequest(bool Enabled);

/// <param name="Preview">Nur zählen, was das Einschalten bewirken würde; nichts ändern.</param>
public sealed record EnableLexwareSyncRequest([property: Required, MinLength(1), MaxLength(2000)] IReadOnlyList<Guid> ContactIds, bool Preview = false);

/// <param name="Changed">Kunden, für die der Abgleich eingeschaltet wurde (in der Vorschau: würde).</param>
/// <param name="WouldCreate">Davon Kunden ohne ähnlichen Kontakt: Sie werden danach automatisch in Lexware Office angelegt (0, wenn die automatische Anlage aus ist).</param>
/// <param name="WithMatches">Davon Kunden mit ähnlichem Kontakt: Sie erscheinen unter „Mögliche Dubletten“.</param>
public sealed record EnableLexwareSyncResponse(int Changed, int WouldCreate = 0, int WithMatches = 0);

public sealed record LexwareCandidatesResponse(IReadOnlyList<LexwareContactCandidate> Candidates, DateTimeOffset? LoadedAt);

public sealed record LinkLexwareContactRequest([property: Required, MaxLength(100)] string LexwareContactId);

/// <param name="CreateAnyway">Bewusst neu anlegen, obwohl die Prüfung mögliche Dubletten gefunden hat.</param>
public sealed record CreateLexwareContactRequest(bool CreateAnyway = false);

/// <param name="Created">Kontakt angelegt und verknüpft. Sonst stehen in <paramref name="Candidates"/> die möglichen Dubletten zur Entscheidung.</param>
public sealed record CreateLexwareContactResponse(bool Created, LexwareContactLinkResponse? Link, IReadOnlyList<LexwareContactCandidate> Candidates);

/// <summary>Angaben eines Kunden, der gerade angelegt wird, für die Prüfung gegen Lexware Office.</summary>
public sealed record CheckLexwareContactRequest(
    [property: MaxLength(200)] string? Name = null, ContactKind? Kind = null, [property: MaxLength(320)] string? Email = null,
    [property: MaxLength(50)] string? Phone = null, [property: MaxLength(200)] string? Street = null, [property: MaxLength(10)] string? PostalCode = null,
    [property: MaxLength(100)] string? City = null, [property: MaxLength(30)] string? VatId = null);

/// <summary>Ergebnis der Prüfung „Gibt es diesen Kunden schon in Lexware Office?“ beim Anlegen eines Kunden.</summary>
/// <param name="Checked">Die Prüfung war möglich. Sonst nennt <paramref name="Message"/> den Grund; das Anlegen wird nie blockiert.</param>
/// <param name="Candidates">Passende Kontakte in Lexware Office mit Grund; <c>LinkedContactId</c> nennt den EstrichONE-Kunden, mit dem ein Kontakt schon verknüpft ist.</param>
/// <param name="CanImport">Der Nutzer darf den Kontakt aus Lexware Office übernehmen (<c>invoices.assign</c> und <c>contacts.edit</c>).</param>
public sealed record CheckLexwareContactResponse(bool Checked, string? Message, IReadOnlyList<LexwareContactCandidate> Candidates, bool CanImport);

public sealed record LexwareContactListResponse(
    IReadOnlyList<LexwareContactSummary> Items, int Page, int PageSize, int Total, DateTimeOffset LoadedAt);

/// <param name="CreateAnyway">Bewusst neu anlegen, obwohl die Prüfung mögliche Dubletten gefunden hat.</param>
public sealed record ImportLexwareContactRequest(bool CreateAnyway = false);

/// <param name="Imported">Kunde angelegt und verknüpft. Sonst stehen in <paramref name="Candidates"/> die möglichen Dubletten zur Entscheidung.</param>
public sealed record ImportLexwareContactResponse(bool Imported, Guid? ContactId, string? CustomerNumber, IReadOnlyList<LexwareCustomerCandidate> Candidates);

/// <param name="Sync">Stand des Änderungsabgleichs dieser Verknüpfung.</param>
public sealed record LexwareLinkedPair(LexwareCustomerSummary Customer, LexwareContactSummary? Lexware, bool MissingInLexware, LexwareSyncState? Sync = null);

/// <param name="Unique">Eindeutiger Treffer: auf beiden Seiten genau ein Gegenstück mit gleicher E-Mail-Adresse oder gleicher Kundennummer.</param>
public sealed record LexwareSuggestion(
    LexwareCustomerSummary Customer, LexwareContactSummary Lexware, int Score, IReadOnlyList<string> Reasons, bool Unique);

/// <summary>Doppelter Eintrag innerhalb eines Systems (nur Hinweis).</summary>
/// <param name="System"><c>estrichOne</c> oder <c>lexware</c>.</param>
public sealed record LexwareInternalDuplicate(
    string System, string FirstId, string FirstName, string? FirstNumber, string SecondId, string SecondName, string? SecondNumber, int Score,
    IReadOnlyList<string> Reasons);

/// <summary>Abgleichübersicht Kunden: Zähler über alle Einträge, Listen auf je 500 Zeilen begrenzt (<paramref name="Truncated"/>).</summary>
/// <param name="OnlyEstrichOne">Kunden mit eingeschaltetem Abgleich, die in Lexware Office noch fehlen.</param>
/// <param name="Excluded">Kunden, die bewusst nur in EstrichONE bleiben („Mit Lexware Office abgleichen“ aus, nicht verknüpft).</param>
/// <param name="Deviations">Verknüpfte Kunden mit offenen Abweichungen (beide Seiten haben ein Feld unterschiedlich; der Nutzer entscheidet).</param>
/// <param name="ChangeSyncError">Der Änderungsabgleich beim Aktualisieren ist fehlgeschlagen; die Übersicht zeigt den gespeicherten Stand.</param>
public sealed record LexwareReconciliationResponse(
    DateTimeOffset LoadedAt, int Linked, int OnlyEstrichOne, int OnlyLexware, int PossibleDuplicates, int UniqueMatches,
    IReadOnlyList<LexwareLinkedPair> LinkedItems, IReadOnlyList<LexwareCustomerSummary> OnlyEstrichOneItems,
    IReadOnlyList<LexwareContactSummary> OnlyLexwareItems, IReadOnlyList<LexwareSuggestion> Suggestions,
    IReadOnlyList<LexwareInternalDuplicate> InternalDuplicates, bool Truncated, int Excluded = 0,
    IReadOnlyList<LexwareCustomerSummary>? ExcludedItems = null, int Deviations = 0, IReadOnlyList<LexwareDeviationItem>? DeviationItems = null,
    LexwareChangeSync ChangeSync = LexwareChangeSync.Both, string? ChangeSyncError = null);

public sealed record LexwareLinkPair(Guid ContactId, [property: Required, MaxLength(100)] string LexwareContactId);

/// <param name="Pairs">Paare aus der Vorschau; verknüpft wird nur, was weiterhin ein eindeutiger Treffer ist.</param>
public sealed record LinkUniqueLexwareRequest([property: Required, MaxLength(2000)] IReadOnlyList<LexwareLinkPair> Pairs);

public sealed record LinkUniqueLexwareResponse(int Linked, int Skipped);

/// <summary>Beleg aus Lexware Office, der einer Bauakte zugeordnet ist (nur Anzeige; Belege entstehen in Lexware Office).</summary>
/// <param name="Type"><c>invoice</c>, <c>quotation</c>, <c>creditnote</c> oder <c>orderconfirmation</c>.</param>
/// <param name="DueDate">Fälligkeit der Rechnung bzw. „gültig bis“ des Angebots.</param>
/// <param name="Status">
/// <c>draft</c>, <c>open</c>, <c>overdue</c> (Rechnung offen und Fälligkeit überschritten), <c>paid</c>, <c>paidoff</c>, <c>voided</c>;
/// bei Angeboten auch <c>accepted</c> und <c>rejected</c>.
/// </param>
public sealed record LexwareVoucherResponse(
    Guid Id, string LexwareId, string Type, string? Number, string? Title, DateOnly? VoucherDate, DateOnly? DueDate, decimal? NetAmount,
    decimal? GrossAmount, string Currency, string Status, string? ContactName, string Url, DateTimeOffset? SyncedAt, string? SyncError);

/// <param name="SyncEnabled">„Mit Lexware Office abgleichen“ ist für den Kunden eingeschaltet.</param>
public sealed record LexwareBauakteCustomer(Guid Id, string Name, string? LexwareContactId, bool SyncEnabled = false);

/// <summary>
/// Daten des verknüpften Lexware-Kontakts für die Bauakte (nur Anzeige), aus dem zuletzt abgeglichenen Stand: kein Aufruf
/// bei Lexware Office. Anschrift und E-Mail-Adresse sind leer, solange noch kein Änderungsabgleich lief.
/// </summary>
/// <param name="Url">Adresse des Kontakts in der Lexware-Oberfläche.</param>
/// <param name="DifferingFields">Felder, in denen Lexware Office vom Kunden in EstrichONE abweicht (<c>name</c>, <c>street</c>, <c>postalCode</c>, <c>city</c>, <c>email</c> …).</param>
/// <param name="Sync">Stand des Änderungsabgleichs (zuletzt abgeglichen, offene Abweichungen).</param>
public sealed record LexwareBauakteContact(
    string LexwareContactId, string? Name, string? Number, string Url, string? Street, string? PostalCode, string? City, string? Email,
    IReadOnlyList<string> DifferingFields, LexwareSyncState Sync);

/// <param name="SyncError">Abgleich mit Lexware Office fehlgeschlagen; die Liste zeigt den zuletzt gespeicherten Stand.</param>
/// <param name="Contact">Verknüpfter Lexware-Kontakt des Kunden der Bauakte; fehlt ohne Verknüpfung.</param>
/// <param name="Summary">Summen der zugeordneten Belege als Textzeile, z. B. „2 Rechnungen: offen 1.190,00 €. 1 Angebot: angenommen 5.355,00 €“.</param>
public sealed record BauakteLexwareResponse(
    Guid BauakteId, bool Configured, bool CanAssign, LexwareBauakteCustomer? Customer, IReadOnlyList<LexwareVoucherResponse> Vouchers, string? SyncError,
    LexwareBauakteContact? Contact = null, string? Summary = null);

/// <summary>Beleg aus der Belegliste von Lexware Office (Kunde, Zuordnungsdialog).</summary>
/// <param name="GrossAmount">Bruttobetrag laut Belegliste.</param>
/// <param name="AssignedBauakteId">Bauakte, der der Beleg bereits zugeordnet ist.</param>
/// <param name="Suggested">Die Bauaktennummer kommt in Titel, Einleitung oder Schlusstext vor (Vorschlag für die Zuordnung).</param>
public sealed record LexwareVoucherItem(
    string LexwareId, string Type, string? Number, DateOnly? VoucherDate, DateOnly? DueDate, decimal? GrossAmount, string Currency, string Status,
    string? ContactName, string Url, Guid? AssignedBauakteId, string? AssignedBauakte, bool Suggested = false);

/// <summary>Belege eines Kunden aus Lexware Office (nur Anzeige).</summary>
/// <param name="Linked">Der Kunde ist mit einem Lexware-Kontakt verknüpft; sonst ist die Liste leer.</param>
public sealed record CustomerVouchersResponse(bool Linked, IReadOnlyList<LexwareVoucherItem> Vouchers, DateTimeOffset? LoadedAt);

/// <param name="CustomerLinked">Der Kunde der Bauakte ist verknüpft; sonst geht nur die Suche über die Belegnummer.</param>
public sealed record BauakteVoucherCandidatesResponse(bool CustomerLinked, IReadOnlyList<LexwareVoucherItem> Vouchers);

public sealed record AssignLexwareVoucherRequest([property: Required, MaxLength(100)] string LexwareId, [property: Required, MaxLength(30)] string Type);

/// <summary>Summen einer Belegliste als Textzeile (wie in der Weboberfläche): Entwürfe und stornierte Belege zählen nicht in die Beträge.</summary>
public static class LexwareVoucherSummary
{
    private static readonly System.Globalization.NumberFormatInfo German = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };

    public static string Euro(decimal value) => value.ToString("#,##0.00", German) + " €";

    public static string? Text(IReadOnlyList<LexwareVoucherResponse> vouchers)
    {
        List<LexwareVoucherResponse> Of(string type, params string[] statuses) => vouchers.Where(v => v.Type == type && statuses.Contains(v.Status)).ToList();
        static decimal Sum(IEnumerable<LexwareVoucherResponse> list) => list.Sum(v => v.GrossAmount ?? 0m);
        static string Count(int n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var parts = new List<string>();
        var invoices = vouchers.Count(v => v.Type == LexwareVoucherTypes.Invoice);
        if (invoices > 0)
        {
            var open = Of(LexwareVoucherTypes.Invoice, "open", "overdue");
            var overdue = Of(LexwareVoucherTypes.Invoice, "overdue");
            var paid = Of(LexwareVoucherTypes.Invoice, "paid", "paidoff");
            var drafts = Of(LexwareVoucherTypes.Invoice, "draft");
            var details = new[]
            {
                open.Count > 0 ? $"offen {Euro(Sum(open))}" : null, overdue.Count > 0 ? $"davon überfällig {Euro(Sum(overdue))}" : null,
                paid.Count > 0 ? $"bezahlt {Euro(Sum(paid))}" : null, drafts.Count > 0 ? $"{Count(drafts.Count)} im Entwurf" : null,
            }.Where(d => d is not null).ToList();
            parts.Add($"{Count(invoices)} {(invoices == 1 ? "Rechnung" : "Rechnungen")}{(details.Count > 0 ? ": " + string.Join(", ", details) : string.Empty)}");
        }

        var quotations = vouchers.Count(v => v.Type == LexwareVoucherTypes.Quotation);
        if (quotations > 0)
        {
            var accepted = Of(LexwareVoucherTypes.Quotation, "accepted");
            var open = Of(LexwareVoucherTypes.Quotation, "open").Count;
            var rejected = Of(LexwareVoucherTypes.Quotation, "rejected").Count;
            var details = new[]
            {
                open > 0 ? $"{Count(open)} offen" : null, accepted.Count > 0 ? $"angenommen {Euro(Sum(accepted))}" : null,
                rejected > 0 ? $"{Count(rejected)} abgelehnt" : null,
            }.Where(d => d is not null).ToList();
            parts.Add($"{Count(quotations)} {(quotations == 1 ? "Angebot" : "Angebote")}{(details.Count > 0 ? ": " + string.Join(", ", details) : string.Empty)}");
        }

        var others = vouchers.Count - invoices - quotations;
        if (others > 0)
        {
            parts.Add($"{Count(others)} {(others == 1 ? "weiterer Beleg" : "weitere Belege")}");
        }

        return parts.Count == 0 ? null : string.Join(". ", parts);
    }
}

/// <summary>
/// Lexware Office: Einstellungen (nur mit <c>settings.integrations</c>), Kundenverknüpfung mit beidseitiger
/// Dublettenprüfung, Abgleich späterer Änderungen an verknüpften Kunden und die Anzeige von Rechnungen und Angeboten an
/// Kunde und Bauakte (Liste, Status, PDF). Belege werden nur gelesen und in EstrichONE Bauakten zugeordnet; angelegt oder
/// geändert werden sie ausschließlich in Lexware Office.
/// </summary>
internal static class LexwareEndpoints
{
    private const string Tag = "Lexware Office";
    private const int ListLimit = 500;

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/lexware/status", StatusAsync).WithTags(Tag).WithName("GetLexwareStatus")
            .WithSummary("Stand der Lexware-Office-Anbindung für die Oberfläche (alle Angemeldeten)");

        // Einstellungen bleiben auch bei abgeschaltetem Modul erreichbar (erst einrichten, dann einschalten).
        var settings = api.MapGroup("/settings/integrations/lexware").WithTags(Tag).RequirePermission(Permissions.SettingsIntegrations)
            .AddEndpointFilter(GuardAsync);
        settings.MapGet("/", GetSettingsAsync).WithName("GetLexwareSettings").WithSummary("Einstellungen (der API-Schlüssel wird nie zurückgegeben)");
        settings.MapPut("/", SaveSettingsAsync).WithName("SaveLexwareSettings")
            .WithSummary("Speichern (Schlüssel verschlüsselt; ohne Angabe bleibt der gespeicherte Schlüssel)")
            .ProducesProblem(StatusCodes.Status412PreconditionFailed).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        settings.MapPost("/test", TestAsync).WithName("TestLexwareConnection")
            .WithSummary("Verbindung testen (GET /v1/profile): liefert den Firmennamen der Lexware-Organisation oder die Fehlerursache")
            .ProducesProblem(StatusCodes.Status409Conflict);
        var reconciliation = settings.MapGroup("/reconciliation").RequireModule(AppModules.Lexware);
        reconciliation.MapGet("/", ReconciliationAsync).WithName("GetLexwareReconciliation")
            .WithSummary("Abgleichübersicht Kunden: verknüpft, nur in EstrichONE, nur in Lexware, mögliche Dubletten (auch innerhalb eines Systems)")
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status424FailedDependency)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        reconciliation.MapPost("/link-unique", LinkUniqueAsync).WithName("LinkUniqueLexwareContacts")
            .WithSummary("Eindeutige Treffer aus der Vorschau verknüpfen (genau ein Gegenstück mit gleicher E-Mail-Adresse oder Kundennummer)")
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status424FailedDependency)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        reconciliation.MapPost("/enable-sync", EnableSyncAsync).WithName("EnableLexwareSyncForCustomers")
            .WithSummary("„Mit Lexware Office abgleichen“ für ausgewählte Kunden einschalten (überträgt nichts; die Kunden erscheinen danach im Abgleich)");

        var customers = api.MapGroup("/lexware/customers/{contactId:guid}").WithTags(Tag).RequireModule(AppModules.Lexware).AddEndpointFilter(GuardAsync);
        customers.MapGet("/", CustomerAsync).WithName("GetCustomerLexwareLink").WithSummary("Verknüpfung des Kunden mit Lexware Office")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.InvoicesRead);
        customers.MapGet("/vouchers", CustomerVouchersAsync).WithName("ListCustomerLexwareVouchers")
            .WithSummary("Rechnungen, Angebote, Gutschriften und Auftragsbestätigungen des verknüpften Lexware-Kontakts (nur Anzeige, kurzer Cache)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status424FailedDependency).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequirePermission(Permissions.InvoicesRead);
        customers.MapGet("/candidates", CandidatesAsync).WithName("FindLexwareContactCandidates")
            .WithSummary("Dublettenprüfung in Lexware Office für diesen Kunden; mit q gezielte Suche nach Name, E-Mail-Adresse oder Kundennummer")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status424FailedDependency).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequirePermission(Permissions.InvoicesAssign);
        customers.MapPost("/link", LinkCustomerAsync).WithName("LinkCustomerToLexware")
            .WithSummary("Kunde mit vorhandenem Lexware-Kontakt verknüpfen (je Seite höchstens eine Verknüpfung)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status424FailedDependency).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequirePermission(Permissions.InvoicesAssign);
        customers.MapPost("/create", CreateContactAsync).WithName("CreateLexwareContact")
            .WithSummary("Kunde in Lexware Office anlegen; vorher immer Dublettenprüfung, bei Treffern nur mit createAnyway")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status424FailedDependency).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequirePermission(Permissions.InvoicesAssign);
        customers.MapPut("/sync", SetSyncAsync).WithName("SetCustomerLexwareSync")
            .WithSummary("„Mit Lexware Office abgleichen“ für den Kunden ein- oder ausschalten (eine bestehende Verknüpfung bleibt, bis sie gelöst wird)")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.InvoicesAssign);
        customers.MapPost("/create/retry", RetryCreateAsync).WithName("RetryLexwareContactCreation")
            .WithSummary("Automatische Anlage in Lexware Office erneut versuchen (prüft vorher erneut auf Dubletten; legt nur ohne Treffer an)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(Permissions.InvoicesAssign);
        customers.MapPost("/sync/run", RunSyncAsync).WithName("RunCustomerLexwareSync")
            .WithSummary("Änderungsabgleich für diesen Kunden jetzt ausführen (liest den Lexware-Kontakt frisch)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status424FailedDependency).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequirePermission(Permissions.InvoicesAssign);
        customers.MapPost("/sync/resolve", ResolveSyncAsync).WithName("ResolveCustomerLexwareDeviations")
            .WithSummary("Abweichungen entscheiden: je Feld oder gesamt EstrichONE oder Lexware übernehmen (Lexware übernehmen verlangt zusätzlich contacts.edit)")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesProblem(StatusCodes.Status424FailedDependency)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable).RequirePermission(Permissions.InvoicesAssign);
        customers.MapGet("/vouchers/{lexwareId}/document", CustomerVoucherDocumentAsync).WithName("GetCustomerLexwareVoucherDocument")
            .WithSummary("Beleg des verknüpften Lexware-Kontakts als PDF (nur lesend durchgereicht; download=true als Anhang). Für Entwürfe gibt es kein PDF")
            .Produces<FileContentHttpResult>(StatusCodes.Status200OK, "application/pdf")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status424FailedDependency).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequirePermission(Permissions.InvoicesRead);
        customers.MapDelete("/link", UnlinkCustomerAsync).WithName("UnlinkCustomerFromLexware")
            .WithSummary("Verknüpfung lösen (in Lexware Office wird nichts gelöscht)").ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(Permissions.InvoicesAssign);

        var contacts = api.MapGroup("/lexware/contacts").WithTags(Tag).RequireModule(AppModules.Lexware).AddEndpointFilter(GuardAsync);
        contacts.MapGet("/", ListContactsAsync).WithName("ListLexwareContacts")
            .WithSummary("Kunden aus Lexware Office, seitenweise mit Suche (Liste kurz zwischengespeichert); unlinked=true nur ohne Verknüpfung")
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status424FailedDependency)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable).RequirePermission(Permissions.InvoicesAssign);
        contacts.MapPost("/check", CheckContactAsync).WithName("CheckLexwareContact")
            .WithSummary("Beim Anlegen eines Kunden prüfen, ob es ihn in Lexware Office schon gibt (Dublettenprüfung mit Gründen; blockiert nie, auch wenn Lexware Office nicht erreichbar ist)")
            .RequirePermission(Permissions.ContactsEdit);
        contacts.MapGet("/{lexwareContactId}/candidates", CustomerCandidatesAsync).WithName("FindCustomerCandidatesForLexwareContact")
            .WithSummary("Dublettenprüfung in EstrichONE für einen Lexware-Kontakt")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status424FailedDependency).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequirePermission(Permissions.InvoicesAssign);
        contacts.MapPost("/{lexwareContactId}/import", ImportContactAsync).WithName("ImportLexwareContact")
            .WithSummary("Lexware-Kontakt als Kunde nach EstrichONE übernehmen; vorher immer Dublettenprüfung, bei Treffern nur mit createAnyway")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status424FailedDependency).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequirePermission(Permissions.InvoicesAssign);

        var bauakte = api.MapGroup("/bauakten/{id:guid}/lexware").WithTags(Tag).RequireModule(AppModules.Lexware).AddEndpointFilter(GuardAsync);
        bauakte.MapGet("/", BauakteAsync).WithName("GetBauakteLexware")
            .WithSummary("Rechnungen und Angebote der Bauakte mit Statusabgleich (kurzer Cache; refresh=true gleicht alle sofort ab)")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.InvoicesRead);
        bauakte.MapGet("/candidates", CandidatesForBauakteAsync).WithName("ListLexwareVoucherCandidates")
            .WithSummary("Belege zum Zuordnen: Belege des Kunden der Bauakte (mit Vorschlag, wenn die Bauaktennummer im Text vorkommt) oder Suche über die Belegnummer")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status424FailedDependency).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequirePermission(Permissions.InvoicesAssign);
        bauakte.MapPost("/vouchers/assign", AssignVoucherAsync).WithName("AssignLexwareVoucher")
            .WithSummary("Beleg aus Lexware Office der Bauakte zuordnen (nur in EstrichONE; in Lexware Office wird nichts geschrieben)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status424FailedDependency).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequirePermission(Permissions.InvoicesAssign);
        bauakte.MapGet("/vouchers/{linkId:guid}/document", BauakteVoucherDocumentAsync).WithName("GetBauakteLexwareVoucherDocument")
            .WithSummary("Der Bauakte zugeordneter Beleg als PDF (nur lesend durchgereicht; download=true als Anhang). Für Entwürfe gibt es kein PDF")
            .Produces<FileContentHttpResult>(StatusCodes.Status200OK, "application/pdf")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status424FailedDependency).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequirePermission(Permissions.InvoicesRead);
        bauakte.MapDelete("/vouchers/{linkId:guid}", UnassignVoucherAsync).WithName("UnassignLexwareVoucher")
            .WithSummary("Zuordnung des Belegs zur Bauakte lösen (in Lexware Office ändert sich nichts)")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.InvoicesAssign);
    }

    // ---------- Status und Einstellungen ----------

    private static async Task<Ok<LexwareStatusResponse>> StatusAsync(LexwareService lexware, ISystemSettings system, IUserPermissions permissions,
        CancellationToken cancellationToken)
    {
        var granted = await permissions.GetAsync(cancellationToken);
        var enabled = (await system.GetAsync(cancellationToken)).Modules.IsEnabled(AppModules.Lexware);
        var row = await lexware.SettingsAsync(cancellationToken);
        return TypedResults.Ok(new LexwareStatusResponse(enabled, row.Configured, granted.Contains(Permissions.InvoicesRead),
            granted.Contains(Permissions.InvoicesAssign), granted.Contains(Permissions.SettingsIntegrations), enabled && row.NewCustomersSync));
    }

    private static async Task<LexwareSettingsResponse> ToResponseAsync(LexwareService lexware, ISystemSettings system, LexwareSettingsRow row,
        CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        string baseUrl;
        try
        {
            baseUrl = lexware.ResolveBaseUri(row.BaseUrl).ToString().TrimEnd('/');
        }
        catch (LexwareException)
        {
            baseUrl = row.BaseUrl ?? LexwareOptions.DefaultBaseUrl;
        }

        return new LexwareSettingsResponse((await system.GetAsync(cancellationToken)).Modules.IsEnabled(AppModules.Lexware), row.Configured, baseUrl,
            !string.IsNullOrWhiteSpace(row.BaseUrl), row.OrganizationName, row.LastTestAt, row.LastTestOk,
            row.LastTestError, await db.Set<LexwareContactLink>().CountAsync(cancellationToken),
            await db.Set<LexwareVoucherLink>().CountAsync(cancellationToken), LexwareService.KeyHelpUrl, row.Version, row.NewCustomersSync, row.ChangeSync,
            row.LastChangeSyncAt, row.LastChangeSyncError,
            row.ChangeSync == LexwareChangeSync.Off ? 0 : await db.Set<LexwareContactLink>().CountAsync(l => l.DeviationCount > 0, cancellationToken),
            row.AutoCreate);
    }

    private static async Task<Ok<LexwareSettingsResponse>> GetSettingsAsync(LexwareService lexware, ISystemSettings system, CancellationToken cancellationToken) =>
        TypedResults.Ok(await ToResponseAsync(lexware, system, await lexware.SettingsAsync(cancellationToken), cancellationToken));

    private static async Task<Results<Ok<LexwareSettingsResponse>, ProblemHttpResult>> SaveSettingsAsync(SaveLexwareSettingsRequest request,
        LexwareService lexware, ISystemSettings system, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var row = await lexware.SettingsForUpdateAsync(cancellationToken);
        if (request.Version is { } version && version != row.Version)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Geändert",
                detail: "Die Einstellungen wurden inzwischen geändert. Bitte laden Sie die Seite neu.", extensions: Code("VERSION_CONFLICT"));
        }

        var baseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? null : request.BaseUrl.Trim().TrimEnd('/');
        if (string.Equals(baseUrl, LexwareOptions.DefaultBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = null;
        }

        try
        {
            lexware.ResolveBaseUri(baseUrl);
        }
        catch (LexwareException exception)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: Tag, detail: exception.Message,
                extensions: Code("LEXWARE_" + exception.Code));
        }

        var key = string.IsNullOrWhiteSpace(request.ApiKey) ? null : request.ApiKey.Trim();
        var keyChange = request.ClearApiKey ? "entfernt" : key is not null ? "neu hinterlegt" : null;
        var connectionChanged = keyChange is not null || baseUrl != row.BaseUrl;
        if (request.ClearApiKey)
        {
            row.ProtectedApiKey = null;
        }
        else if (key is not null)
        {
            row.ProtectedApiKey = lexware.Protect(key);
        }

        row.BaseUrl = baseUrl;
        row.NewCustomersSync = request.NewCustomersSync ?? row.NewCustomersSync;
        var changeSync = request.ChangeSync ?? row.ChangeSync;
        if (!Enum.IsDefined(changeSync))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: Tag,
                detail: "Die Richtung des Änderungsabgleichs ist unbekannt.", extensions: Code("LEXWARE_INVALID_CHANGE_SYNC"));
        }

        // Aus heißt: Es gilt kein abgeglichener Stand mehr. Beim Wiedereinschalten werden vorhandene Unterschiede als
        // Abweichungen gezeigt und nicht still übernommen.
        var forget = changeSync == LexwareChangeSync.Off && row.ChangeSync != LexwareChangeSync.Off;
        // Wird die automatische Anlage eingeschaltet, gelten die bis dahin nicht angelegten Kunden als Bestand: Sie werden
        // nicht nachträglich in einem Zug angelegt, sondern bleiben unter „Nur in EstrichONE“.
        var keepExisting = request.AutoCreate == true && !row.AutoCreate;
        row.AutoCreate = request.AutoCreate ?? row.AutoCreate;
        row.ChangeSync = changeSync;
        if (connectionChanged)
        {
            row.LastTestAt = null;
            row.LastTestOk = null;
            row.LastTestError = null;
            row.OrganizationId = null;
            row.OrganizationName = null;
            lexware.ForgetContacts();
        }

        // Der Schlüssel selbst gehört nie ins Protokoll.
        audit.Record("lexware.settings_changed", currentUser.UserId, "settings", "lexware", new Dictionary<string, string?>
        {
            ["apiKey"] = keyChange, ["baseUrl"] = baseUrl ?? LexwareOptions.DefaultBaseUrl, ["newCustomersSync"] = row.NewCustomersSync ? "ja" : "nein",
            ["changeSync"] = row.ChangeSync.ToString(), ["autoCreate"] = row.AutoCreate ? "ja" : "nein",
        });
        if (keepExisting)
        {
            var now = lexware.Clock.GetUtcNow();
            foreach (var id in await LexwareAutoCreateService.Pending(lexware.Db).Select(c => c.Id).ToListAsync(cancellationToken))
            {
                lexware.Db.Add(new LexwareContactCreation { ContactId = id, State = LexwareContactCreation.Skipped, UpdatedAt = now });
            }
        }

        await lexware.Db.SaveChangesAsync(cancellationToken);
        if (forget)
        {
            await LexwareChangeSyncService.ForgetAsync(lexware.Db.Set<LexwareContactLink>(), cancellationToken);
        }

        return TypedResults.Ok(await ToResponseAsync(lexware, system, row, cancellationToken));
    }

    private static async Task<Results<Ok<LexwareTestResponse>, ProblemHttpResult>> TestAsync(LexwareService lexware, CancellationToken cancellationToken)
    {
        var row = await lexware.SettingsForUpdateAsync(cancellationToken);
        if (!row.Configured)
        {
            return Problem(LexwareService.NotConfigured());
        }

        var at = lexware.Clock.GetUtcNow();
        row.LastTestAt = at;
        try
        {
            var profile = await lexware.Client.ProfileAsync(lexware.Connection(row), cancellationToken);
            row.LastTestOk = true;
            row.LastTestError = null;
            row.OrganizationId = LexwareService.Cut(profile.OrganizationId, 100);
            row.OrganizationName = LexwareService.Cut(profile.CompanyName, LexwareContactLink.MaxName);
        }
        catch (LexwareException exception)
        {
            row.LastTestOk = false;
            row.LastTestError = exception.Message;
        }

        await lexware.Db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new LexwareTestResponse(row.LastTestOk == true, row.OrganizationName, row.LastTestError, at));
    }

    // ---------- Kunde und Lexware-Kontakt ----------

    private static LexwareCustomerSummary Summary(Contact c, string? lexwareContactId) => new(
        c.Id, c.Name, c.Kind == ContactKind.Company, c.CustomerNumber, c.BillingAddress?.Street ?? c.Street, c.BillingAddress?.PostalCode ?? c.PostalCode,
        c.BillingAddress?.City ?? c.City, c.Email, c.Phone, c.ArchivedAt is not null, lexwareContactId, c.LexwareSync);

    private static LexwareContactSummary Summary(LexwareService lexware, LexwareSettingsRow row, LexwareContact c, Guid? linkedId = null,
        string? linkedName = null) => new(
        c.Id, c.Name, c.IsCompany, c.CustomerNumber, c.Street, c.PostalCode, c.City, c.Emails.Count > 0 ? c.Emails[0] : null, c.Phones.Count > 0 ? c.Phones[0] : null,
        lexware.ContactUrl(row, c.Id), linkedId, linkedName);

    private static LexwareContactLinkResponse ToResponse(LexwareService lexware, LexwareSettingsRow row, LexwareContactLink link) => new(
        link.LexwareContactId, link.LexwareName, link.LexwareNumber, lexware.ContactUrl(row, link.LexwareContactId), link.Origin, link.LinkedAt);

    /// <summary>Verknüpfte Lexware-Kontakte: ID → (Kunde, Name).</summary>
    private static async Task<Dictionary<string, (Guid Id, string Name)>> LinkedAsync(EstrichOneDbContext db, CancellationToken cancellationToken) =>
        (await db.Set<LexwareContactLink>().AsNoTracking()
            .Join(db.Set<Contact>().AsNoTracking(), l => l.ContactId, c => c.Id, (l, c) => new { l.LexwareContactId, c.Id, c.Name })
            .ToListAsync(cancellationToken)).ToDictionary(x => x.LexwareContactId, x => (x.Id, x.Name), StringComparer.Ordinal);

    private static Task<Contact?> CustomerEntityAsync(EstrichOneDbContext db, Guid contactId, CancellationToken cancellationToken) =>
        db.Set<Contact>().Include(c => c.Persons).SingleOrDefaultAsync(c => c.Id == contactId, cancellationToken);

    private static async Task<Results<Ok<CustomerLexwareResponse>, ProblemHttpResult>> CustomerAsync(Guid contactId, LexwareService lexware,
        IUserPermissions permissions, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        if (!await db.Set<Contact>().AnyAsync(c => c.Id == contactId, cancellationToken))
        {
            return CustomerNotFound();
        }

        return TypedResults.Ok(await CustomerResponseAsync(lexware, contactId, await permissions.HasAsync(Permissions.InvoicesAssign, cancellationToken),
            cancellationToken));
    }

    private static async Task<CustomerLexwareResponse> CustomerResponseAsync(LexwareService lexware, Guid contactId, bool canAssign,
        CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        var row = await lexware.SettingsAsync(cancellationToken);
        var link = await db.Set<LexwareContactLink>().AsNoTracking().SingleOrDefaultAsync(l => l.ContactId == contactId, cancellationToken);
        var contact = await db.Set<Contact>().AsNoTracking().SingleAsync(c => c.Id == contactId, cancellationToken);
        LexwareCreationState? creation = null;
        if (link is null && contact.LexwareSync && contact.ArchivedAt is null && contact.AnonymizedAt is null)
        {
            var note = await db.Set<LexwareContactCreation>().AsNoTracking().SingleOrDefaultAsync(x => x.ContactId == contactId, cancellationToken);
            creation = note is null
                ? new LexwareCreationState(row.Configured && row.AutoCreate ? "pending" : "manual", null, null)
                : new LexwareCreationState(note.State == LexwareContactCreation.Skipped ? "manual" : note.State, note.Error, note.UpdatedAt);
        }

        return new CustomerLexwareResponse(contactId, link is null ? null : ToResponse(lexware, row, link), row.Configured && canAssign, contact.LexwareSync,
            link is null ? null : SyncState(row, link, contact), creation);
    }

    private static async Task<Results<Ok<CustomerLexwareResponse>, ProblemHttpResult>> RetryCreateAsync(Guid contactId, LexwareService lexware,
        LexwareAutoCreateService creation, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        if (await db.Set<Contact>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == contactId, cancellationToken) is not { } contact)
        {
            return CustomerNotFound();
        }

        if (!contact.LexwareSync)
        {
            return Problem(LexwareService.SyncDisabled(contact.Name));
        }

        lexware.Connection(await lexware.SettingsAsync(cancellationToken)); // ohne Schlüssel: klare Meldung
        // Der Versuch prüft wieder zuerst auf Dubletten; ein inzwischen angelegter Kontakt wird gefunden und nicht doppelt angelegt.
        await db.Set<LexwareContactCreation>().Where(x => x.ContactId == contactId && x.State != LexwareContactCreation.Failed).ExecuteDeleteAsync(cancellationToken);
        await creation.TryAsync(contactId, currentUser.UserId, cancellationToken);
        db.ChangeTracker.Clear();
        return TypedResults.Ok(await CustomerResponseAsync(lexware, contactId, true, cancellationToken));
    }

    private static List<LexwareDeviationField> DeviationFields(LexwareContactLink link) =>
        LexwareContactSync.Deviations(link.Deviations).Select(d => new LexwareDeviationField(d.Field, LexwareSyncFields.Label(d.Field), d.EstrichOne, d.Lexware))
            .ToList();

    /// <summary>Stand des Änderungsabgleichs für die Anzeige am Kunden und in der Abgleichübersicht.</summary>
    private static LexwareSyncState SyncState(LexwareSettingsRow row, LexwareContactLink link, Contact contact)
    {
        if (row.ChangeSync == LexwareChangeSync.Off)
        {
            return new LexwareSyncState("off", link.SyncedAt, [], null);
        }

        if (!contact.LexwareSync || contact.ArchivedAt is not null || contact.AnonymizedAt is not null)
        {
            return new LexwareSyncState(contact.LexwareSync ? "paused" : "disabled", link.SyncedAt, [], null);
        }

        if (link.RemoteState is { } remote)
        {
            return new LexwareSyncState(remote, link.SyncedAt, [], null);
        }

        if (link.SyncError is not null)
        {
            return new LexwareSyncState("error", link.SyncedAt, DeviationFields(link), link.SyncError);
        }

        var deviations = DeviationFields(link);
        return new LexwareSyncState(deviations.Count > 0 ? "deviations" : link.SyncedAt is null || link.LocalVersion != contact.Version ? "pending" : "synced",
            link.SyncedAt, deviations, null);
    }

    private static async Task<Results<Ok<CustomerLexwareResponse>, ProblemHttpResult>> RunSyncAsync(Guid contactId, LexwareService lexware,
        LexwareChangeSyncService changes, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        await SyncNowAsync(contactId, lexware, changes, currentUser, null, cancellationToken);

    private static async Task<Results<Ok<CustomerLexwareResponse>, ProblemHttpResult>> ResolveSyncAsync(Guid contactId, ResolveLexwareDeviationsRequest request,
        LexwareService lexware, LexwareChangeSyncService changes, IUserPermissions permissions, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        static bool Side(string? value) => value is LexwareContactSync.EstrichOne or LexwareContactSync.Lexware;
        var choices = new Dictionary<string, string>(StringComparer.Ordinal);
        if (request.Source is not null)
        {
            foreach (var field in LexwareSyncFields.All)
            {
                choices[field] = request.Source;
            }
        }

        foreach (var (field, source) in request.Fields ?? new Dictionary<string, string>())
        {
            choices[field] = source;
        }

        if (choices.Count == 0 || choices.Any(c => !LexwareSyncFields.All.Contains(c.Key) || !Side(c.Value)))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: Tag,
                detail: "Bitte wählen Sie je Feld, ob der Wert aus EstrichONE oder aus Lexware Office übernommen wird.", extensions: Code("LEXWARE_INVALID_CHOICE"));
        }

        if (choices.ContainsValue(LexwareContactSync.Lexware) && !await permissions.HasAsync(Permissions.ContactsEdit, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Nicht erlaubt",
                detail: "Um Werte aus Lexware Office in den Kunden zu übernehmen, benötigen Sie zusätzlich das Recht, Kunden zu bearbeiten.",
                extensions: Code("PERMISSION_DENIED"));
        }

        return await SyncNowAsync(contactId, lexware, changes, currentUser, choices, cancellationToken);
    }

    private static async Task<Results<Ok<CustomerLexwareResponse>, ProblemHttpResult>> SyncNowAsync(Guid contactId, LexwareService lexware,
        LexwareChangeSyncService changes, ICurrentUser currentUser, IReadOnlyDictionary<string, string>? choices, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        if (await db.Set<Contact>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == contactId, cancellationToken) is not { } contact)
        {
            return CustomerNotFound();
        }

        if (!await db.Set<LexwareContactLink>().AnyAsync(l => l.ContactId == contactId, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Nicht verknüpft",
                detail: "Dieser Kunde ist nicht mit Lexware Office verknüpft.", extensions: Code("LEXWARE_NOT_LINKED"));
        }

        if ((await lexware.SettingsAsync(cancellationToken)).ChangeSync == LexwareChangeSync.Off)
        {
            return Problem(new LexwareException("CHANGE_SYNC_OFF",
                "Der Änderungsabgleich ist in den Einstellungen von Lexware Office ausgeschaltet."));
        }

        if (!LexwareChangeSyncService.Eligible(contact))
        {
            return Problem(contact.LexwareSync
                ? new LexwareException("SYNC_PAUSED", $"Der Kunde „{contact.Name}“ ist archiviert. Archivierte Kunden werden nicht abgeglichen.")
                : LexwareService.SyncDisabled(contact.Name));
        }

        try
        {
            await changes.SyncCustomerAsync(contactId, null, currentUser.UserId, choices, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(new LexwareException("CUSTOMER_CHANGED", "Der Kunde wurde gerade an anderer Stelle geändert. Bitte versuchen Sie es erneut."));
        }

        db.ChangeTracker.Clear();
        return TypedResults.Ok(await CustomerResponseAsync(lexware, contactId, true, cancellationToken));
    }

    private static List<LexwareContactCandidate> Candidates(LexwareService lexware, LexwareSettingsRow row, Contact contact,
        IEnumerable<LexwareContact> pool, Dictionary<string, (Guid Id, string Name)> linked, bool keepUnmatched)
    {
        var subject = LexwareService.Party(contact);
        var result = new List<LexwareContactCandidate>();
        foreach (var other in pool)
        {
            var match = LexwareMatching.Compare(subject, LexwareService.Party(other));
            if (match is null && !keepUnmatched)
            {
                continue;
            }

            var owner = linked.TryGetValue(other.Id, out var found) ? found : ((Guid Id, string Name)?)null;
            result.Add(new LexwareContactCandidate(Summary(lexware, row, other, owner?.Id, owner?.Name), match?.Score ?? 0, match?.Reasons ?? [],
                match?.Strong ?? false));
        }

        return result.OrderByDescending(c => c.Score).ThenBy(c => c.Contact.Name, StringComparer.OrdinalIgnoreCase).Take(25).ToList();
    }

    private static async Task<Results<Ok<LexwareCandidatesResponse>, ProblemHttpResult>> CandidatesAsync(Guid contactId, LexwareService lexware,
        CancellationToken cancellationToken, [FromQuery, MaxLength(200)] string? q = null)
    {
        var db = lexware.Db;
        if (await CustomerEntityAsync(db, contactId, cancellationToken) is not { } contact)
        {
            return CustomerNotFound();
        }

        var row = await lexware.SettingsAsync(cancellationToken);
        var linked = await LinkedAsync(db, cancellationToken);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var hits = await lexware.SearchAsync(row, q, cancellationToken);
            return TypedResults.Ok(new LexwareCandidatesResponse(Candidates(lexware, row, contact, hits.Where(c => !c.Archived), linked, true), null));
        }

        var (contacts, loadedAt) = await lexware.ContactsAsync(row, refresh: false, cancellationToken);
        return TypedResults.Ok(new LexwareCandidatesResponse(Candidates(lexware, row, contact, contacts, linked, false), loadedAt));
    }

    private static async Task<Results<Ok<CustomerLexwareResponse>, ProblemHttpResult>> LinkCustomerAsync(Guid contactId, LinkLexwareContactRequest request,
        LexwareService lexware, LexwareChangeSyncService changes, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        if (await CustomerEntityAsync(db, contactId, cancellationToken) is not { } contact)
        {
            return CustomerNotFound();
        }

        if (!contact.LexwareSync)
        {
            return Problem(LexwareService.SyncDisabled(contact.Name));
        }

        var row = await lexware.SettingsAsync(cancellationToken);
        var remote = await lexware.Client.GetContactAsync(lexware.Connection(row), request.LexwareContactId.Trim(), cancellationToken);
        if (!remote.IsCustomer)
        {
            return Problem(new LexwareException("NOT_A_CUSTOMER",
                $"„{remote.Name}“ hat in Lexware Office nicht die Rolle Kunde. Bitte ergänzen Sie die Rolle dort und versuchen Sie es erneut."));
        }

        var link = await lexware.LinkAsync(contact, remote, LexwareLinkOrigin.Linked, currentUser.UserId, cancellationToken);
        audit.Record("lexware.contact_linked", currentUser.UserId, "contact", contactId.ToString(), new Dictionary<string, string?>
        {
            ["name"] = contact.Name, ["lexwareContact"] = remote.Name, ["lexwareNumber"] = remote.CustomerNumber,
        });
        await db.SaveChangesAsync(cancellationToken);

        // Erstabgleich gleich mit dem eben gelesenen Kontakt: Unterschiede erscheinen sofort als Abweichungen, übernommen wird nichts.
        try
        {
            await changes.SyncCustomerAsync(contactId, remote, currentUser.UserId, null, cancellationToken);
        }
        catch (Exception exception) when (exception is LexwareException or DbUpdateConcurrencyException)
        {
            // Die Verknüpfung steht; den Abgleich holt der Hintergrunddienst nach.
        }

        db.ChangeTracker.Clear();
        return TypedResults.Ok(await CustomerResponseAsync(lexware, contactId, true, cancellationToken));
    }

    private static async Task<Results<Ok<CustomerLexwareResponse>, ProblemHttpResult>> SetSyncAsync(Guid contactId, SetLexwareSyncRequest request,
        LexwareService lexware, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        if (await db.Set<Contact>().SingleOrDefaultAsync(c => c.Id == contactId, cancellationToken) is not { } contact)
        {
            return CustomerNotFound();
        }

        if (contact.LexwareSync != request.Enabled)
        {
            contact.SetLexwareSync(request.Enabled);
            // Bewusst eingeschaltet: wie ein neuer Kunde behandeln (ohne Treffer automatisch anlegen, sonst mögliche Dublette).
            await db.Set<LexwareContactCreation>().Where(x => x.ContactId == contactId).ExecuteDeleteAsync(cancellationToken);
            audit.Record("lexware.sync_changed", currentUser.UserId, "contact", contactId.ToString(), new Dictionary<string, string?>
            {
                ["name"] = contact.Name, ["lexwareSync"] = request.Enabled ? "ein" : "aus",
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        // Eine bestehende Verknüpfung bleibt beim Ausschalten bestehen: Der Kontakt und seine Rechnungen liegen bereits in
        // Lexware Office, und ein stilles Lösen würde die Belege der Bauakten vom Kunden trennen. Gelöst wird nur bewusst.
        return TypedResults.Ok(await CustomerResponseAsync(lexware, contactId, true, cancellationToken));
    }

    private static async Task<Ok<EnableLexwareSyncResponse>> EnableSyncAsync(EnableLexwareSyncRequest request, LexwareService lexware,
        LexwareAutoCreateService creation, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        var ids = request.ContactIds.Distinct().ToList();
        var customers = await db.Set<Contact>().Include(c => c.Persons).Where(c => ids.Contains(c.Id) && !c.LexwareSync && c.AnonymizedAt == null)
            .ToListAsync(cancellationToken);

        // Was das Einschalten bewirkt: Kunden ohne ähnlichen Kontakt werden danach automatisch in Lexware Office angelegt.
        var wouldCreate = 0;
        var withMatches = 0;
        var row = await lexware.SettingsAsync(cancellationToken);
        if (customers.Count > 0 && await creation.ActiveAsync(row, cancellationToken))
        {
            try
            {
                var (remote, _) = await lexware.ContactsAsync(row, refresh: false, cancellationToken);
                var parties = remote.Select(LexwareService.Party).ToList();
                var linked = await db.Set<LexwareContactLink>().Where(l => ids.Contains(l.ContactId)).Select(l => l.ContactId).ToListAsync(cancellationToken);
                foreach (var customer in customers.Where(c => c.ArchivedAt is null && !linked.Contains(c.Id)))
                {
                    if (LexwareMatching.Find(LexwareService.Party(customer), parties, 1).Count > 0)
                    {
                        withMatches++;
                    }
                    else
                    {
                        wouldCreate++;
                    }
                }
            }
            catch (LexwareException)
            {
                // Ohne Verbindung keine Vorschau der Zahlen; angelegt wird dann auch nichts, bis Lexware Office wieder erreichbar ist.
            }
        }

        if (request.Preview)
        {
            return TypedResults.Ok(new EnableLexwareSyncResponse(customers.Count, wouldCreate, withMatches));
        }

        foreach (var customer in customers)
        {
            customer.SetLexwareSync(true);
        }

        if (customers.Count > 0)
        {
            // Frühere Vermerke gelten nicht mehr: Die Kunden werden wie neu behandelt (ohne Treffer anlegen, sonst mögliche Dublette).
            var changed = customers.Select(c => c.Id).ToList();
            await db.Set<LexwareContactCreation>().Where(x => changed.Contains(x.ContactId)).ExecuteDeleteAsync(cancellationToken);
            audit.Record("lexware.sync_enabled_bulk", currentUser.UserId, "settings", "lexware", new Dictionary<string, string?>
            {
                ["count"] = customers.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(new EnableLexwareSyncResponse(customers.Count, wouldCreate, withMatches));
    }

    private static async Task<Results<Ok<CreateLexwareContactResponse>, ProblemHttpResult>> CreateContactAsync(Guid contactId,
        CreateLexwareContactRequest request, LexwareService lexware, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        if (await CustomerEntityAsync(db, contactId, cancellationToken) is not { } contact)
        {
            return CustomerNotFound();
        }

        if (contact.AnonymizedAt is not null)
        {
            return Problem(new LexwareException("ANONYMIZED", "Anonymisierte Kunden können nicht nach Lexware Office übertragen werden."));
        }

        if (!contact.LexwareSync)
        {
            return Problem(LexwareService.SyncDisabled(contact.Name));
        }

        if (await db.Set<LexwareContactLink>().AnyAsync(l => l.ContactId == contactId, cancellationToken))
        {
            return Problem(new LexwareException("ALREADY_LINKED", $"„{contact.Name}“ ist bereits mit einem Kontakt in Lexware Office verknüpft."));
        }

        var row = await lexware.SettingsAsync(cancellationToken);
        var connection = lexware.Connection(row);

        // Nicht gleichzeitig mit der automatischen Anlage im Hintergrund: sonst entstünde der Kontakt doppelt.
        await lexware.SyncGate.WaitAsync(cancellationToken);
        try
        {
            return await CreateContactCoreAsync(contactId, request, lexware, audit, currentUser, contact, row, connection, cancellationToken);
        }
        finally
        {
            lexware.SyncGate.Release();
        }
    }

    private static async Task<Results<Ok<CreateLexwareContactResponse>, ProblemHttpResult>> CreateContactCoreAsync(Guid contactId,
        CreateLexwareContactRequest request, LexwareService lexware, IAuditLog audit, ICurrentUser currentUser, Contact contact, LexwareSettingsRow row,
        LexwareConnection connection, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        if (await db.Set<LexwareContactLink>().AnyAsync(l => l.ContactId == contactId, cancellationToken))
        {
            return Problem(new LexwareException("ALREADY_LINKED", $"„{contact.Name}“ ist bereits mit einem Kontakt in Lexware Office verknüpft."));
        }

        // Ohne Dublettenprüfung wird nie angelegt: Die Prüfung läuft hier auf dem Server, nicht nur in der Oberfläche.
        var (contacts, _) = await lexware.ContactsAsync(row, refresh: false, cancellationToken);
        var candidates = Candidates(lexware, row, contact, contacts, await LinkedAsync(db, cancellationToken), false);
        if (candidates.Count > 0 && !request.CreateAnyway)
        {
            return TypedResults.Ok(new CreateLexwareContactResponse(false, null, candidates));
        }

        var id = await lexware.Client.CreateContactAsync(connection, LexwareService.Draft(contact), cancellationToken);
        LexwareContact remote;
        try
        {
            remote = await lexware.Client.GetContactAsync(connection, id, cancellationToken);
        }
        catch (LexwareException)
        {
            // Angelegt ist der Kontakt bereits; die Kundennummer zeigt dann der nächste Abgleich.
            var party = LexwareService.Party(contact);
            remote = new LexwareContact(id, party.IsCompany, contact.Name, null, null, null, null, true, party.Emails, party.Phones, party.Street,
                party.PostalCode, party.City, party.VatId, null, false);
        }

        lexware.Remember(row, remote);
        var link = await lexware.LinkAsync(contact, remote, LexwareLinkOrigin.CreatedInLexware, currentUser.UserId, cancellationToken);
        audit.Record("lexware.contact_created", currentUser.UserId, "contact", contactId.ToString(), new Dictionary<string, string?>
        {
            ["name"] = contact.Name, ["lexwareNumber"] = remote.CustomerNumber,
            ["duplicatesIgnored"] = candidates.Count > 0 ? candidates.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
        });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new CreateLexwareContactResponse(true, ToResponse(lexware, row, link), []));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UnlinkCustomerAsync(Guid contactId, LexwareService lexware, IAuditLog audit,
        ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        if (await db.Set<LexwareContactLink>().SingleOrDefaultAsync(l => l.ContactId == contactId, cancellationToken) is not { } link)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Nicht verknüpft",
                detail: "Dieser Kunde ist nicht mit Lexware Office verknüpft.", extensions: Code("LEXWARE_NOT_LINKED"));
        }

        db.Remove(link);
        // Bewusst gelöst: Der Kunde wird danach nicht von selbst erneut in Lexware Office angelegt.
        if (!await db.Set<LexwareContactCreation>().AnyAsync(x => x.ContactId == contactId, cancellationToken))
        {
            db.Add(new LexwareContactCreation { ContactId = contactId, State = LexwareContactCreation.Skipped, UpdatedAt = lexware.Clock.GetUtcNow() });
        }

        audit.Record("lexware.contact_unlinked", currentUser.UserId, "contact", contactId.ToString(), new Dictionary<string, string?>
        {
            ["lexwareContact"] = link.LexwareName, ["lexwareNumber"] = link.LexwareNumber,
        });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    // ---------- Lexware-Kontakte übernehmen ----------

    private static async Task<Results<Ok<LexwareContactListResponse>, ProblemHttpResult>> ListContactsAsync(LexwareService lexware,
        CancellationToken cancellationToken, [FromQuery, MaxLength(200)] string? q = null, [FromQuery] int page = 0, [FromQuery] int size = 50,
        [FromQuery] bool unlinked = false, [FromQuery] bool refresh = false)
    {
        var row = await lexware.SettingsAsync(cancellationToken);
        var (contacts, loadedAt) = await lexware.ContactsAsync(row, refresh, cancellationToken);
        var linked = await LinkedAsync(lexware.Db, cancellationToken);
        var terms = LexwareMatching.Normalize(q).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var filtered = contacts.Where(c => !unlinked || !linked.ContainsKey(c.Id))
            .Where(c => terms.Length == 0 || terms.All(LexwareMatching.Normalize(
                string.Join(' ', new[] { c.Name, c.CustomerNumber, c.City, c.PostalCode, c.Street }.Concat(c.Emails))).Contains))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        size = Math.Clamp(size, 1, 200);
        page = Math.Max(page, 0);
        var items = filtered.Skip(page * size).Take(size).Select(c =>
        {
            var owner = linked.TryGetValue(c.Id, out var found) ? found : ((Guid Id, string Name)?)null;
            return Summary(lexware, row, c, owner?.Id, owner?.Name);
        }).ToList();
        return TypedResults.Ok(new LexwareContactListResponse(items, page, size, filtered.Count, loadedAt));
    }

    /// <summary>
    /// Prüfung beim Anlegen eines Kunden, unabhängig vom Schalter „Mit Lexware Office abgleichen“. Ohne Schlüssel oder ohne
    /// Verbindung gibt es keine Fehlermeldung, sondern den Hinweis, dass die Prüfung derzeit nicht möglich ist.
    /// </summary>
    private static async Task<Ok<CheckLexwareContactResponse>> CheckContactAsync(CheckLexwareContactRequest request, LexwareService lexware,
        IUserPermissions permissions, CancellationToken cancellationToken)
    {
        var canImport = await permissions.HasAsync(Permissions.InvoicesAssign, cancellationToken);
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length < 2 && string.IsNullOrWhiteSpace(request.Email))
        {
            return TypedResults.Ok(new CheckLexwareContactResponse(true, null, [], canImport));
        }

        var row = await lexware.SettingsAsync(cancellationToken);
        if (!row.Configured)
        {
            return TypedResults.Ok(new CheckLexwareContactResponse(false, null, [], canImport));
        }

        IReadOnlyList<LexwareContact> contacts;
        try
        {
            (contacts, _) = await lexware.ContactsAsync(row, refresh: false, cancellationToken);
        }
        catch (LexwareException)
        {
            return TypedResults.Ok(new CheckLexwareContactResponse(false, "Die Prüfung in Lexware Office ist derzeit nicht möglich.", [], canImport));
        }

        static List<string> One(string? value) => string.IsNullOrWhiteSpace(value) ? [] : [value.Trim()];
        var subject = new MatchParty("neu", name, request.Kind != ContactKind.Person, One(request.Email), One(request.Phone), request.Street, request.PostalCode,
            request.City, null, request.VatId);
        var linked = await LinkedAsync(lexware.Db, cancellationToken);
        var byId = contacts.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var candidates = LexwareMatching.Find(subject, contacts.Select(LexwareService.Party), 10).Select(r =>
        {
            var owner = linked.TryGetValue(r.Party.Key, out var found) ? found : ((Guid Id, string Name)?)null;
            return new LexwareContactCandidate(Summary(lexware, row, byId[r.Party.Key], owner?.Id, owner?.Name), r.Match.Score, r.Match.Reasons, r.Match.Strong);
        }).ToList();
        return TypedResults.Ok(new CheckLexwareContactResponse(true, null, candidates, canImport));
    }

    private static async Task<(List<Contact> Customers, Dictionary<Guid, string> Links)> CustomersAsync(EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var customers = await db.Set<Contact>().AsNoTracking().Include(c => c.Persons).Where(c => c.AnonymizedAt == null).ToListAsync(cancellationToken);
        var links = await db.Set<LexwareContactLink>().AsNoTracking().ToDictionaryAsync(l => l.ContactId, l => l.LexwareContactId, cancellationToken);
        return (customers, links);
    }

    private static List<LexwareCustomerCandidate> CustomerCandidates(LexwareContact remote, List<Contact> customers, Dictionary<Guid, string> links)
    {
        var byId = customers.ToDictionary(c => c.Id.ToString(), StringComparer.Ordinal);
        return LexwareMatching.Find(LexwareService.Party(remote), customers.Select(LexwareService.Party), 25).Select(r =>
        {
            var customer = byId[r.Party.Key];
            return new LexwareCustomerCandidate(Summary(customer, links.GetValueOrDefault(customer.Id)), r.Match.Score, r.Match.Reasons, r.Match.Strong);
        }).ToList();
    }

    private static async Task<Results<Ok<List<LexwareCustomerCandidate>>, ProblemHttpResult>> CustomerCandidatesAsync(string lexwareContactId,
        LexwareService lexware, CancellationToken cancellationToken)
    {
        var row = await lexware.SettingsAsync(cancellationToken);
        var remote = await RemoteContactAsync(lexware, row, lexwareContactId, cancellationToken);
        var (customers, links) = await CustomersAsync(lexware.Db, cancellationToken);
        return TypedResults.Ok(CustomerCandidates(remote, customers, links));
    }

    /// <summary>Kontakt aus der zwischengespeicherten Liste, sonst einzeln aus Lexware Office.</summary>
    private static async Task<LexwareContact> RemoteContactAsync(LexwareService lexware, LexwareSettingsRow row, string lexwareContactId,
        CancellationToken cancellationToken)
    {
        var (contacts, _) = await lexware.ContactsAsync(row, refresh: false, cancellationToken);
        return contacts.FirstOrDefault(c => c.Id == lexwareContactId)
            ?? await lexware.Client.GetContactAsync(lexware.Connection(row), lexwareContactId, cancellationToken);
    }

    private static async Task<Results<Ok<ImportLexwareContactResponse>, ProblemHttpResult>> ImportContactAsync(string lexwareContactId,
        ImportLexwareContactRequest request, LexwareService lexware, ICustomerNumbers numbers, IUserPermissions permissions, IAuditLog audit,
        ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (!await permissions.HasAsync(Permissions.ContactsEdit, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Nicht erlaubt",
                detail: "Zum Übernehmen von Kunden benötigen Sie zusätzlich das Recht, Kunden zu bearbeiten.", extensions: Code("PERMISSION_DENIED"));
        }

        var db = lexware.Db;
        var row = await lexware.SettingsAsync(cancellationToken);
        var remote = await RemoteContactAsync(lexware, row, lexwareContactId, cancellationToken);
        if (await db.Set<LexwareContactLink>().AnyAsync(l => l.LexwareContactId == remote.Id, cancellationToken))
        {
            return Problem(new LexwareException("ALREADY_LINKED", $"Der Lexware-Kontakt „{remote.Name}“ ist bereits mit einem Kunden verknüpft."));
        }

        // Auch in dieser Richtung wird nie ohne Dublettenprüfung angelegt.
        var (customers, links) = await CustomersAsync(db, cancellationToken);
        var candidates = CustomerCandidates(remote, customers, links);
        if (candidates.Count > 0 && !request.CreateAnyway)
        {
            return TypedResults.Ok(new ImportLexwareContactResponse(false, null, null, candidates));
        }

        var kind = remote.IsCompany ? ContactKind.Company : ContactKind.Person;
        var name = LexwareService.Cut(remote.Name, 200)!;
        var contact = Contact.Create(Guid.CreateVersion7(), kind, name);
        var phones = remote.Phones.Select(p => LexwareService.Cut(p, 50)!).Take(10).ToList();
        var emails = remote.Emails.Select(e => LexwareService.Cut(e, 320)!).Take(10).ToList();
        contact.Update(kind, name, null, LexwareService.Cut(remote.Street, 200), LexwareService.Cut(remote.PostalCode, 10), LexwareService.Cut(remote.City, 100),
            phones.FirstOrDefault(), emails.FirstOrDefault(), null);
        contact.UpdateDetails(new ContactDetails(phones.Select(p => new ContactPhone(ContactPhoneKind.Landline, p)).ToList(),
            emails.Select(e => new ContactEmail(e)).ToList(), LexwareService.Cut(remote.Salutation, 30), null, null, null,
            LexwareService.Cut(remote.VatId, 30), LexwareService.Cut(remote.TaxNumber, 30), null, null, null));
        contact.SetLexwareSync(true); // kommt aus Lexware Office und ist damit verknüpft
        try
        {
            await numbers.AssignIfMissingAsync(contact, cancellationToken);
        }
        catch (NumberRangeExhaustedException exception)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Nummernkreis ausgeschöpft", detail: exception.Message,
                extensions: Code(CustomerNumbersExhaustedException.Code));
        }

        db.Add(contact);
        await lexware.LinkAsync(contact, remote, LexwareLinkOrigin.ImportedFromLexware, currentUser.UserId, cancellationToken);
        audit.Record("lexware.contact_imported", currentUser.UserId, "contact", contact.Id.ToString(), new Dictionary<string, string?>
        {
            ["name"] = contact.Name, ["lexwareNumber"] = remote.CustomerNumber,
            ["duplicatesIgnored"] = candidates.Count > 0 ? candidates.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
        });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new ImportLexwareContactResponse(true, contact.Id, contact.CustomerNumber, []));
    }

    // ---------- Abgleichübersicht ----------

    private sealed record Reconciliation(
        LexwareSettingsRow Row, DateTimeOffset LoadedAt, List<Contact> Customers, Dictionary<Guid, string> Links, IReadOnlyList<LexwareContact> Remote,
        List<(Contact Customer, LexwareContact Lexware, MatchResult Match, bool Unique)> Suggestions);

    private static async Task<Reconciliation> ReconcileAsync(LexwareService lexware, bool refresh, CancellationToken cancellationToken)
    {
        var row = await lexware.SettingsAsync(cancellationToken);
        var (remote, loadedAt) = await lexware.ContactsAsync(row, refresh, cancellationToken);
        var (customers, links) = await CustomersAsync(lexware.Db, cancellationToken);
        var linkedRemote = links.Values.ToHashSet(StringComparer.Ordinal);
        // Kunden ohne „Mit Lexware Office abgleichen“ bleiben bewusst nur in EstrichONE und werden nicht als fehlend vorgeschlagen.
        var freeCustomers = customers.Where(c => !links.ContainsKey(c.Id) && c.ArchivedAt is null && c.LexwareSync).ToList();
        var freeRemote = remote.Where(c => !linkedRemote.Contains(c.Id)).ToList();
        var customerById = freeCustomers.ToDictionary(c => c.Id.ToString(), StringComparer.Ordinal);
        var remoteById = freeRemote.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var pairs = LexwareMatching.Pairs(freeCustomers.Select(LexwareService.Party).ToList(), freeRemote.Select(LexwareService.Party).ToList());

        // Eindeutig: gleiche E-Mail-Adresse oder gleiche Kundennummer, und auf beiden Seiten gibt es kein zweites solches Gegenstück.
        static bool Decisive(MatchResult m) =>
            m.Strong && (m.Reasons.Contains(LexwareMatching.SameEmail) || m.Reasons.Contains(LexwareMatching.SameCustomerNumber));
        var decisive = pairs.Where(p => Decisive(p.Match)).ToList();
        var perCustomer = decisive.GroupBy(p => p.Left.Key).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var perRemote = decisive.GroupBy(p => p.Right.Key).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var suggestions = pairs.Select(p => (customerById[p.Left.Key], remoteById[p.Right.Key], p.Match,
                Decisive(p.Match) && perCustomer[p.Left.Key] == 1 && perRemote[p.Right.Key] == 1))
            .OrderByDescending(s => s.Item4).ThenByDescending(s => s.Match.Score).ThenBy(s => s.Item1.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return new Reconciliation(row, loadedAt, customers, links, remote, suggestions);
    }

    private static async Task<Results<Ok<LexwareReconciliationResponse>, ProblemHttpResult>> ReconciliationAsync(LexwareService lexware,
        LexwareChangeSyncService changes, CancellationToken cancellationToken, [FromQuery] bool refresh = false)
    {
        string? changeError = null;
        if (refresh)
        {
            // „Aktualisieren“ lädt die Kontaktliste neu und gleicht danach die Änderungen aller Verknüpfungen ab.
            await lexware.ContactsAsync(await lexware.SettingsAsync(cancellationToken), refresh: true, cancellationToken);
            changeError = (await changes.RunAsync(full: true, cancellationToken)).Error;
        }

        var data = await ReconcileAsync(lexware, refresh: false, cancellationToken);
        var row = data.Row;
        var syncLinks = await lexware.Db.Set<LexwareContactLink>().AsNoTracking().ToDictionaryAsync(l => l.ContactId, cancellationToken);
        var remoteById = data.Remote.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var names = data.Customers.ToDictionary(c => c.Id, c => c.Name);
        var owners = data.Links.Where(l => names.ContainsKey(l.Key)).ToDictionary(l => l.Value, l => (l.Key, names[l.Key]), StringComparer.Ordinal);
        LexwareContactSummary Remote(LexwareContact c) =>
            owners.TryGetValue(c.Id, out var owner) ? Summary(lexware, row, c, owner.Key, owner.Item2) : Summary(lexware, row, c);

        var linked = data.Customers.Where(c => data.Links.ContainsKey(c.Id)).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c =>
            {
                var remote = remoteById.GetValueOrDefault(data.Links[c.Id]);
                var link = syncLinks.GetValueOrDefault(c.Id);
                return new LexwareLinkedPair(Summary(c, data.Links[c.Id]), remote is null ? null : Remote(remote), remote is null || link?.RemoteState is not null,
                    link is null ? null : SyncState(row, link, c));
            }).ToList();
        var deviations = data.Customers.Where(c => syncLinks.TryGetValue(c.Id, out var link) && SyncState(row, link, c).State == "deviations")
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(c =>
            {
                var link = syncLinks[c.Id];
                return new LexwareDeviationItem(Summary(c, link.LexwareContactId), link.LexwareContactId, link.LexwareName,
                    lexware.ContactUrl(row, link.LexwareContactId), DeviationFields(link), link.SyncedAt);
            }).ToList();
        var notes = await lexware.Db.Set<LexwareContactCreation>().AsNoTracking().ToDictionaryAsync(x => x.ContactId, cancellationToken);
        var auto = row.Configured && row.AutoCreate;
        var onlyLocal = data.Customers.Where(c => !data.Links.ContainsKey(c.Id) && c.ArchivedAt is null && c.LexwareSync)
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(c =>
            {
                var note = notes.GetValueOrDefault(c.Id);
                return Summary(c, null) with
                {
                    Creation = note is null ? auto ? "pending" : "manual" : note.State == LexwareContactCreation.Skipped ? "manual" : note.State,
                    CreationError = note?.Error,
                };
            }).ToList();
        var excluded = data.Customers.Where(c => !data.Links.ContainsKey(c.Id) && c.ArchivedAt is null && !c.LexwareSync)
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(c => Summary(c, null)).ToList();
        var linkedRemote = data.Links.Values.ToHashSet(StringComparer.Ordinal);
        var onlyRemote = data.Remote.Where(c => !linkedRemote.Contains(c.Id)).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(Remote).ToList();
        var suggestions = data.Suggestions.Select(s => new LexwareSuggestion(Summary(s.Customer, null), Remote(s.Lexware), s.Match.Score, s.Match.Reasons,
            s.Unique)).ToList();

        var active = data.Customers.Where(c => c.ArchivedAt is null).ToList();
        var numbers = active.ToDictionary(c => c.Id.ToString(), c => c.CustomerNumber, StringComparer.Ordinal);
        var remoteNumbers = data.Remote.ToDictionary(c => c.Id, c => c.CustomerNumber, StringComparer.Ordinal);
        var withinLocal = LexwareMatching.PairsWithin(active.Select(LexwareService.Party).ToList())
            .Select(p => new LexwareInternalDuplicate("estrichOne", p.Left.Key, p.Left.Name, numbers[p.Left.Key], p.Right.Key, p.Right.Name, numbers[p.Right.Key],
                p.Match.Score, p.Match.Reasons));
        var withinRemote = LexwareMatching.PairsWithin(data.Remote.Select(LexwareService.Party).ToList())
            .Select(p => new LexwareInternalDuplicate("lexware", p.Left.Key, p.Left.Name, remoteNumbers[p.Left.Key], p.Right.Key, p.Right.Name,
                remoteNumbers[p.Right.Key], p.Match.Score, p.Match.Reasons));
        var within = withinLocal.Concat(withinRemote).OrderByDescending(d => d.Score).ThenBy(d => d.FirstName, StringComparer.OrdinalIgnoreCase).ToList();

        var truncated = linked.Count > ListLimit || onlyLocal.Count > ListLimit || excluded.Count > ListLimit * 4 || onlyRemote.Count > ListLimit || suggestions.Count > ListLimit
            || within.Count > ListLimit || deviations.Count > ListLimit;
        return TypedResults.Ok(new LexwareReconciliationResponse(data.LoadedAt, linked.Count, onlyLocal.Count, onlyRemote.Count,
            suggestions.Count + within.Count, suggestions.Count(s => s.Unique), linked.Take(ListLimit).ToList(), onlyLocal.Take(ListLimit).ToList(),
            onlyRemote.Take(ListLimit).ToList(), suggestions.Take(ListLimit).ToList(), within.Take(ListLimit).ToList(), truncated, excluded.Count,
            excluded.Take(ListLimit * 4).ToList(), deviations.Count, deviations.Take(ListLimit).ToList(), row.ChangeSync, changeError));
    }

    private static async Task<Results<Ok<LinkUniqueLexwareResponse>, ProblemHttpResult>> LinkUniqueAsync(LinkUniqueLexwareRequest request,
        LexwareService lexware, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        // Die Vorschau der Oberfläche wird hier erneut geprüft: verknüpft wird nur, was jetzt noch eindeutig ist.
        var data = await ReconcileAsync(lexware, refresh: false, cancellationToken);
        var unique = data.Suggestions.Where(s => s.Unique).ToDictionary(s => (s.Customer.Id, s.Lexware.Id));
        var linked = 0;
        foreach (var pair in request.Pairs.DistinctBy(p => p.ContactId))
        {
            if (!unique.TryGetValue((pair.ContactId, pair.LexwareContactId), out var match))
            {
                continue;
            }

            lexware.Db.Add(new LexwareContactLink
            {
                ContactId = match.Customer.Id, LexwareContactId = match.Lexware.Id, LexwareName = LexwareService.Cut(match.Lexware.Name, LexwareContactLink.MaxName),
                LexwareNumber = LexwareService.Cut(match.Lexware.CustomerNumber, 50), Origin = LexwareLinkOrigin.Linked, LinkedByUserId = currentUser.UserId,
                LinkedAt = lexware.Clock.GetUtcNow(),
            });
            linked++;
        }

        if (linked > 0)
        {
            audit.Record("lexware.contacts_linked_bulk", currentUser.UserId, "settings", "lexware", new Dictionary<string, string?>
            {
                ["count"] = linked.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
            await lexware.Db.SaveChangesAsync(cancellationToken);
            await lexware.Db.Set<LexwareContactCreation>().Where(x => lexware.Db.Set<LexwareContactLink>().Any(l => l.ContactId == x.ContactId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        return TypedResults.Ok(new LinkUniqueLexwareResponse(linked, request.Pairs.Count - linked));
    }

    // ---------- Bauakte ----------

    private static LexwareVoucherResponse ToResponse(LexwareService lexware, LexwareSettingsRow row, LexwareVoucherLink link) => new(
        link.Id, link.LexwareId, link.VoucherType, link.Number, link.Title, link.VoucherDate, link.DueDate, link.NetAmount, link.GrossAmount,
        link.Currency, lexware.DisplayStatus(link.VoucherType, link.Status, link.DueDate), link.ContactName,
        lexware.VoucherUrl(row, link.VoucherType, link.LexwareId, link.Status), link.SyncedAt, link.SyncError);

    /// <summary>Belege der Belegliste mit der Bauakte, der sie schon zugeordnet sind.</summary>
    private static async Task<List<LexwareVoucherItem>> ItemsAsync(LexwareService lexware, LexwareSettingsRow row, IReadOnlyList<LexwareVoucher> vouchers,
        HashSet<string>? suggested, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        var ids = vouchers.Select(v => v.Id).ToList();
        var assigned = (await db.Set<LexwareVoucherLink>().AsNoTracking().Where(l => ids.Contains(l.LexwareId))
            .Join(db.Set<Bauakte>().AsNoTracking(), l => l.BauakteId, b => b.Id, (l, b) => new { l.LexwareId, b.Id, b.Number, b.Title })
            .ToListAsync(cancellationToken)).ToDictionary(x => x.LexwareId, StringComparer.Ordinal);
        return vouchers.Select(v =>
        {
            var owner = assigned.GetValueOrDefault(v.Id);
            return new LexwareVoucherItem(v.Id, v.Type, v.Number, v.VoucherDate, v.DueDate, v.GrossAmount, v.Currency,
                lexware.DisplayStatus(v.Type, v.Status, v.DueDate), v.ContactName, lexware.VoucherUrl(row, v.Type, v.Id, v.Status), owner?.Id,
                owner is null ? null : string.Join(' ', new[] { owner.Number, owner.Title }.Where(p => p is not null)), suggested?.Contains(v.Id) == true);
        }).ToList();
    }

    private static async Task<Results<Ok<CustomerVouchersResponse>, ProblemHttpResult>> CustomerVouchersAsync(Guid contactId, LexwareService lexware,
        CancellationToken cancellationToken, [FromQuery] bool refresh = false)
    {
        var db = lexware.Db;
        if (!await db.Set<Contact>().AnyAsync(c => c.Id == contactId, cancellationToken))
        {
            return CustomerNotFound();
        }

        var lexwareId = await db.Set<LexwareContactLink>().AsNoTracking().Where(l => l.ContactId == contactId).Select(l => l.LexwareContactId)
            .FirstOrDefaultAsync(cancellationToken);
        if (lexwareId is null)
        {
            return TypedResults.Ok(new CustomerVouchersResponse(false, [], null));
        }

        var row = await lexware.SettingsAsync(cancellationToken);
        var (vouchers, loadedAt) = await lexware.ContactVouchersAsync(row, lexwareId, refresh, cancellationToken);
        return TypedResults.Ok(new CustomerVouchersResponse(true, await ItemsAsync(lexware, row, vouchers, null, cancellationToken), loadedAt));
    }

    private static async Task<Bauakte?> BauakteEntityAsync(LexwareService lexware, IBauakteAccess access, Guid id, CancellationToken cancellationToken) =>
        await access.CanReadAsync(id, cancellationToken)
            ? await lexware.Db.Set<Bauakte>().AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, cancellationToken)
            : null;

    private static async Task<LexwareBauakteCustomer?> BauakteCustomerAsync(EstrichOneDbContext db, Bauakte bauakte, CancellationToken cancellationToken)
    {
        if (bauakte.CustomerId is not { } customerId
            || await db.Set<Contact>().AsNoTracking().Where(c => c.Id == customerId).Select(c => new { c.Name, c.LexwareSync })
                .FirstOrDefaultAsync(cancellationToken) is not { } customer)
        {
            return null;
        }

        var lexwareId = await db.Set<LexwareContactLink>().AsNoTracking().Where(l => l.ContactId == customerId).Select(l => l.LexwareContactId)
            .FirstOrDefaultAsync(cancellationToken);
        return new LexwareBauakteCustomer(customerId, customer.Name, lexwareId, customer.LexwareSync);
    }

    private static async Task<Results<Ok<BauakteLexwareResponse>, ProblemHttpResult>> BauakteAsync(Guid id, LexwareService lexware, IBauakteAccess access,
        IUserPermissions permissions, CancellationToken cancellationToken, [FromQuery] bool refresh = false)
    {
        if (await BauakteEntityAsync(lexware, access, id, cancellationToken) is not { } bauakte)
        {
            return BauakteWriteProblems.NotFound();
        }

        var db = lexware.Db;
        var row = await lexware.SettingsAsync(cancellationToken);
        var links = await db.Set<LexwareVoucherLink>().Where(l => l.BauakteId == id).ToListAsync(cancellationToken);
        var error = await lexware.SyncAsync(row, links, refresh, cancellationToken);
        var vouchers = links.OrderByDescending(l => l.VoucherDate ?? DateOnly.FromDateTime(l.LinkedAt.UtcDateTime)).ThenByDescending(l => l.LinkedAt)
            .Select(l => ToResponse(lexware, row, l)).ToList();
        return TypedResults.Ok(new BauakteLexwareResponse(id, row.Configured,
            row.Configured && await permissions.HasAsync(Permissions.InvoicesAssign, cancellationToken),
            await BauakteCustomerAsync(db, bauakte, cancellationToken), vouchers, error,
            await BauakteContactAsync(lexware, row, bauakte, cancellationToken), LexwareVoucherSummary.Text(vouchers)));
    }

    /// <summary>Lexware-Seite des verknüpften Kunden aus dem gemerkten Abgleichstand (zuletzt abgeglichene Werte, bei Abweichungen der Wert aus Lexware Office).</summary>
    private static async Task<LexwareBauakteContact?> BauakteContactAsync(LexwareService lexware, LexwareSettingsRow row, Bauakte bauakte,
        CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        if (bauakte.CustomerId is not { } customerId
            || await db.Set<LexwareContactLink>().AsNoTracking().SingleOrDefaultAsync(l => l.ContactId == customerId, cancellationToken) is not { } link
            || await db.Set<Contact>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == customerId, cancellationToken) is not { } contact)
        {
            return null;
        }

        var state = SyncState(row, link, contact);
        var values = LexwareContactSync.Baseline(link.SyncedFields) ?? [];
        var deviations = state.State is "off" or "disabled" or "paused" ? [] : LexwareContactSync.Deviations(link.Deviations);
        foreach (var deviation in deviations)
        {
            values[deviation.Field] = deviation.Lexware;
        }

        return new LexwareBauakteContact(link.LexwareContactId, link.LexwareName, link.LexwareNumber, lexware.ContactUrl(row, link.LexwareContactId),
            values.GetValueOrDefault(LexwareSyncFields.Street), values.GetValueOrDefault(LexwareSyncFields.PostalCode),
            values.GetValueOrDefault(LexwareSyncFields.City), values.GetValueOrDefault(LexwareSyncFields.Email), deviations.Select(d => d.Field).ToList(), state);
    }

    /// <summary>Höchstzahl der Belege, die für den Vorschlag einzeln gelesen werden (Lexware erlaubt zwei Anfragen je Sekunde).</summary>
    private const int SuggestionLimit = 12;

    private static async Task<Results<Ok<BauakteVoucherCandidatesResponse>, ProblemHttpResult>> CandidatesForBauakteAsync(Guid id, LexwareService lexware,
        IBauakteAccess access, [FromQuery, MaxLength(100)] string? number, CancellationToken cancellationToken, [FromQuery] bool refresh = false)
    {
        if (await BauakteEntityAsync(lexware, access, id, cancellationToken) is not { } bauakte)
        {
            return BauakteWriteProblems.NotFound();
        }

        var row = await lexware.SettingsAsync(cancellationToken);
        var connection = lexware.Connection(row);
        var customer = await BauakteCustomerAsync(lexware.Db, bauakte, cancellationToken);
        if (!string.IsNullOrWhiteSpace(number))
        {
            var hits = await lexware.Client.ListVouchersAsync(connection, null, number.Trim(), 25, cancellationToken);
            return TypedResults.Ok(new BauakteVoucherCandidatesResponse(customer?.LexwareContactId is not null,
                await ItemsAsync(lexware, row, hits, null, cancellationToken)));
        }

        if (customer?.LexwareContactId is null)
        {
            return TypedResults.Ok(new BauakteVoucherCandidatesResponse(false, []));
        }

        var (vouchers, _) = await lexware.ContactVouchersAsync(row, customer.LexwareContactId, refresh, cancellationToken);
        var taken = await lexware.Db.Set<LexwareVoucherLink>().AsNoTracking().Select(l => l.LexwareId).ToListAsync(cancellationToken);
        // Vorschlag statt automatischer Zuordnung: Die Nummer kann auch als Verweis in einem Beleg zu einer anderen Bauakte
        // stehen (Nachtrag, Sammelrechnung). Zugeordnet wird deshalb immer erst nach Bestätigung.
        var suggested = new HashSet<string>(StringComparer.Ordinal);
        if (bauakte.Number is { Length: >= 3 } bauakteNumber)
        {
            foreach (var voucher in vouchers.Where(v => !taken.Contains(v.Id)).Take(SuggestionLimit))
            {
                try
                {
                    if (await lexware.MentionsAsync(row, voucher, bauakteNumber, cancellationToken))
                    {
                        suggested.Add(voucher.Id);
                    }
                }
                catch (LexwareException)
                {
                    break; // Die Liste bleibt nutzbar, nur ohne weitere Vorschläge.
                }
            }
        }

        var items = await ItemsAsync(lexware, row, vouchers, suggested, cancellationToken);
        return TypedResults.Ok(new BauakteVoucherCandidatesResponse(true, items.OrderByDescending(i => i.Suggested).ToList()));
    }

    private static async Task<Results<Created<LexwareVoucherResponse>, ProblemHttpResult>> AssignVoucherAsync(Guid id, AssignLexwareVoucherRequest request,
        LexwareService lexware, IBauakteAccess access, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (await BauakteEntityAsync(lexware, access, id, cancellationToken) is not { } bauakte)
        {
            return BauakteWriteProblems.NotFound();
        }

        var db = lexware.Db;
        var lexwareId = request.LexwareId.Trim();
        if (await db.Set<LexwareVoucherLink>().AsNoTracking().Where(l => l.LexwareId == lexwareId)
                .Join(db.Set<Bauakte>().AsNoTracking(), l => l.BauakteId, b => b.Id, (l, b) => new { b.Id, b.Number, b.Title })
                .FirstOrDefaultAsync(cancellationToken) is { } owner)
        {
            return Problem(new LexwareException("ALREADY_LINKED", owner.Id == id
                ? "Dieser Beleg ist der Bauakte bereits zugeordnet."
                : $"Dieser Beleg ist bereits der Bauakte {owner.Number ?? owner.Title} zugeordnet. Lösen Sie die Zuordnung zuerst dort."));
        }

        var row = await lexware.SettingsAsync(cancellationToken);
        var voucher = await lexware.Client.GetVoucherAsync(lexware.Connection(row), request.Type.Trim().ToLowerInvariant(), lexwareId, cancellationToken);
        var link = new LexwareVoucherLink
        {
            Id = Guid.CreateVersion7(), BauakteId = id, LexwareId = voucher.Id, VoucherType = voucher.Type, LinkedByUserId = currentUser.UserId,
            LinkedAt = lexware.Clock.GetUtcNow(),
        };
        lexware.Apply(link, voucher);
        db.Add(link);
        audit.Record("lexware.voucher_assigned", currentUser.UserId, "bauakte", id.ToString(), new Dictionary<string, string?>
        {
            ["bauakte"] = bauakte.Number ?? bauakte.Title, ["kind"] = link.VoucherType, ["number"] = link.Number,
        });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/v1/bauakten/{id}/lexware", ToResponse(lexware, row, link));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UnassignVoucherAsync(Guid id, Guid linkId, LexwareService lexware, IBauakteAccess access,
        IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        if (!await access.CanReadAsync(id, cancellationToken)
            || await db.Set<LexwareVoucherLink>().SingleOrDefaultAsync(l => l.Id == linkId && l.BauakteId == id, cancellationToken) is not { } link)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Nicht gefunden",
                detail: "Dieser Beleg ist der Bauakte nicht zugeordnet.", extensions: Code("LEXWARE_NOT_LINKED"));
        }

        db.Remove(link);
        audit.Record("lexware.voucher_unassigned", currentUser.UserId, "bauakte", id.ToString(), new Dictionary<string, string?>
        {
            ["kind"] = link.VoucherType, ["number"] = link.Number,
        });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    // ---------- Beleg als PDF ----------

    private static readonly LexwareException DraftWithoutDocument = new("NO_DOCUMENT", "Für Entwürfe stellt Lexware Office kein PDF bereit.");

    /// <summary>PDF durchreichen: zur Anzeige im Browser oder mit <paramref name="download"/> als Anhang. Gespeichert wird es nicht.</summary>
    private static FileContentHttpResult Pdf(HttpContext http, LexwareDocument document, string type, string? number, bool download)
    {
        var kind = type switch
        {
            LexwareVoucherTypes.Invoice => "Rechnung",
            LexwareVoucherTypes.Quotation => "Angebot",
            LexwareVoucherTypes.CreditNote => "Gutschrift",
            LexwareVoucherTypes.OrderConfirmation => "Auftragsbestaetigung",
            _ => "Beleg",
        };
        var safe = new string((number ?? string.Empty).Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').ToArray());
        var name = safe.Length > 0 ? $"{kind}-{safe}.pdf" : $"{kind}.pdf";
        http.Response.Headers.CacheControl = "private, no-store";
        if (download)
        {
            return TypedResults.File(document.Content, "application/pdf", name);
        }

        // Anzeige in der Vorschau der Weboberfläche (Rahmen derselben Herkunft).
        http.Items[EstrichOne.Api.Infrastructure.Http.SecurityHeadersMiddleware.AllowSameOriginFramingKey] = true;
        http.Response.Headers.ContentDisposition = $"inline; filename=\"{name}\"";
        return TypedResults.File(document.Content, "application/pdf");
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> CustomerVoucherDocumentAsync(Guid contactId, string lexwareId,
        LexwareService lexware, HttpContext http, CancellationToken cancellationToken, [FromQuery] bool download = false)
    {
        var db = lexware.Db;
        if (!await db.Set<Contact>().AnyAsync(c => c.Id == contactId, cancellationToken))
        {
            return CustomerNotFound();
        }

        var lexwareContactId = await db.Set<LexwareContactLink>().AsNoTracking().Where(l => l.ContactId == contactId).Select(l => l.LexwareContactId)
            .FirstOrDefaultAsync(cancellationToken);
        var row = await lexware.SettingsAsync(cancellationToken);

        // Nur Belege des verknüpften Kontakts: Der Beleg muss in dessen Belegliste stehen (bei Bedarf einmal neu geladen).
        LexwareVoucher? voucher = null;
        if (lexwareContactId is not null)
        {
            voucher = (await lexware.ContactVouchersAsync(row, lexwareContactId, refresh: false, cancellationToken)).Vouchers.FirstOrDefault(v => v.Id == lexwareId);
            if (voucher is null || voucher.Status == "draft")
            {
                voucher = (await lexware.ContactVouchersAsync(row, lexwareContactId, refresh: true, cancellationToken)).Vouchers.FirstOrDefault(v => v.Id == lexwareId);
            }
        }

        if (voucher is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Nicht gefunden",
                detail: "Dieser Beleg gehört nicht zu diesem Kunden.", extensions: Code("LEXWARE_VOUCHER_NOT_FOUND"));
        }

        if (voucher.Status == "draft")
        {
            return Problem(DraftWithoutDocument);
        }

        return Pdf(http, await lexware.VoucherDocumentAsync(row, voucher.Type, voucher.Id, cancellationToken), voucher.Type, voucher.Number, download);
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> BauakteVoucherDocumentAsync(Guid id, Guid linkId, LexwareService lexware,
        IBauakteAccess access, HttpContext http, CancellationToken cancellationToken, [FromQuery] bool download = false)
    {
        var db = lexware.Db;
        if (!await access.CanReadAsync(id, cancellationToken)
            || await db.Set<LexwareVoucherLink>().SingleOrDefaultAsync(l => l.Id == linkId && l.BauakteId == id, cancellationToken) is not { } link)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Nicht gefunden",
                detail: "Dieser Beleg ist der Bauakte nicht zugeordnet.", extensions: Code("LEXWARE_NOT_LINKED"));
        }

        var row = await lexware.SettingsAsync(cancellationToken);
        if (link.Status == "draft")
        {
            // Der gespeicherte Stand kann veraltet sein: einmal frisch prüfen, bevor es „kein PDF“ heißt.
            await lexware.SyncAsync(row, [link], force: true, cancellationToken);
            if (link.Status == "draft")
            {
                return Problem(DraftWithoutDocument);
            }
        }

        return Pdf(http, await lexware.VoucherDocumentAsync(row, link.VoucherType, link.LexwareId, cancellationToken), link.VoucherType, link.Number, download);
    }

    // ---------- Fehler ----------

    private static Dictionary<string, object?> Code(string code) => new() { ["code"] = code };

    private static ProblemHttpResult CustomerNotFound() => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Kontakt nicht gefunden",
        extensions: Code("CONTACT_NOT_FOUND"));

    /// <summary>Lexware-Fehler als Problem-Antwort: nie 500, immer mit Code <c>LEXWARE_…</c> und verständlichem Text.</summary>
    internal static ProblemHttpResult Problem(LexwareException exception)
    {
        var (status, title) = exception.Code switch
        {
            "NOT_CONFIGURED" or "KEY_UNREADABLE" => (StatusCodes.Status409Conflict, "Lexware Office ist nicht eingerichtet"),
            "ALREADY_LINKED" or "NOT_A_CUSTOMER" or "ANONYMIZED" or "SYNC_DISABLED" or "SYNC_PAUSED" or "CHANGE_SYNC_OFF" or "CUSTOMER_CHANGED" =>
                (StatusCodes.Status409Conflict, Tag),
            "NO_DOCUMENT" or "DOCUMENT_TOO_LARGE" => (StatusCodes.Status409Conflict, "Kein PDF verfügbar"),
            "INVALID_BASE_URL" or "UNKNOWN_VOUCHER_TYPE" => (StatusCodes.Status422UnprocessableEntity, Tag),
            "OFFLINE" or "TIMEOUT" or "UNAVAILABLE" or "RATE_LIMITED" => (StatusCodes.Status503ServiceUnavailable, "Lexware Office ist nicht erreichbar"),
            _ => (StatusCodes.Status424FailedDependency, "Lexware Office hat die Anfrage abgelehnt"),
        };
        return TypedResults.Problem(statusCode: status, title: title, detail: exception.Message, extensions: Code("LEXWARE_" + exception.Code));
    }

    private static async ValueTask<object?> GuardAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (LexwareException exception)
        {
            return Problem(exception);
        }
        catch (DbUpdateException exception) when (LexwareService.UniqueViolation(exception) is { } conflict)
        {
            return Problem(conflict);
        }
    }
}
