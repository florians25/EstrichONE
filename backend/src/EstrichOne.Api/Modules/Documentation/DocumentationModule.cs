using System.Threading.Channels;
using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Sync.Exports;

namespace EstrichOne.Api.Modules.Documentation;

/// <summary>Fotos, Videos, Dokumente, Bautagebuch und Prüfungen (Erstbegehung, CM-Messung).</summary>
public sealed class DocumentationModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FileStorageSettings>().Bind(configuration.GetSection(FileStorageSettings.SectionName));
        services.AddSingleton<LocalFileStorage>();
        services.AddSingleton(Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true }));
        services.AddScoped<FileService>();
        services.AddScoped<ApprovalService>();
        services.AddScoped<Exports.IArchivableDocuments, ArchivableDocuments>();
        services.AddScoped<BauakteReportBuilder>();
        services.AddScoped<IBauakteHintSource, DocumentationHints>();
        services.AddHostedService<VariantWorker>();
        services.AddScoped<ISyncOperationHandler, FileSync>();
        services.AddScoped<ISyncEntitySource, FileSource>();
        services.AddScoped<ISyncOperationHandler, SiteRecordSync>();
        services.AddScoped<ISyncEntitySource, DiaryEntrySource>();
        services.AddScoped<ISyncEntitySource, InspectionSource>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        FileEndpoints.Map(api);
        SiteRecordEndpoints.Map(api);
        ApprovalEndpoints.Map(api);
        DocumentEndpoints.Map(api);
        BauakteReportEndpoints.Map(api);
    }
}
