using EstrichOne.Api.Infrastructure.Pdf;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetVips;

namespace EstrichOne.Api.Modules.Organization;

/// <summary>Logo des eigenen Betriebs (als PNG normalisiert), erscheint im Web und auf allen PDFs.</summary>
public sealed class CompanyLogo
{
    public const int SingletonId = 1;
    public const int MaxUploadBytes = 5 * 1024 * 1024;

    public int Id { get; private set; } = SingletonId;
    public byte[] Png { get; private set; } = [];
    public int Width { get; private set; }
    public int Height { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Replace(byte[] png, int width, int height, DateTimeOffset at)
    {
        Png = png;
        Width = width;
        Height = height;
        UpdatedAt = at;
    }
}

internal sealed class CompanyLogoConfiguration : IEntityTypeConfiguration<CompanyLogo>
{
    public void Configure(EntityTypeBuilder<CompanyLogo> builder)
    {
        builder.ToTable("company_logo");
        builder.Property(l => l.Id).ValueGeneratedNever();
    }
}

/// <summary>Firmenkopf für PDFs: Firmendaten, eigenes Logo und Gestaltung aus den Einstellungen.</summary>
internal sealed class PdfBranding(EstrichOneDbContext db, ICompanyProfiles companies, ISystemSettings settings) : IPdfBranding
{
    public Task<bool> HasLogoAsync(CancellationToken cancellationToken) => db.Set<CompanyLogo>().AnyAsync(cancellationToken);

    public async Task<PdfCompany> GetAsync(CancellationToken cancellationToken)
    {
        var profile = await companies.GetProfileAsync(cancellationToken);
        var pdf = (await settings.GetAsync(cancellationToken)).Pdf;
        var logo = pdf.ShowCompanyLogo
            ? await db.Set<CompanyLogo>().AsNoTracking().Select(l => l.Png).SingleOrDefaultAsync(cancellationToken)
            : null;
        return new PdfCompany(profile?.Name ?? "EstrichONE", profile?.Street, profile?.PostalCode, profile?.City, profile?.Phone, profile?.Email, profile?.Website)
        {
            Logo = logo is { Length: > 0 } ? logo : null,
            ShowEstrichOneLogo = pdf.ShowEstrichOneLogo,
            AccentColor = pdf.AccentColor,
            FooterText = pdf.FooterText,
        };
    }
}

public sealed record CompanyLogoInfo(bool HasLogo, int Width, int Height, DateTimeOffset? UpdatedAt);

internal static class CompanyLogoEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var logo = api.MapGroup("/company/logo").WithTags("Firmendaten");
        logo.MapGet("/", GetAsync).WithName("GetCompanyLogo").WithSummary("Firmenlogo als PNG (404 ohne Logo); öffentlich (Anmeldeseite, Kundenfreigabe)")
            .AllowAnonymous()
            .Produces(StatusCodes.Status200OK, contentType: "image/png").ProducesProblem(StatusCodes.Status404NotFound);
        logo.MapGet("/info", InfoAsync).WithName("GetCompanyLogoInfo");
        logo.MapPut("/", UploadAsync).WithName("UploadCompanyLogo")
            .WithSummary("Firmenlogo hochladen (PNG, JPEG, WebP; wird auf höchstens 1200 × 400 Pixel als PNG gespeichert)")
            .Accepts<Stream>("image/png", "image/jpeg", "image/webp")
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.SettingsManage);
        logo.MapDelete("/", DeleteAsync).WithName("DeleteCompanyLogo").RequirePermission(Permissions.SettingsManage);
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> GetAsync(EstrichOneDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var logo = await db.Set<CompanyLogo>().AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (logo is null || logo.Png.Length == 0)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Kein Logo",
                detail: "Es ist noch kein Firmenlogo hinterlegt.", extensions: new Dictionary<string, object?> { ["code"] = "NO_LOGO" });
        }

        http.Response.Headers.CacheControl = "private, max-age=300";
        return TypedResults.File(logo.Png, "image/png", lastModified: logo.UpdatedAt);
    }

    private static async Task<Ok<CompanyLogoInfo>> InfoAsync(EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var logo = await db.Set<CompanyLogo>().AsNoTracking().Select(l => new { l.Width, l.Height, l.UpdatedAt, Size = l.Png.Length }).SingleOrDefaultAsync(cancellationToken);
        return TypedResults.Ok(logo is null || logo.Size == 0 ? new CompanyLogoInfo(false, 0, 0, null) : new CompanyLogoInfo(true, logo.Width, logo.Height, logo.UpdatedAt));
    }

    private static async Task<Results<Ok<CompanyLogoInfo>, ProblemHttpResult>> UploadAsync(
        HttpRequest request, EstrichOneDbContext db, IAuditLog audit, ICurrentUser currentUser, TimeProvider clock, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var read = new byte[81920];
        int count;
        while ((count = await request.Body.ReadAsync(read, cancellationToken)) > 0)
        {
            buffer.Write(read, 0, count);
            if (buffer.Length > CompanyLogo.MaxUploadBytes)
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Zu groß",
                    detail: "Das Logo darf höchstens 5 MB groß sein.", extensions: new Dictionary<string, object?> { ["code"] = "LOGO_TOO_LARGE" });
            }
        }

        byte[] png;
        int width, height;
        try
        {
            using var image = Image.ThumbnailBuffer(buffer.ToArray(), 1200, height: 400, size: Enums.Size.Down);
            png = image.PngsaveBuffer(compression: 9, keep: Enums.ForeignKeep.None);
            width = image.Width;
            height = image.Height;
        }
        catch (VipsException)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Kein Bild",
                detail: "Bitte ein Logo als PNG, JPEG oder WebP hochladen.", extensions: new Dictionary<string, object?> { ["code"] = "INVALID_IMAGE" });
        }

        var logo = await db.Set<CompanyLogo>().SingleOrDefaultAsync(cancellationToken);
        if (logo is null)
        {
            logo = new CompanyLogo();
            db.Add(logo);
        }

        logo.Replace(png, width, height, clock.GetUtcNow());
        audit.Record("company.logo_changed", currentUser.UserId, "company", "logo");
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new CompanyLogoInfo(true, width, height, logo.UpdatedAt));
    }

    private static async Task<NoContent> DeleteAsync(EstrichOneDbContext db, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        await db.Set<CompanyLogo>().ExecuteDeleteAsync(cancellationToken);
        audit.Record("company.logo_removed", currentUser.UserId, "company", "logo");
        return TypedResults.NoContent();
    }
}
