using System.ComponentModel.DataAnnotations;
using System.Globalization;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace EstrichOne.Api.Modules.Documentation;

/// <summary>
/// Ankündigung einer Datei (Schritt 1). Der Client erzeugt die ID und berechnet SHA-256 vorab – auch offline.
/// Fotos lädt die iOS-App als JPEG hoch (HEIC-Varianten sind serverseitig nicht garantiert).
/// </summary>
public sealed record AnnounceFileRequest(
    [property: Required] Guid Id,
    Guid? BauakteId,
    FileKind Kind,
    [property: Required, MaxLength(255)] string OriginalName,
    [property: Required, MaxLength(100)] string ContentType,
    [property: Range(1, long.MaxValue)] long SizeBytes,
    [property: Required, StringLength(64, MinimumLength = 64)] string Sha256,
    DateTimeOffset? CapturedAt = null,
    [property: Range(-90, 90)] double? Latitude = null,
    [property: Range(-180, 180)] double? Longitude = null,
    [property: MaxLength(500)] string? Caption = null,
    [property: MaxLength(30)] string? Category = null);

/// <summary>Beschriftung und Dokumentart nachträglich ändern.</summary>
public sealed record UpdateFileRequest([property: MaxLength(500)] string? Caption, [property: MaxLength(30)] string? Category);

public sealed record FileResponse(
    Guid Id, Guid? BauakteId, FileKind Kind, FileState State, VariantState Variants, string OriginalName, string ContentType,
    long SizeBytes, string Sha256, string? Caption, DateTimeOffset? CapturedAt, double? Latitude, double? Longitude,
    Guid UploadedByUserId, DateTimeOffset CreatedAt, long Version, string? Category = null);

public sealed record UploadStatus(Guid Id, long ReceivedBytes, long SizeBytes, FileState State);

