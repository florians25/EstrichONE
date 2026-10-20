using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Identity;

/// <summary>Nur Name und Status – genug für „Zugewiesen an“ und Verfasserangaben, ohne Kontaktdaten.</summary>
public sealed record DirectoryUser(Guid Id, string DisplayName, bool IsActive);

/// <summary>Mitarbeiterverzeichnis für alle angemeldeten Benutzer (Auswahl von Zuständigen, Anzeige von Namen).</summary>
internal static class DirectoryEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/directory/users", ListAsync).WithName("ListDirectoryUsers").WithTags("Verzeichnis")
            .WithSummary("Alle Benutzer mit Anzeigenamen (aktive zuerst, alphabetisch)");
    }

    private static async Task<List<DirectoryUser>> ListAsync(EstrichOneDbContext db, CancellationToken cancellationToken) =>
        (await db.Set<User>().AsNoTracking()
            .Select(u => new DirectoryUser(u.Id, u.DisplayName, u.IsActive))
            .ToListAsync(cancellationToken))
        .OrderByDescending(u => u.IsActive)
        .ThenBy(u => u.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        .ToList();
}
