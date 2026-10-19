using EstrichOne.Api.Infrastructure.Modules;

namespace EstrichOne.Api.Modules.Contacts;

/// <summary>Kunden und Projektbeteiligte.</summary>
public sealed class ContactsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ContactImport>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => ContactEndpoints.Map(api);
}
