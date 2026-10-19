using System.Threading.Channels;
using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Modules.Sync.Exports;

namespace EstrichOne.Api.Modules.Documentation;

/// <summary>Fotos, Videos, Dokumente; später Bautagebuch und Prüfungen.</summary>
public sealed class DocumentationModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FileStorageSettings>().Bind(configuration.GetSection(FileStorageSettings.SectionName));
        services.AddSingleton<LocalFileStorage>();
        services.AddSingleton(Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true }));
        services.AddScoped<FileService>();
        services.AddHostedService<VariantWorker>();
        services.AddScoped<ISyncOperationHandler, FileSync>();
        services.AddScoped<ISyncEntitySource, FileSource>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => FileEndpoints.Map(api);
}
