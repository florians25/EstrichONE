using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Organization;

/// <summary>Einstellungen der Installation als ein JSON-Dokument (eine Zeile, ADR 0004).</summary>
public sealed class SystemSettingsRow : IVersionedEntity
{
    public const int SingletonId = 1;

    public int Id { get; private set; } = SingletonId;
    public string Data { get; private set; } = "{}";

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public void Update(string data) => Data = data;
}

internal sealed class SystemSettingsRowConfiguration : IEntityTypeConfiguration<SystemSettingsRow>
{
    public void Configure(EntityTypeBuilder<SystemSettingsRow> builder)
    {
        builder.ToTable("system_settings");
        builder.ConfigureVersioned();
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Data).HasColumnType("jsonb");
    }
}

internal sealed class SystemSettingsStore(EstrichOneDbContext db) : ISystemSettings
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public async Task<SystemSettings> GetAsync(CancellationToken cancellationToken)
    {
        var row = await db.Set<SystemSettingsRow>().AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return Parse(row?.Data);
    }

    internal static SystemSettings Parse(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return new SystemSettings().Normalize();
        }

        try
        {
            return (JsonSerializer.Deserialize<SystemSettings>(data, Json) ?? new SystemSettings()).Normalize();
        }
        catch (JsonException)
        {
            return new SystemSettings().Normalize();
        }
    }
}

public sealed record SystemSettingsResponse(SystemSettings Settings, long Version);

public sealed record SaveSystemSettingsRequest([property: Required] SystemSettings Settings, long? Version = null);

internal static class SystemSettingsEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var settings = api.MapGroup("/settings").WithTags("Einstellungen");
        settings.MapGet("/", GetAsync).WithName("GetSystemSettings")
            .WithSummary("Einstellungen der Installation (für alle Angemeldeten lesbar: Karte, Darstellung, Vorgaben)");
        settings.MapPut("/", SaveAsync).WithName("SaveSystemSettings")
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .RequirePermission(Permissions.SettingsManage);
    }

    private static async Task<Ok<SystemSettingsResponse>> GetAsync(EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var row = await db.Set<SystemSettingsRow>().AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return TypedResults.Ok(new SystemSettingsResponse(SystemSettingsStore.Parse(row?.Data), row?.Version ?? 0));
    }

    private static async Task<Results<Ok<SystemSettingsResponse>, ProblemHttpResult>> SaveAsync(
        SaveSystemSettingsRequest request, EstrichOneDbContext db, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var row = await db.Set<SystemSettingsRow>().SingleOrDefaultAsync(cancellationToken);
        if (row is not null && request.Version is { } version && version != row.Version)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Geändert",
                detail: "Die Einstellungen wurden inzwischen geändert. Bitte neu laden.",
                extensions: new Dictionary<string, object?> { ["code"] = "VERSION_CONFLICT" });
        }

        if (row is null)
        {
            row = new SystemSettingsRow();
            db.Add(row);
        }

        var normalized = request.Settings.Normalize();
        row.Update(JsonSerializer.Serialize(normalized, SystemSettingsStore.Json));
        audit.Record("settings.saved", currentUser.UserId, "settings", "system");
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new SystemSettingsResponse(normalized, row.Version));
    }
}
