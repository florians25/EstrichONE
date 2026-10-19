using System.Threading.Channels;
using EstrichOne.Api.Infrastructure.Modules;

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
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => FileEndpoints.Map(api);
}
