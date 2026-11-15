using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Http;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.AppUpdates;

/// <summary>
/// Zuletzt gesehene Versionshinweise eines Benutzers, getrennt für Web und Apps (geräteübergreifend). Ohne Zeile gilt
/// der Benutzer als neu: Beim ersten Abruf wird still die Serverversion eingetragen.
/// </summary>
public sealed class ReleaseNoteReceipt
{
    public const int MaxVersionLength = 50;

    public Guid UserId { get; private set; }

    /// <summary><c>web</c> oder <c>apps</c>.</summary>
    public string Channel { get; private set; } = string.Empty;

    public string Version { get; private set; } = string.Empty;
    public DateTimeOffset SeenAt { get; private set; }

    public static ReleaseNoteReceipt Create(Guid userId, string channel, string version, DateTimeOffset now) =>
        new() { UserId = userId, Channel = channel, Version = version, SeenAt = now };

    public void Seen(string version, DateTimeOffset now)
    {
        Version = version;
        SeenAt = now;
    }
}

internal sealed class ReleaseNoteReceiptConfiguration : IEntityTypeConfiguration<ReleaseNoteReceipt>
{
    public void Configure(EntityTypeBuilder<ReleaseNoteReceipt> builder)
    {
        builder.ToTable("release_note_receipts");
        builder.HasKey(r => new { r.UserId, r.Channel });
        builder.Property(r => r.Channel).HasMaxLength(10);
        builder.Property(r => r.Version).HasMaxLength(ReleaseNoteReceipt.MaxVersionLength);
    }
}

/// <summary>„Was gibt's Neues“ nach der Anmeldung.</summary>
/// <param name="ServerVersion">Version dieses Servers.</param>
/// <param name="LastSeenVersion">Zuletzt gesehene Version (vor diesem Abruf); <c>null</c> bei neuen Benutzern.</param>
/// <param name="Notes">Neuerungen seit der zuletzt gesehenen Version, neueste zuerst (leer: nichts anzeigen).</param>
public sealed record WhatsNewResponse(string ServerVersion, string? LastSeenVersion, IReadOnlyList<AppReleaseNote> Notes);

/// <param name="Audience"><c>web</c> (Standard) oder <c>apps</c>.</param>
/// <param name="Version">Gesehene Version; ohne Angabe die Serverversion.</param>
public sealed record MarkReleaseNotesSeenRequest(string? Audience, string? Version);

internal static class WhatsNewEndpoints
{
    /// <summary>Höchstens so viele Versionen nach einem großen Sprung (die Oberflächen klappen ältere ein).</summary>
    public const int MaxVersions = 20;

    public static void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/release-notes").WithTags("Was gibt's Neues");

        group.MapGet("/", ListAsync).WithName("ListReleaseNotes")
            .WithSummary("Neuerungen dieses Servers (Web: „## Neu“, Apps: „## Apps“), neueste zuerst; since = nach dieser Version")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/whats-new", WhatsNewAsync).WithName("GetWhatsNew")
            .WithSummary("Nach der Anmeldung: Neuerungen seit der zuletzt gesehenen Version (neue Benutzer: still als gesehen markiert)")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPut("/seen", SeenAsync).WithName("MarkReleaseNotesSeen")
            .WithSummary("„Verstanden“: Neuerungen bis zu dieser Version (Standard: Serverversion) als gesehen markieren")
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static Results<Ok<IReadOnlyList<AppReleaseNote>>, ProblemHttpResult> ListAsync(
        ReleaseNotesCatalog notes, AppUpdateService service, string? since, string? audience, string? platform, int? limit)
    {
        if (ParseAudience(audience) is not { } kind || !ValidPlatform(platform) || (since is not null && !VersionComparer.IsValid(since)))
        {
            return Invalid();
        }

        return TypedResults.Ok(notes.Recent(since is null ? null : VersionComparer.Normalize(since), service.ServerVersion, kind, platform,
            limit ?? (since is null ? 5 : MaxVersions)));
    }