internal static class FileEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var files = api.MapGroup("/files").WithTags("Dateien");

        files.MapPost("/", AnnounceAsync).WithName("AnnounceFile")
            .WithSummary("Datei ankündigen (Metadaten + SHA-256); idempotent über die ID")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .RequirePermission(Permissions.FilesUpload);

        files.MapPut("/{id:guid}/content", UploadAsync).WithName("UploadFileContent")
            .WithSummary("Inhalt hochladen – ganz oder ab Position (Content-Range: bytes start-ende/gesamt)")
            .Accepts<Stream>("application/octet-stream")
            .Produces<UploadStatus>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithMetadata(new DisableRequestSizeLimitAttribute())
            .RequirePermission(Permissions.FilesUpload);

        files.MapGet("/{id:guid}/upload-status", GetUploadStatusAsync).WithName("GetUploadStatus")
            .WithSummary("Bereits empfangene Bytes (zum Fortsetzen nach Abbruch)")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(Permissions.FilesUpload);

        // Lesen: Die Prüfung je Datei (FindReadableAsync) entscheidet – Bauakte, eigene Datei oder Ticketanhang.
        files.MapGet("/{id:guid}", GetAsync).WithName("GetFile")
            .ProducesProblem(StatusCodes.Status404NotFound);

        files.MapGet("/{id:guid}/content", DownloadAsync).WithName("DownloadFile")
            .WithSummary("Inhalt (variant = original | display | thumb); unterstützt Range für Videos")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);

        files.MapPatch("/{id:guid}", UpdateAsync).WithName("UpdateFile")
            .WithSummary("Beschriftung und Dokumentart ändern")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.FilesUpload);

        files.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteFile")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(Permissions.FilesDelete);

        api.MapGet("/bauakten/{bauakteId:guid}/files", ListForBauakteAsync).WithName("ListBauakteFiles")
            .WithTags("Dateien")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(Permissions.BauaktenRead);
    }

    private static async Task<Results<Created<FileResponse>, Ok<FileResponse>, ProblemHttpResult>> AnnounceAsync(
        AnnounceFileRequest request, FileService service, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        if (request.Category is { } category && !DocumentCategories.All.Contains(category))
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "Unbekannte Dokumentart.");
        }

        var (file, created, failure) = await service.AnnounceAsync(request.Id, request.BauakteId, request.Kind, request.OriginalName,
            request.ContentType, request.SizeBytes, request.Sha256, request.CapturedAt, request.Latitude, request.Longitude,
            request.Caption, cancellationToken);
        if (failure is not null)
        {
            return ProblemFor(failure.Value);
        }

        if (created && request.Category is not null)
        {
            file!.Categorize(request.Category);
            await db.SaveChangesAsync(cancellationToken);
        }

        return created ? TypedResults.Created($"/api/v1/files/{file!.Id}", ToResponse(file)) : TypedResults.Ok(ToResponse(file!));
    }

    private static async Task<Results<Ok<FileResponse>, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdateFileRequest request, FileService service, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        if (await service.FindReadableAsync(id, cancellationToken) is null)
        {
            return ProblemFor(FileFailure.NotFound);
        }

        if (request.Category is { } category && !DocumentCategories.All.Contains(category))
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "Unbekannte Dokumentart.");
        }

        var file = await db.Set<FileRecord>().SingleAsync(f => f.Id == id, cancellationToken);
        file.Describe(string.IsNullOrWhiteSpace(request.Caption) ? null : request.Caption.Trim());
        file.Categorize(file.Kind == FileKind.Document ? request.Category : null);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToResponse(file));
    }

    private static async Task<Results<Ok<FileResponse>, Accepted<UploadStatus>, ProblemHttpResult>> UploadAsync(
        Guid id, HttpRequest request, FileService service, CancellationToken cancellationToken)
    {
        long? offset = null;
        if (request.Headers.ContentRange.Count > 0)
        {
            if (!ContentRangeHeaderValue.TryParse(request.Headers.ContentRange.ToString(), out var range) || range.From is null)
            {
                return Problem(StatusCodes.Status400BadRequest, "INVALID_CONTENT_RANGE", "Content-Range ist ungültig.");
            }

            offset = range.From;
        }

        var (file, received, failure) = await service.UploadAsync(id, request.Body, offset, cancellationToken);
        if (failure == FileFailure.OffsetMismatch)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Falsche Position",
                detail: "Der Upload muss an der gemeldeten Position fortgesetzt werden.",
                extensions: new Dictionary<string, object?> { ["code"] = "OFFSET_MISMATCH", ["receivedBytes"] = received });
        }

        if (failure is not null)
        {
            return ProblemFor(failure.Value);
        }

        return file!.State == FileState.Stored
            ? TypedResults.Ok(ToResponse(file))
            : TypedResults.Accepted($"/api/v1/files/{id}/upload-status", new UploadStatus(id, received, file.SizeBytes, file.State));
    }

    private static async Task<Results<Ok<UploadStatus>, ProblemHttpResult>> GetUploadStatusAsync(
        Guid id, FileService service, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var received = await service.ReceivedBytesAsync(id, cancellationToken);
        if (received is null)
        {
            return ProblemFor(FileFailure.NotFound);
        }

        var file = await db.Set<FileRecord>().AsNoTracking().SingleAsync(f => f.Id == id, cancellationToken);
        return TypedResults.Ok(new UploadStatus(id, received.Value, file.SizeBytes, file.State));
    }

    private static async Task<Results<Ok<FileResponse>, ProblemHttpResult>> GetAsync(Guid id, FileService service, CancellationToken cancellationToken) =>
        await service.FindReadableAsync(id, cancellationToken) is { } file ? TypedResults.Ok(ToResponse(file)) : ProblemFor(FileFailure.NotFound);

    private static async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> DownloadAsync(
        Guid id, FileService service, LocalFileStorage storage, CancellationToken cancellationToken,
        [FromQuery] string variant = "original")
    {
        var file = await service.FindReadableAsync(id, cancellationToken);
        if (file is not { State: FileState.Stored, StorageKey: not null })
        {
            return ProblemFor(FileFailure.NotFound);
        }

        if (variant is "thumb" or "display")
        {
            switch (file.Variants)
            {
                case VariantState.Ready:
                    return TypedResults.PhysicalFile(storage.VariantPath(id, variant), "image/webp", enableRangeProcessing: true);
                case VariantState.Pending:
                    return Problem(StatusCodes.Status404NotFound, "VARIANT_PENDING", "Die Vorschau wird gerade erzeugt.");
                case VariantState.NotApplicable:
                    return Problem(StatusCodes.Status404NotFound, "VARIANT_NOT_AVAILABLE", "Für diesen Dateityp gibt es keine Vorschau.");
                case VariantState.Failed:
                    break; // Original ausliefern
            }
        }
        else if (variant != "original")
        {
            return Problem(StatusCodes.Status400BadRequest, "UNKNOWN_VARIANT", "Erlaubt: original, display, thumb.");
        }

        var contentType = file.DetectedContentType switch
        {
            "application/zip" => "application/octet-stream",
            { } detected => detected,
            null => "application/octet-stream",
        };
        var inline = contentType.StartsWith("image/", StringComparison.Ordinal) || contentType.StartsWith("video/", StringComparison.Ordinal)
            || contentType == "application/pdf";
        return TypedResults.PhysicalFile(storage.PathOf(file.StorageKey), contentType,
            fileDownloadName: inline ? null : file.OriginalName, enableRangeProcessing: true);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id, FileService service, EstrichOneDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (await service.FindReadableAsync(id, cancellationToken) is null)
        {
            return ProblemFor(FileFailure.NotFound);
        }

        var file = await db.Set<FileRecord>().SingleAsync(f => f.Id == id, cancellationToken);
        file.Delete(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<List<FileResponse>>, ProblemHttpResult>> ListForBauakteAsync(
        Guid bauakteId, EstrichOneDbContext db, IBauakteAccess access, CancellationToken cancellationToken, [FromQuery] FileKind? kind = null)
    {
        if (!await access.CanReadAsync(bauakteId, cancellationToken))
        {
            return Problem(StatusCodes.Status404NotFound, "BAUAKTE_NOT_FOUND", "Bauakte nicht gefunden");
        }

        var query = db.Set<FileRecord>().AsNoTracking().Where(f => f.BauakteId == bauakteId);
        if (kind is not null)
        {
            query = query.Where(f => f.Kind == kind);
        }

        var files = await query.OrderByDescending(f => f.CapturedAt ?? f.CreatedAt).ToListAsync(cancellationToken);
        return TypedResults.Ok(files.Select(ToResponse).ToList());
    }

    internal static FileResponse ToResponse(FileRecord f) => new(f.Id, f.BauakteId, f.Kind, f.State, f.Variants, f.OriginalName,
        f.ContentType, f.SizeBytes, f.Sha256, f.Caption, f.CapturedAt, f.Latitude, f.Longitude, f.UploadedByUserId, f.CreatedAt, f.Version,
        f.Category);

    private static ProblemHttpResult ProblemFor(FileFailure failure) => failure switch
    {
        FileFailure.NotFound => Problem(StatusCodes.Status404NotFound, "FILE_NOT_FOUND", "Datei nicht gefunden"),
        FileFailure.BauakteNotFound => Problem(StatusCodes.Status404NotFound, "BAUAKTE_NOT_FOUND", "Bauakte nicht gefunden"),
        FileFailure.IdConflict => Problem(StatusCodes.Status409Conflict, "ID_CONFLICT", "Unter dieser ID existiert bereits eine andere Datei."),
        FileFailure.TooLarge => Problem(StatusCodes.Status413PayloadTooLarge, "FILE_TOO_LARGE", "Die Datei ist zu groß."),
        FileFailure.ChecksumMismatch => Problem(StatusCodes.Status422UnprocessableEntity, "CHECKSUM_MISMATCH",
            "Der Inhalt passt nicht zur angekündigten Prüfsumme. Bitte erneut hochladen."),
        FileFailure.TypeNotAllowed => Problem(StatusCodes.Status422UnprocessableEntity, "FILE_TYPE_NOT_ALLOWED",
            "Dieser Dateityp ist nicht erlaubt."),
        _ => Problem(StatusCodes.Status400BadRequest, "BAD_REQUEST", "Ungültige Anfrage"),
    };

    private static ProblemHttpResult Problem(int status, string code, string title) =>
        TypedResults.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}
