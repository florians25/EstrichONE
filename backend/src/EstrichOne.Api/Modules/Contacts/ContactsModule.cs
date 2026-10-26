using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Modules.Sync.Exports;

namespace EstrichOne.Api.Modules.Contacts;

/// <summary>Kunden und Projektbeteiligte.</summary>
public sealed class ContactsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ContactImport>();
        services.AddScoped<ISyncEntitySource, ContactSource>();
        services.AddScoped<ISyncOperationHandler, ContactSync>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        ContactEndpoints.Map(api);
        ContactMailingEndpoints.Map(api);
    }
}
