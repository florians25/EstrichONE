using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Chat;

/// <summary>Mini-Chat Büro ↔ Mitarbeiter (nur Text, verschlüsselt gespeichert, Löschfrist). Standardmäßig aus.</summary>
public sealed class ChatModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ChatService>();
        services.AddScoped<IDataInitializer, ChatSetup>();
        services.AddHostedService<ChatRetention>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => ChatEndpoints.Map(api);
}

/// <summary>
/// Einmalig: Einstellungszeile anlegen (Modul aus) und den Standardrollen die Chat-Rechte geben – auch in bestehenden
/// Installationen („Büro und Bauleitung“: nutzen und verwalten, „Baustelle“: nutzen). Danach nie wieder verändert.
/// </summary>
internal sealed class ChatSetup(EstrichOneDbContext db, IRoleAssignments roles) : IDataInitializer
{
    private static readonly Guid OfficeRoleId = Guid.Parse("01a00000-0000-7000-8000-000000000002");
    private static readonly Guid SiteRoleId = Guid.Parse("01a00000-0000-7000-8000-000000000003");

    public int Order => 60;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var row = await db.Set<ChatSettingsRow>().SingleOrDefaultAsync(cancellationToken);
        if (row is { Seeded: true })
        {
            return;
        }

        await roles.GrantAsync(OfficeRoleId, Permissions.ChatUse, cancellationToken);
        await roles.GrantAsync(OfficeRoleId, Permissions.ChatAdmin, cancellationToken);
        await roles.GrantAsync(SiteRoleId, Permissions.ChatUse, cancellationToken);
        if (row is null)
        {
            row = new ChatSettingsRow();
            db.Add(row);
        }

        row.Seeded = true;
        await db.SaveChangesAsync(cancellationToken);
    }
}
