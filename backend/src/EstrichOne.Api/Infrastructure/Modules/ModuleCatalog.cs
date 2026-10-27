using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Archive;
using EstrichOne.Api.Modules.Audit;
using EstrichOne.Api.Modules.Backup;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Calls;
using EstrichOne.Api.Modules.Chat;
using EstrichOne.Api.Modules.Contacts;
using EstrichOne.Api.Modules.Devices;
using EstrichOne.Api.Modules.Documentation;
using EstrichOne.Api.Modules.Helpdesk;
using EstrichOne.Api.Modules.Identity;
using EstrichOne.Api.Modules.Mail;
using EstrichOne.Api.Modules.Materials;
using EstrichOne.Api.Modules.Notices;
using EstrichOne.Api.Modules.Notifications;
using EstrichOne.Api.Modules.Organization;
using EstrichOne.Api.Modules.Planning;
using EstrichOne.Api.Modules.Platform;
using EstrichOne.Api.Modules.Setup;
using EstrichOne.Api.Modules.Sync;
using EstrichOne.Api.Modules.Work;

namespace EstrichOne.Api.Infrastructure.Modules;

public static class ModuleCatalog
{
    public static IReadOnlyList<IModule> All { get; } =
    [
        new PlatformModule(),
        new AuditModule(),
        new IdentityModule(),
        new AccessModule(),
        new DevicesModule(),
        new OrganizationModule(),
        new ContactsModule(),
        new BauaktenModule(),
        new DocumentationModule(),
        new WorkModule(),
        new HelpdeskModule(),
        new PlanningModule(),
        new CallsModule(),
        new MaterialsModule(),
        new SetupModule(),
        new SyncModule(),
        new NoticesModule(),
        new NotificationsModule(),
        new ChatModule(),
        new BackupModule(),
        new MailModule(),
        new ArchiveModule(),
    ];

    public static IServiceCollection AddModules(this IServiceCollection services, IConfiguration configuration)
    {
        foreach (var module in All)
        {
            module.AddServices(services, configuration);
        }

        return services;
    }

    public static IEndpointRouteBuilder MapModules(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1");
        foreach (var module in All)
        {
            module.MapEndpoints(api);
        }

        return app;
    }
}
