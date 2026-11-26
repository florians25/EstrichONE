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
/// <param name="CanCreate">Recht <c>invoices.create</c>.</param>
/// <param name="CanManage">Recht <c>settings.integrations</c>.</param>
/// <param name="SyncDefault">Vorgabe für „Mit Lexware Office abgleichen“ bei neuen Kunden (Modul an und Einstellung gesetzt).</param>
public sealed record LexwareStatusResponse(bool Enabled, bool Configured, bool CanRead, bool CanCreate, bool CanManage, bool SyncDefault = false);

/// <param name="HasApiKey">Schlüssel hinterlegt; der Schlüssel selbst wird nie zurückgegeben.</param>
/// <param name="BaseUrl">Wirksame Basis-URL der API.</param>
/// <param name="CustomBaseUrl">Abweichende Basis-URL eingestellt (sonst die Vorgabe).</param>
/// <param name="KeyHelpUrl">Seite in Lexware Office, auf der der Schlüssel erzeugt wird.</param>
public sealed record LexwareSettingsResponse(
    bool ModuleEnabled, bool HasApiKey, string BaseUrl, bool CustomBaseUrl, int PaymentTermDays, string? PaymentTermLabel, string? OrganizationName,
    DateTimeOffset? LastTestAt, bool? LastTestOk, string? LastTestError, int LinkedCustomers, int LinkedInvoices, string KeyHelpUrl, long Version,
    bool NewCustomersSync = true);

/// <param name="ApiKey">Neuer Schlüssel; <c>null</c> oder leer lässt den gespeicherten unverändert.</param>
/// <param name="ClearApiKey">Gespeicherten Schlüssel entfernen.</param>
/// <param name="BaseUrl">Leer = Vorgabe <c>https://api.lexware.io</c>.</param>
/// <param name="NewCustomersSync">Neue Kunden standardmäßig mit Lexware Office abgleichen; ohne Angabe unverändert.</param>
public sealed record SaveLexwareSettingsRequest(
    [property: MaxLength(500)] string? ApiKey = null, bool ClearApiKey = false, [property: MaxLength(LexwareSettingsRow.MaxUrl)] string? BaseUrl = null,
    [property: Range(0, 365)] int PaymentTermDays = 14, [property: MaxLength(LexwareSettingsRow.MaxLabel)] string? PaymentTermLabel = null,
    long? Version = null, bool? NewCustomersSync = null);

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
    bool Archived, string? LinkedLexwareContactId, bool SyncEnabled = false);

/// <summary>Möglicher Treffer in EstrichONE mit Übereinstimmungsgründen.</summary>
public sealed record LexwareCustomerCandidate(LexwareCustomerSummary Customer, int Score, IReadOnlyList<string> Reasons, bool Strong);

public sealed record LexwareContactLinkResponse(
    string LexwareContactId, string? Name, string? Number, string Url, LexwareLinkOrigin Origin, DateTimeOffset LinkedAt);

/// <summary>Verknüpfungsstatus eines Kunden.</summary>
/// <param name="SyncEnabled">„Mit Lexware Office abgleichen“ ist für den Kunden eingeschaltet (sonst bleibt er nur in EstrichONE).</param>
public sealed record CustomerLexwareResponse(Guid ContactId, LexwareContactLinkResponse? Link, bool CanEdit, bool SyncEnabled = false);

public sealed record SetLexwareSyncRequest(bool Enabled);

public sealed record EnableLexwareSyncRequest([property: Required, MinLength(1), MaxLength(2000)] IReadOnlyList<Guid> ContactIds);

public sealed record EnableLexwareSyncResponse(int Changed);

public sealed record LexwareCandidatesResponse(IReadOnlyList<LexwareContactCandidate> Candidates, DateTimeOffset? LoadedAt);

public sealed record LinkLexwareContactRequest([property: Required, MaxLength(100)] string LexwareContactId);

/// <param name="CreateAnyway">Bewusst neu anlegen, obwohl die Prüfung mögliche Dubletten gefunden hat.</param>
public sealed record CreateLexwareContactRequest(bool CreateAnyway = false);

