using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Modules.Contacts.Exports;

namespace EstrichOne.Api.Modules.Lexware;

/// <summary>
/// Lexware Office (früher lexoffice): API-Schlüssel des Betriebs, Kundenverknüpfung mit beidseitiger Dublettenprüfung,
/// Abgleich späterer Änderungen an verknüpften Kunden und Anzeige der Belege (Liste, Status, PDF), dazu Artikelabgleich
/// („Lexware Office: Artikel“) und das Modul „Verkauf“ (Angebote, Auftragsbestätigungen, Rechnungen, Gutschriften) und „Eingangsbelege“ (Belege der Lieferanten, Belegeingang mit E-Rechnung). Ohne Schlüssel
/// bzw. abgeschaltet wirkungslos (docs/lexware-office.md).
/// </summary>
public sealed class LexwareModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LexwareOptions>().Bind(configuration.GetSection(LexwareOptions.SectionName));
        // Obergrenze; die Frist je Versuch setzt LexwareClient. Ohne die Standardprotokollierung des HttpClient, damit
        // weder Kopfzeilen noch Abfragen (Namen, E-Mail-Adressen) im Log erscheinen.
        services.AddHttpClient(LexwareClient.HttpClientName, client => client.Timeout = TimeSpan.FromMinutes(1)).RemoveAllLoggers();
        services.AddSingleton<LexwareClient>();
        services.AddSingleton<LexwareDirectory>();
        services.AddScoped<LexwareService>();
        services.AddScoped<LexwareChangeSyncService>();
        services.AddScoped<LexwareAutoCreateService>();
        services.AddScoped<LexwareArticleSyncService>();
        services.AddScoped<SalesVoucherService>();
        services.AddScoped<LexwareAutoAssignService>();
        services.AddScoped<LexwareBauakteStatusService>();
        services.AddSingleton<LexwareChangeSyncJob>();
        services.AddHostedService(provider => provider.GetRequiredService<LexwareChangeSyncJob>());
        services.AddScoped<ICustomerReferenceSource, LexwareCustomerReferences>();
        services.AddScoped<ICustomerSyncDefault, LexwareCustomerSyncDefault>();
        services.AddScoped<ICustomerCreationCheck, LexwareCustomerCreationCheck>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        LexwareEndpoints.Map(api);
        LexwareArticleEndpoints.Map(api);
        SalesEndpoints.Map(api);
        PurchaseEndpoints.Map(api);
    }
}
