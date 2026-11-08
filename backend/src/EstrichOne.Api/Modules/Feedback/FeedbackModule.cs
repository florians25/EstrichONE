using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Modules.Documentation.Exports;

namespace EstrichOne.Api.Modules.Feedback;

/// <summary>Fehler melden und Funktionen wünschen (geführtes Formular im Web); Verwaltung durch die IT.</summary>
public sealed class FeedbackModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<FeedbackService>();
        services.AddScoped<FeedbackNotifier>();
        services.AddScoped<IFileReadGrant, FeedbackAttachmentGrant>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => FeedbackEndpoints.Map(api);
}