/// <param name="Created">Kontakt angelegt und verknüpft. Sonst stehen in <paramref name="Candidates"/> die möglichen Dubletten zur Entscheidung.</param>
public sealed record CreateLexwareContactResponse(bool Created, LexwareContactLinkResponse? Link, IReadOnlyList<LexwareContactCandidate> Candidates);

public sealed record LexwareContactListResponse(
    IReadOnlyList<LexwareContactSummary> Items, int Page, int PageSize, int Total, DateTimeOffset LoadedAt);

/// <param name="CreateAnyway">Bewusst neu anlegen, obwohl die Prüfung mögliche Dubletten gefunden hat.</param>
public sealed record ImportLexwareContactRequest(bool CreateAnyway = false);

/// <param name="Imported">Kunde angelegt und verknüpft. Sonst stehen in <paramref name="Candidates"/> die möglichen Dubletten zur Entscheidung.</param>
public sealed record ImportLexwareContactResponse(bool Imported, Guid? ContactId, string? CustomerNumber, IReadOnlyList<LexwareCustomerCandidate> Candidates);

public sealed record LexwareLinkedPair(LexwareCustomerSummary Customer, LexwareContactSummary? Lexware, bool MissingInLexware);

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
public sealed record LexwareReconciliationResponse(
    DateTimeOffset LoadedAt, int Linked, int OnlyEstrichOne, int OnlyLexware, int PossibleDuplicates, int UniqueMatches,
    IReadOnlyList<LexwareLinkedPair> LinkedItems, IReadOnlyList<LexwareCustomerSummary> OnlyEstrichOneItems,
    IReadOnlyList<LexwareContactSummary> OnlyLexwareItems, IReadOnlyList<LexwareSuggestion> Suggestions,
    IReadOnlyList<LexwareInternalDuplicate> InternalDuplicates, bool Truncated, int Excluded = 0,
    IReadOnlyList<LexwareCustomerSummary>? ExcludedItems = null);

public sealed record LexwareLinkPair(Guid ContactId, [property: Required, MaxLength(100)] string LexwareContactId);

/// <param name="Pairs">Paare aus der Vorschau; verknüpft wird nur, was weiterhin ein eindeutiger Treffer ist.</param>
public sealed record LinkUniqueLexwareRequest([property: Required, MaxLength(2000)] IReadOnlyList<LexwareLinkPair> Pairs);

public sealed record LinkUniqueLexwareResponse(int Linked, int Skipped);

/// <summary>Beleg der Bauakte in Lexware Office.</summary>
/// <param name="Status"><c>draft</c>, <c>open</c>, <c>overdue</c> (offen und Fälligkeit überschritten), <c>paid</c>, <c>paidoff</c>, <c>voided</c>.</param>
/// <param name="Created">Aus EstrichONE als Entwurf angelegt (sonst nachträglich zugeordnet).</param>
public sealed record LexwareVoucherResponse(
    Guid Id, string LexwareId, string? Number, string? Title, DateOnly? VoucherDate, DateOnly? DueDate, decimal? NetAmount, decimal? GrossAmount,
    string Currency, string Status, bool Created, string Url, DateTimeOffset? SyncedAt, string? SyncError);

/// <param name="SyncEnabled">„Mit Lexware Office abgleichen“ ist für den Kunden eingeschaltet.</param>
public sealed record LexwareBauakteCustomer(Guid Id, string Name, string? LexwareContactId, bool SyncEnabled = false);

/// <param name="SyncError">Abgleich mit Lexware Office fehlgeschlagen; die Liste zeigt den zuletzt gespeicherten Stand.</param>
public sealed record BauakteLexwareResponse(
    Guid BauakteId, bool Configured, bool CanCreate, LexwareBauakteCustomer? Customer, IReadOnlyList<LexwareVoucherResponse> Invoices, string? SyncError);

/// <param name="Kind"><c>custom</c> (Position mit Menge und Preis) oder <c>text</c> (Textzeile).</param>
/// <param name="UnitPrice">Einzelpreis netto in Euro.</param>
/// <param name="TaxRate">Steuersatz in Prozent (0, 7 oder 19).</param>
public sealed record LexwareInvoiceLine(
    [property: Required, MaxLength(10)] string Kind, [property: Required, MaxLength(255)] string Name, [property: MaxLength(2000)] string? Description = null,
    [property: Range(0, 10_000_000)] decimal? Quantity = null, [property: MaxLength(30)] string? UnitName = null,
    [property: Range(0, 100_000_000)] decimal? UnitPrice = null, decimal? TaxRate = null);