    private static async Task<Results<Ok<WhatsNewResponse>, ProblemHttpResult>> WhatsNewAsync(ReleaseNotesCatalog notes,
        AppUpdateService service, EstrichOneDbContext db, ICurrentUser user, TimeProvider clock, string? audience, string? platform,
        CancellationToken cancellationToken)
    {
        if (ParseAudience(audience) is not { } kind || !ValidPlatform(platform))
        {
            return Invalid();
        }

        var server = service.ServerVersion;
        var channel = Channel(kind);
        var receipt = await db.Set<ReleaseNoteReceipt>().SingleOrDefaultAsync(r => r.UserId == user.UserId && r.Channel == channel, cancellationToken);
        if (receipt is null)
        {
            // Neuer Benutzer bzw. Neuinstallation: nicht mit alten Neuerungen begrüßen.
            db.Add(ReleaseNoteReceipt.Create(user.UserId, channel, server, clock.GetUtcNow()));
            await SaveIgnoringRaceAsync(db, cancellationToken);
            return TypedResults.Ok(new WhatsNewResponse(server, null, []));
        }

        var last = receipt.Version;
        if (!VersionComparer.IsValid(last) || VersionComparer.Compare(last, server) >= 0)
        {
            return TypedResults.Ok(new WhatsNewResponse(server, last, []));
        }

        var list = notes.Recent(VersionComparer.Normalize(last), server, kind, platform, MaxVersions);
        if (list.Count == 0)
        {
            // Nichts für diese Oberfläche (z. B. kein „## Apps“): ohne Anzeige weiterzählen.
            receipt.Seen(server, clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(new WhatsNewResponse(server, last, list));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> SeenAsync(MarkReleaseNotesSeenRequest request, AppUpdateService service,
        EstrichOneDbContext db, ICurrentUser user, TimeProvider clock, CancellationToken cancellationToken)
    {
        var server = service.ServerVersion;
        if (ParseAudience(request.Audience) is not { } kind
            || (request.Version is { } v && (!VersionComparer.IsValid(v) || v.Length > ReleaseNoteReceipt.MaxVersionLength
                || VersionComparer.Compare(v, server) > 0)))
        {
            return Invalid();
        }

        var version = request.Version is null ? server : VersionComparer.Normalize(request.Version);
        var channel = Channel(kind);
        var receipt = await db.Set<ReleaseNoteReceipt>().SingleOrDefaultAsync(r => r.UserId == user.UserId && r.Channel == channel, cancellationToken);
        if (receipt is null)
        {
            db.Add(ReleaseNoteReceipt.Create(user.UserId, channel, version, clock.GetUtcNow()));
        }
        else
        {
            receipt.Seen(version, clock.GetUtcNow());
        }

        await SaveIgnoringRaceAsync(db, cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Zwei gleichzeitige Erstabrufe (zwei Tabs): der zweite Eintrag scheitert am Schlüssel – unschädlich.</summary>
    private static async Task SaveIgnoringRaceAsync(EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
        }
    }

    private static ReleaseNoteAudience? ParseAudience(string? audience) => audience?.ToLowerInvariant() switch
    {
        null or "" or "web" => ReleaseNoteAudience.Web,
        "apps" or "app" => ReleaseNoteAudience.Apps,
        _ => null,
    };

    private static bool ValidPlatform(string? platform) => platform is null || ClientIdentity.Platforms.Contains(platform);

    private static string Channel(ReleaseNoteAudience audience) => audience == ReleaseNoteAudience.Apps ? "apps" : "web";

    private static ProblemHttpResult Invalid() => TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest,
        title: "Ungültige Angabe: audience (web, apps), platform (android, ios) bzw. Version (z. B. 1.0.5, höchstens die Serverversion).",
        extensions: new Dictionary<string, object?> { ["code"] = "RELEASE_NOTES_INVALID" });
}