/// <summary>Vorschlag für den Dialog „Rechnung erstellen“.</summary>
public sealed record LexwareInvoiceProposal(
    LexwareBauakteCustomer? Customer, string Title, string Introduction, string Remark, DateOnly VoucherDate, DateOnly? ServiceDate,
    DateOnly? ServiceEndDate, int PaymentTermDays, IReadOnlyList<LexwareInvoiceLine> Lines);

public sealed record CreateLexwareInvoiceRequest(
    [property: Required, MinLength(1), MaxLength(100)] IReadOnlyList<LexwareInvoiceLine> Lines, DateOnly? VoucherDate = null,
    [property: MaxLength(250)] string? Title = null, [property: MaxLength(2000)] string? Introduction = null,
    [property: MaxLength(2000)] string? Remark = null, DateOnly? ServiceDate = null, DateOnly? ServiceEndDate = null,
    [property: Range(0, 365)] int? PaymentTermDays = null);

/// <param name="AssignedBauakteId">Bauakte, der die Rechnung bereits zugeordnet ist.</param>
public sealed record LexwareVoucherHit(
    string LexwareId, string? Number, DateOnly? VoucherDate, DateOnly? DueDate, decimal? GrossAmount, string Currency, string Status, string? ContactName,
    Guid? AssignedBauakteId, string? AssignedBauakte);

public sealed record AssignLexwareInvoiceRequest([property: Required, MaxLength(100)] string LexwareId);

/// <summary>
/// Lexware Office: Einstellungen (nur mit <c>settings.integrations</c>), Kundenverknüpfung mit beidseitiger
/// Dublettenprüfung, Rechnungsentwurf aus der Bauakte und Belegliste mit Statusabgleich.
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
        customers.MapGet("/candidates", CandidatesAsync).WithName("FindLexwareContactCandidates")
            .WithSummary("Dublettenprüfung in Lexware Office für diesen Kunden; mit q gezielte Suche nach Name, E-Mail-Adresse oder Kundennummer")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status424FailedDependency).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequirePermission(Permissions.InvoicesCreate);
        customers.MapPost("/link", LinkCustomerAsync).WithName("LinkCustomerToLexware")
            .WithSummary("Kunde mit vorhandenem Lexware-Kontakt verknüpfen (je Seite höchstens eine Verknüpfung)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status424FailedDependency).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequirePermission(Permissions.InvoicesCreate);
        customers.MapPost("/create", CreateContactAsync).WithName("CreateLexwareContact")
            .WithSummary("Kunde in Lexware Office anlegen; vorher immer Dublettenprüfung, bei Treffern nur mit createAnyway")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status424FailedDependency).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequirePermission(Permissions.InvoicesCreate);
        customers.MapPut("/sync", SetSyncAsync).WithName("SetCustomerLexwareSync")
            .WithSummary("„Mit Lexware Office abgleichen“ für den Kunden ein- oder ausschalten (eine bestehende Verknüpfung bleibt, bis sie gelöst wird)")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.InvoicesCreate);
        customers.MapDelete("/link", UnlinkCustomerAsync).WithName("UnlinkCustomerFromLexware")
            .WithSummary("Verknüpfung lösen (in Lexware Office wird nichts gelöscht)").ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(Permissions.InvoicesCreate);

        var contacts = api.MapGroup("/lexware/contacts").WithTags(Tag).RequireModule(AppModules.Lexware).AddEndpointFilter(GuardAsync);
        contacts.MapGet("/", ListContactsAsync).WithName("ListLexwareContacts")
            .WithSummary("Kunden aus Lexware Office, seitenweise mit Suche (Liste kurz zwischengespeichert); unlinked=true nur ohne Verknüpfung")
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status424FailedDependency)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable).RequirePermission(Permissions.InvoicesCreate);
        contacts.MapGet("/{lexwareContactId}/candidates", CustomerCandidatesAsync).WithName("FindCustomerCandidatesForLexwareContact")
            .WithSummary("Dublettenprüfung in EstrichONE für einen Lexware-Kontakt")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status424FailedDependency).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequirePermission(Permissions.InvoicesCreate);
        contacts.MapPost("/{lexwareContactId}/import", ImportContactAsync).WithName("ImportLexwareContact")
            .WithSummary("Lexware-Kontakt als Kunde nach EstrichONE übernehmen; vorher immer Dublettenprüfung, bei Treffern nur mit createAnyway")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status424FailedDependency).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequirePermission(Permissions.InvoicesCreate);

        var bauakte = api.MapGroup("/bauakten/{id:guid}/lexware").WithTags(Tag).RequireModule(AppModules.Lexware).AddEndpointFilter(GuardAsync);
        bauakte.MapGet("/", BauakteAsync).WithName("GetBauakteLexware")
            .WithSummary("Rechnungen der Bauakte mit Statusabgleich (kurzer Cache; refresh=true gleicht alle sofort ab)")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.InvoicesRead);
        bauakte.MapGet("/invoice-proposal", ProposalAsync).WithName("GetLexwareInvoiceProposal")
            .WithSummary("Vorschlag für den Rechnungsentwurf: Texte, Positionen aus den Kennwerten, Leistungszeitraum, Zahlungsziel")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.InvoicesCreate);
        bauakte.MapPost("/invoices", CreateInvoiceAsync).WithName("CreateLexwareInvoiceDraft")
            .WithSummary("Rechnung als Entwurf in Lexware Office anlegen (kein Abschluss) und der Bauakte zuordnen")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesProblem(StatusCodes.Status424FailedDependency)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable).RequirePermission(Permissions.InvoicesCreate);
        bauakte.MapGet("/invoices/search", SearchInvoicesAsync).WithName("SearchLexwareInvoices")
            .WithSummary("Vorhandene Rechnung in Lexware Office über die Belegnummer suchen")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status424FailedDependency).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequirePermission(Permissions.InvoicesCreate);
        bauakte.MapPost("/invoices/assign", AssignInvoiceAsync).WithName("AssignLexwareInvoice")
            .WithSummary("Vorhandene Rechnung aus Lexware Office der Bauakte zuordnen")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status424FailedDependency).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequirePermission(Permissions.InvoicesCreate);
        bauakte.MapDelete("/invoices/{linkId:guid}", UnassignInvoiceAsync).WithName("UnassignLexwareInvoice")
            .WithSummary("Zuordnung der Rechnung zur Bauakte lösen (in Lexware Office wird nichts gelöscht)")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.InvoicesCreate);
    }

    // ---------- Status und Einstellungen ----------

    private static async Task<Ok<LexwareStatusResponse>> StatusAsync(LexwareService lexware, ISystemSettings system, IUserPermissions permissions,
        CancellationToken cancellationToken)
    {
        var granted = await permissions.GetAsync(cancellationToken);
        var enabled = (await system.GetAsync(cancellationToken)).Modules.IsEnabled(AppModules.Lexware);
        var row = await lexware.SettingsAsync(cancellationToken);
        return TypedResults.Ok(new LexwareStatusResponse(enabled, row.Configured, granted.Contains(Permissions.InvoicesRead),
            granted.Contains(Permissions.InvoicesCreate), granted.Contains(Permissions.SettingsIntegrations), enabled && row.NewCustomersSync));
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
            !string.IsNullOrWhiteSpace(row.BaseUrl), row.PaymentTermDays, row.PaymentTermLabel, row.OrganizationName, row.LastTestAt, row.LastTestOk,
            row.LastTestError, await db.Set<LexwareContactLink>().CountAsync(cancellationToken),
            await db.Set<LexwareVoucherLink>().CountAsync(cancellationToken), LexwareService.KeyHelpUrl, row.Version, row.NewCustomersSync);
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
        row.PaymentTermDays = request.PaymentTermDays;
        row.PaymentTermLabel = LexwareService.Cut(request.PaymentTermLabel, LexwareSettingsRow.MaxLabel);
        row.NewCustomersSync = request.NewCustomersSync ?? row.NewCustomersSync;
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
        });
        await lexware.Db.SaveChangesAsync(cancellationToken);
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

        var row = await lexware.SettingsAsync(cancellationToken);
        var link = await db.Set<LexwareContactLink>().AsNoTracking().SingleOrDefaultAsync(l => l.ContactId == contactId, cancellationToken);
        var sync = await db.Set<Contact>().Where(c => c.Id == contactId).Select(c => c.LexwareSync).SingleAsync(cancellationToken);
        return TypedResults.Ok(new CustomerLexwareResponse(contactId, link is null ? null : ToResponse(lexware, row, link),
            row.Configured && await permissions.HasAsync(Permissions.InvoicesCreate, cancellationToken), sync));
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
        LexwareService lexware, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
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
        return TypedResults.Ok(new CustomerLexwareResponse(contactId, ToResponse(lexware, row, link), true, true));
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
            audit.Record("lexware.sync_changed", currentUser.UserId, "contact", contactId.ToString(), new Dictionary<string, string?>
            {
                ["name"] = contact.Name, ["lexwareSync"] = request.Enabled ? "ein" : "aus",
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        // Eine bestehende Verknüpfung bleibt beim Ausschalten bestehen: Der Kontakt und seine Rechnungen liegen bereits in
        // Lexware Office, und ein stilles Lösen würde die Belege der Bauakten vom Kunden trennen. Gelöst wird nur bewusst.
        var row = await lexware.SettingsAsync(cancellationToken);
        var link = await db.Set<LexwareContactLink>().AsNoTracking().SingleOrDefaultAsync(l => l.ContactId == contactId, cancellationToken);
        return TypedResults.Ok(new CustomerLexwareResponse(contactId, link is null ? null : ToResponse(lexware, row, link), row.Configured, contact.LexwareSync));
    }

    private static async Task<Ok<EnableLexwareSyncResponse>> EnableSyncAsync(EnableLexwareSyncRequest request, LexwareService lexware, IAuditLog audit,
        ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        var ids = request.ContactIds.Distinct().ToList();
        var customers = await db.Set<Contact>().Where(c => ids.Contains(c.Id) && !c.LexwareSync && c.AnonymizedAt == null).ToListAsync(cancellationToken);
        foreach (var customer in customers)
        {
            customer.SetLexwareSync(true);
        }

        if (customers.Count > 0)
        {
            audit.Record("lexware.sync_enabled_bulk", currentUser.UserId, "settings", "lexware", new Dictionary<string, string?>
            {
                ["count"] = customers.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(new EnableLexwareSyncResponse(customers.Count));
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
        CancellationToken cancellationToken, [FromQuery] bool refresh = false)
    {
        var data = await ReconcileAsync(lexware, refresh, cancellationToken);
        var row = data.Row;
        var remoteById = data.Remote.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var names = data.Customers.ToDictionary(c => c.Id, c => c.Name);
        var owners = data.Links.Where(l => names.ContainsKey(l.Key)).ToDictionary(l => l.Value, l => (l.Key, names[l.Key]), StringComparer.Ordinal);
        LexwareContactSummary Remote(LexwareContact c) =>
            owners.TryGetValue(c.Id, out var owner) ? Summary(lexware, row, c, owner.Key, owner.Item2) : Summary(lexware, row, c);

        var linked = data.Customers.Where(c => data.Links.ContainsKey(c.Id)).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c =>
            {
                var remote = remoteById.GetValueOrDefault(data.Links[c.Id]);
                return new LexwareLinkedPair(Summary(c, data.Links[c.Id]), remote is null ? null : Remote(remote), remote is null);
            }).ToList();
        var onlyLocal = data.Customers.Where(c => !data.Links.ContainsKey(c.Id) && c.ArchivedAt is null && c.LexwareSync)
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(c => Summary(c, null)).ToList();
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
            || within.Count > ListLimit;
        return TypedResults.Ok(new LexwareReconciliationResponse(data.LoadedAt, linked.Count, onlyLocal.Count, onlyRemote.Count,
            suggestions.Count + within.Count, suggestions.Count(s => s.Unique), linked.Take(ListLimit).ToList(), onlyLocal.Take(ListLimit).ToList(),
            onlyRemote.Take(ListLimit).ToList(), suggestions.Take(ListLimit).ToList(), within.Take(ListLimit).ToList(), truncated, excluded.Count,
            excluded.Take(ListLimit * 4).ToList()));
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
        }

        return TypedResults.Ok(new LinkUniqueLexwareResponse(linked, request.Pairs.Count - linked));
    }

    // ---------- Bauakte ----------

    private static LexwareVoucherResponse ToResponse(LexwareService lexware, LexwareSettingsRow row, LexwareVoucherLink link) => new(
        link.Id, link.LexwareId, link.Number, link.Title, link.VoucherDate, link.DueDate, link.NetAmount, link.GrossAmount, link.Currency,
        lexware.DisplayStatus(link), link.Created, lexware.InvoiceUrl(row, link.LexwareId, link.Status), link.SyncedAt, link.SyncError);

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
        var invoices = links.OrderByDescending(l => l.VoucherDate ?? DateOnly.FromDateTime(l.LinkedAt.UtcDateTime)).ThenByDescending(l => l.LinkedAt)
            .Select(l => ToResponse(lexware, row, l)).ToList();
        return TypedResults.Ok(new BauakteLexwareResponse(id, row.Configured,
            row.Configured && await permissions.HasAsync(Permissions.InvoicesCreate, cancellationToken),
            await BauakteCustomerAsync(db, bauakte, cancellationToken), invoices, error));
    }

    private static async Task<Results<Ok<LexwareInvoiceProposal>, ProblemHttpResult>> ProposalAsync(Guid id, LexwareService lexware, IBauakteAccess access,
        CancellationToken cancellationToken)
    {
        if (await BauakteEntityAsync(lexware, access, id, cancellationToken) is not { } bauakte)
        {
            return BauakteWriteProblems.NotFound();
        }

        var row = await lexware.SettingsAsync(cancellationToken);
        var (introduction, lines) = await lexware.ProposalAsync(bauakte, cancellationToken);
        var today = lexware.Today();
        // Leistungszeitraum nur vorschlagen, wenn die Ausführung schon begonnen hat.
        var start = bauakte.PlannedStart is { } planned && planned <= today ? planned : (DateOnly?)null;
        var end = start is not null && bauakte.PlannedEnd is { } plannedEnd && plannedEnd <= today && plannedEnd > start ? plannedEnd : (DateOnly?)null;
        return TypedResults.Ok(new LexwareInvoiceProposal(await BauakteCustomerAsync(lexware.Db, bauakte, cancellationToken), "Rechnung", introduction,
            "Vielen Dank für Ihren Auftrag.", today, start, end, row.PaymentTermDays,
            lines.Select(l => new LexwareInvoiceLine(l.Kind, l.Name, l.Description, l.Quantity, l.UnitName, null, l.TaxRatePercentage)).ToList()));
    }

    private static readonly decimal[] TaxRates = [0m, 7m, 19m];

    private static async Task<Results<Created<LexwareVoucherResponse>, ProblemHttpResult>> CreateInvoiceAsync(Guid id, CreateLexwareInvoiceRequest request,
        LexwareService lexware, IBauakteAccess access, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (await BauakteEntityAsync(lexware, access, id, cancellationToken) is not { } bauakte)
        {
            return BauakteWriteProblems.NotFound();
        }

        var db = lexware.Db;
        var row = await lexware.SettingsAsync(cancellationToken);
        var connection = lexware.Connection(row);
        var customer = await BauakteCustomerAsync(db, bauakte, cancellationToken);
        if (customer is null)
        {
            return Problem(new LexwareException("NO_CUSTOMER", "Die Bauakte hat keinen Kunden. Bitte ordnen Sie der Bauakte zuerst einen Kunden zu."));
        }

        if (customer.LexwareContactId is null && !customer.SyncEnabled)
        {
            return Problem(LexwareService.SyncDisabled(customer.Name));
        }

        if (customer.LexwareContactId is null)
        {
            return Problem(new LexwareException("CUSTOMER_NOT_LINKED",
                $"Der Kunde „{customer.Name}“ ist noch nicht mit Lexware Office verknüpft. Bitte verknüpfen Sie den Kunden oder legen Sie ihn in Lexware Office an."));
        }

        var lines = new List<LexwareLineDraft>();
        foreach (var line in request.Lines)
        {
            if (line.Kind is not ("custom" or "text") || string.IsNullOrWhiteSpace(line.Name))
            {
                return Invalid("Jede Position braucht eine Bezeichnung.");
            }

            if (line.Kind == "text")
            {
                lines.Add(new LexwareLineDraft("text", line.Name.Trim(), line.Description, 0m, null, 0m, 0m));
                continue;
            }

            var tax = line.TaxRate ?? 19m;
            if (!TaxRates.Contains(tax))
            {
                return Invalid("Als Steuersatz sind 0, 7 und 19 Prozent möglich.");
            }

            if (line.Quantity is not > 0)
            {
                return Invalid($"Bitte geben Sie bei der Position „{line.Name.Trim()}“ eine Menge größer als 0 an.");
            }

            lines.Add(new LexwareLineDraft("custom", line.Name.Trim(), line.Description, decimal.Round(line.Quantity.Value, 4),
                string.IsNullOrWhiteSpace(line.UnitName) ? "Stück" : line.UnitName.Trim(), decimal.Round(line.UnitPrice ?? 0m, 4), tax));
        }

        if (lines.All(l => l.Kind == "text"))
        {
            return Invalid("Die Rechnung braucht mindestens eine Position mit Menge.");
        }

        if (request.ServiceDate is { } from && request.ServiceEndDate is { } to && to < from)
        {
            return Invalid("Das Ende des Leistungszeitraums darf nicht vor dem Beginn liegen.");
        }

        var days = request.PaymentTermDays ?? row.PaymentTermDays;
        var voucherDate = request.VoucherDate ?? lexware.Today();
        var draft = new LexwareInvoiceDraft(customer.LexwareContactId, voucherDate, LexwareService.Cut(request.Title, 250) ?? "Rechnung",
            LexwareService.Cut(request.Introduction, 2000), LexwareService.Cut(request.Remark, 2000), lines, days,
            LexwareService.PaymentTermLabel(row, days), request.ServiceDate, request.ServiceDate is null ? null : request.ServiceEndDate);
        var lexwareId = await lexware.Client.CreateInvoiceDraftAsync(connection, draft, cancellationToken);

        var net = lines.Sum(l => decimal.Round(l.Quantity * l.NetUnitPrice, 2));
        var gross = lines.Sum(l => decimal.Round(l.Quantity * l.NetUnitPrice * (1 + (l.TaxRatePercentage / 100m)), 2));
        var link = new LexwareVoucherLink
        {
            Id = Guid.CreateVersion7(), BauakteId = id, LexwareId = lexwareId, Title = draft.Title, VoucherDate = voucherDate,
            DueDate = voucherDate.AddDays(days), NetAmount = net, GrossAmount = gross, Status = "draft", Created = true,
            LinkedByUserId = currentUser.UserId, LinkedAt = lexware.Clock.GetUtcNow(),
        };
        try
        {
            // Beträge und Status so übernehmen, wie Lexware sie berechnet hat.
            lexware.Apply(link, await lexware.Client.GetInvoiceAsync(connection, lexwareId, cancellationToken));
        }
        catch (LexwareException)
        {
            // Der Entwurf ist angelegt; der nächste Abgleich holt die Werte nach.
        }

        db.Add(link);
        audit.Record("lexware.invoice_created", currentUser.UserId, "bauakte", id.ToString(), new Dictionary<string, string?>
        {
            ["bauakte"] = bauakte.Number ?? bauakte.Title, ["customer"] = customer.Name,
            ["amount"] = link.GrossAmount?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
        });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/v1/bauakten/{id}/lexware", ToResponse(lexware, row, link));
    }

    private static async Task<Results<Ok<List<LexwareVoucherHit>>, ProblemHttpResult>> SearchInvoicesAsync(Guid id, LexwareService lexware,
        IBauakteAccess access, [FromQuery, MaxLength(100)] string? number, CancellationToken cancellationToken)
    {
        if (await BauakteEntityAsync(lexware, access, id, cancellationToken) is null)
        {
            return BauakteWriteProblems.NotFound();
        }

        if (string.IsNullOrWhiteSpace(number))
        {
            return TypedResults.Ok(new List<LexwareVoucherHit>());
        }

        var db = lexware.Db;
        var hits = await lexware.Client.FindInvoicesByNumberAsync(await lexware.ConnectionAsync(cancellationToken), number.Trim(), cancellationToken);
        var ids = hits.Select(h => h.Id).ToList();
        var assigned = (await db.Set<LexwareVoucherLink>().AsNoTracking().Where(l => ids.Contains(l.LexwareId))
            .Join(db.Set<Bauakte>().AsNoTracking(), l => l.BauakteId, b => b.Id, (l, b) => new { l.LexwareId, b.Id, b.Number, b.Title })
            .ToListAsync(cancellationToken)).ToDictionary(x => x.LexwareId, StringComparer.Ordinal);
        var today = lexware.Today();
        return TypedResults.Ok(hits.Select(h =>
        {
            var owner = assigned.GetValueOrDefault(h.Id);
            return new LexwareVoucherHit(h.Id, h.Number, h.VoucherDate, h.DueDate, h.GrossAmount, h.Currency,
                h.Status == "open" && h.DueDate is { } due && due < today ? "overdue" : h.Status, h.ContactName, owner?.Id,
                owner is null ? null : string.Join(' ', new[] { owner.Number, owner.Title }.Where(p => p is not null)));
        }).ToList());
    }

    private static async Task<Results<Created<LexwareVoucherResponse>, ProblemHttpResult>> AssignInvoiceAsync(Guid id, AssignLexwareInvoiceRequest request,
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
                ? "Diese Rechnung ist der Bauakte bereits zugeordnet."
                : $"Diese Rechnung ist bereits der Bauakte {owner.Number ?? owner.Title} zugeordnet. Lösen Sie die Zuordnung zuerst dort."));
        }

        var row = await lexware.SettingsAsync(cancellationToken);
        var invoice = await lexware.Client.GetInvoiceAsync(lexware.Connection(row), lexwareId, cancellationToken);
        var link = new LexwareVoucherLink
        {
            Id = Guid.CreateVersion7(), BauakteId = id, LexwareId = invoice.Id, LinkedByUserId = currentUser.UserId, LinkedAt = lexware.Clock.GetUtcNow(),
        };
        lexware.Apply(link, invoice);
        db.Add(link);
        audit.Record("lexware.invoice_assigned", currentUser.UserId, "bauakte", id.ToString(), new Dictionary<string, string?>
        {
            ["bauakte"] = bauakte.Number ?? bauakte.Title, ["number"] = link.Number,
        });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/v1/bauakten/{id}/lexware", ToResponse(lexware, row, link));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UnassignInvoiceAsync(Guid id, Guid linkId, LexwareService lexware, IBauakteAccess access,
        IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        if (!await access.CanReadAsync(id, cancellationToken)
            || await db.Set<LexwareVoucherLink>().SingleOrDefaultAsync(l => l.Id == linkId && l.BauakteId == id, cancellationToken) is not { } link)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Nicht gefunden",
                detail: "Diese Rechnung ist der Bauakte nicht zugeordnet.", extensions: Code("LEXWARE_NOT_LINKED"));
        }

        db.Remove(link);
        audit.Record("lexware.invoice_unassigned", currentUser.UserId, "bauakte", id.ToString(), new Dictionary<string, string?> { ["number"] = link.Number });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    // ---------- Fehler ----------

    private static Dictionary<string, object?> Code(string code) => new() { ["code"] = code };

    private static ProblemHttpResult CustomerNotFound() => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Kontakt nicht gefunden",
        extensions: Code("CONTACT_NOT_FOUND"));

    private static ProblemHttpResult Invalid(string detail) => TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
        title: "Rechnung", detail: detail, extensions: Code("LEXWARE_INVALID_INVOICE"));

    /// <summary>Lexware-Fehler als Problem-Antwort: nie 500, immer mit Code <c>LEXWARE_…</c> und verständlichem Text.</summary>
    internal static ProblemHttpResult Problem(LexwareException exception)
    {
        var (status, title) = exception.Code switch
        {
            "NOT_CONFIGURED" or "KEY_UNREADABLE" => (StatusCodes.Status409Conflict, "Lexware Office ist nicht eingerichtet"),
            "ALREADY_LINKED" or "NO_CUSTOMER" or "CUSTOMER_NOT_LINKED" or "NOT_A_CUSTOMER" or "ANONYMIZED" or "SYNC_DISABLED" => (StatusCodes.Status409Conflict, Tag),
            "INVALID_BASE_URL" => (StatusCodes.Status422UnprocessableEntity, Tag),
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
