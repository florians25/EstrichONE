using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace EstrichOne.Client;

/// <summary>
/// Alle weiteren Fachaufrufe der API v1: Dateien (Hoch-/Herunterladen), Bautagebuch und Prüfungen (über den Sync),
/// Räume, Stammdaten, Planung, Material, Helpdesk und Verwaltung – damit die Windows-App ohne Umweg über das Web auskommt.
/// </summary>
public sealed partial class EstrichOneClient
{
    // Bauakte

    /// <summary>Bauakte in den Papierkorb legen; bestätigt mit dem eigenen Passwort.</summary>
    public Task DeleteBauakteAsync(Guid id, string password, CancellationToken cancellationToken = default) =>
        SendAsync<object>(HttpMethod.Post, $"bauakten/{id}/delete", new { password }, cancellationToken: cancellationToken);

    /// <summary>Notiz anheften/lösen oder einfärben (yellow, red, green, blue).</summary>
    public Task<Note> UpdateNoteAsync(Guid bauakteId, Guid noteId, bool? isPinned = null, string? color = null, CancellationToken cancellationToken = default) =>
        SendAsync<Note>(HttpMethod.Patch, $"bauakten/{bauakteId}/notes/{noteId}", new { isPinned, color }, cancellationToken: cancellationToken);

    public Task<Note> AddNoteAsync(Guid bauakteId, string text, bool pinned, CancellationToken cancellationToken = default) =>
        AddNoteWithPinAsync(bauakteId, text, pinned, cancellationToken);

    private async Task<Note> AddNoteWithPinAsync(Guid bauakteId, string text, bool pinned, CancellationToken cancellationToken)
    {
        var note = await AddNoteAsync(bauakteId, text, cancellationToken);
        return pinned ? await UpdateNoteAsync(bauakteId, note.Id, isPinned: true, cancellationToken: cancellationToken) : note;
    }

    public Task<List<ScreedType>> GetScreedTypesAsync(CancellationToken cancellationToken = default) =>
        GetAsync<List<ScreedType>>("screed-types", cancellationToken: cancellationToken);

    public Task<RoomOverview> SaveSectionAsync(Guid bauakteId, Guid sectionId, string name, int? sortOrder = null, CancellationToken cancellationToken = default) =>
        SendAsync<RoomOverview>(HttpMethod.Put, $"bauakten/{bauakteId}/sections/{sectionId}", new { id = sectionId, name, sortOrder },
            cancellationToken: cancellationToken);

    public Task<RoomOverview> DeleteSectionAsync(Guid bauakteId, Guid sectionId, CancellationToken cancellationToken = default) =>
        SendAsync<RoomOverview>(HttpMethod.Delete, $"bauakten/{bauakteId}/sections/{sectionId}", null, cancellationToken: cancellationToken);

    public Task<RoomOverview> SaveRoomAsync(Guid bauakteId, SaveRoomRequest room, CancellationToken cancellationToken = default) =>
        SendAsync<RoomOverview>(HttpMethod.Put, $"bauakten/{bauakteId}/rooms/{room.Id}", room, cancellationToken: cancellationToken);

    public Task<RoomOverview> DeleteRoomAsync(Guid bauakteId, Guid roomId, CancellationToken cancellationToken = default) =>
        SendAsync<RoomOverview>(HttpMethod.Delete, $"bauakten/{bauakteId}/rooms/{roomId}", null, cancellationToken: cancellationToken);

    /// <summary>Aufgabe/Mangel ändern; <paramref name="clearDueDate"/> bzw. <paramref name="unassign"/> entfernen Termin bzw. Zuständigen.</summary>
    public Task<WorkItem> UpdateWorkItemAsync(Guid id, string? title = null, string? description = null, DateOnly? dueDate = null, bool clearDueDate = false,
        Guid? assigneeUserId = null, bool unassign = false, WorkItemStatus? status = null, CancellationToken cancellationToken = default) =>
        SendAsync<WorkItem>(HttpMethod.Patch, $"work-items/{id}", new { title, description, dueDate, clearDueDate, assigneeUserId, unassign, status },
            cancellationToken: cancellationToken);

    // Dateien

    public Task<List<FileItem>> GetFilesAsync(Guid bauakteId, FileKind? kind, CancellationToken cancellationToken = default) =>
        GetAsync<List<FileItem>>($"bauakten/{bauakteId}/files", new Dictionary<string, string?> { ["kind"] = kind?.ToString() },
            cancellationToken);

    public Task<FileItem> GetFileAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetAsync<FileItem>($"files/{id}", cancellationToken: cancellationToken);

    public Task<FileItem> AnnounceFileAsync(AnnounceFileRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<FileItem>(HttpMethod.Post, "files", request, cancellationToken: cancellationToken);

    /// <summary>Beschriftung (z. B. umbenennen) und Dokumentart setzen; beide Werte werden ersetzt.</summary>
    public Task<FileItem> UpdateFileAsync(Guid id, string? caption, string? category, CancellationToken cancellationToken = default) =>
        SendAsync<FileItem>(HttpMethod.Patch, $"files/{id}", new { caption, category }, cancellationToken: cancellationToken);

    /// <summary>Name, Beschriftung, Ablage (Dokumentart, Unterordner) und Bezug zu Raum oder Mangel ändern.</summary>
    public Task<FileItem> UpdateFileDetailsAsync(Guid id, UpdateFileDetailsRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<FileItem>(HttpMethod.Put, $"files/{id}/details", request, cancellationToken: cancellationToken);

    /// <summary>Ausgewählte Dateien der Bauakte als ZIP (Originale, Ordner Fotos/Videos/Dokumente).</summary>
    public async Task DownloadArchiveAsync(Guid bauakteId, IReadOnlyCollection<Guid> fileIds, Stream target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileIds);
        ArgumentNullException.ThrowIfNull(target);
        using var response = await SendRequestAsync(_transfer, () => new HttpRequestMessage(HttpMethod.Get,
            $"bauakten/{bauakteId}/files/archive?ids={string.Join(',', fileIds)}"), authenticated: true, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ToExceptionAsync(response, cancellationToken);
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await source.CopyToAsync(target, cancellationToken);
    }

    public Task DeleteFileAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<object>(HttpMethod.Delete, $"files/{id}", null, cancellationToken: cancellationToken);

    public Task<UploadStatus> GetUploadStatusAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetAsync<UploadStatus>($"files/{id}/upload-status", cancellationToken: cancellationToken);

    /// <summary>
    /// Datei hochladen wie die Apps: SHA-256 berechnen, ankündigen, Inhalt senden. Ist ein Teil schon angekommen
    /// (abgebrochener Upload derselben ID), wird ab dieser Stelle fortgesetzt.
    /// </summary>
    /// <param name="open">Öffnet den Inhalt (für die Prüfsumme und je Sendeversuch neu).</param>
    public async Task<FileItem> UploadFileAsync(Guid id, Guid? bauakteId, string fileName, Func<Stream> open, DateTimeOffset? capturedAt = null,
        string? category = null, string? caption = null, IProgress<double>? progress = null, FileKind? kind = null, string? folder = null,
        Guid? previousVersionId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(open);
        long size;
        string sha256;
        await using (var stream = open())
        {
            using var hash = SHA256.Create();
            var digest = await hash.ComputeHashAsync(stream, cancellationToken);
            size = stream.Length;
            sha256 = Convert.ToHexStringLower(digest);
        }

        if (size == 0)
        {
            throw new ArgumentException($"„{fileName}“ ist leer und kann nicht hochgeladen werden.");
        }

        var contentType = FileTypes.ContentTypeOf(fileName);
        var fileKind = kind ?? FileTypes.KindOf(contentType);
        var announced = await AnnounceFileAsync(new AnnounceFileRequest(id, bauakteId, fileKind, fileName, contentType, size, sha256,
            CapturedAt: fileKind == FileKind.Photo ? capturedAt : null, Caption: caption, Category: fileKind == FileKind.Document ? category : null,
            Folder: fileKind == FileKind.Document ? folder : null, PreviousVersionId: previousVersionId), cancellationToken);
        if (announced.IsStored)
        {
            progress?.Report(1);
            return announced;
        }

        var offset = (await GetUploadStatusAsync(id, cancellationToken)).ReceivedBytes;
        using var response = await SendRequestAsync(_transfer, () =>
        {
            var stream = open();
            stream.Seek(offset, SeekOrigin.Begin);
            var request = new HttpRequestMessage(HttpMethod.Put, $"files/{id}/content")
            {
                Content = new ProgressContent(stream, size - offset, progress is null ? null : new Progress<long>(sent => progress.Report((double)(offset + sent) / size))),
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            if (offset > 0)
            {
                request.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, size - 1, size);
            }

            return request;
        }, authenticated: true, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ToExceptionAsync(response, cancellationToken);
        }

        return await GetFileAsync(id, cancellationToken);
    }

    /// <summary>Inhalt (original, display oder thumb) in einen Datenstrom schreiben.</summary>
    public async Task DownloadFileAsync(Guid id, Stream target, string variant = "original", IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        using var response = await SendRequestAsync(_transfer, () => new HttpRequestMessage(HttpMethod.Get, $"files/{id}/content?variant={variant}"),
            authenticated: true, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ToExceptionAsync(response, cancellationToken);
        }

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            done += read;
            if (total is > 0)
            {
                progress?.Report((double)done / total.Value);
            }
        }
    }

    /// <summary>Inhalt als Bytes (für Vorschaubilder und Berichte).</summary>
    public async Task<byte[]> GetFileContentAsync(Guid id, string variant = "thumb", CancellationToken cancellationToken = default)
    {
        using var memory = new MemoryStream();
        await DownloadFileAsync(id, memory, variant, cancellationToken: cancellationToken);
        return memory.ToArray();
    }

    // Bautagebuch und Prüfungen: Schreiben wie die Apps über den Sync (Desktop-Sitzungen brauchen kein Gerät).

    public Task<PushResponse> PushAsync(IReadOnlyList<object> operations, CancellationToken cancellationToken = default) =>
        SendAsync<PushResponse>(HttpMethod.Post, "sync/push", new { operations }, cancellationToken: cancellationToken);

    private async Task PushOneAsync(string type, Guid entityId, object payload, CancellationToken cancellationToken)
    {
        var result = (await PushAsync([new { opId = Guid.CreateVersion7(), type, entityId, baseVersion = (long?)null, payload }], cancellationToken))
            .Results.Single();
        if (result.Status is not ("applied" or "duplicate"))
        {
            throw new ApiException(System.Net.HttpStatusCode.UnprocessableEntity, result.Code, result.Message ?? "Die Änderung wurde vom Server abgelehnt.");
        }
    }

    /// <param name="weather">sunny, cloudy, rain, snow, frost oder wind.</param>
    public Task AddDiaryEntryAsync(Guid id, Guid bauakteId, DateOnly day, string text, string? weather = null, int? temperatureCelsius = null,
        int? workersPresent = null, CancellationToken cancellationToken = default) =>
        AddDiaryEntryAsync(id, bauakteId, new DiaryInput(day, text, weather, temperatureCelsius, workersPresent), cancellationToken);

    public Task AddDiaryEntryAsync(Guid id, Guid bauakteId, DiaryInput entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return PushOneAsync("diary.create", id, new
        {
            bauakteId, day = entry.Day, text = entry.Text, weather = entry.Weather, temperatureCelsius = entry.TemperatureCelsius,
            workersPresent = entry.WorkersPresent, startTime = entry.StartTime, endTime = entry.EndTime, crew = entry.Crew,
            photoFileIds = entry.PhotoFileIds ?? [],
        }, cancellationToken);
    }

    /// <summary>Eigenen Eintrag ändern (nur der Verfasser).</summary>
    public Task UpdateDiaryEntryAsync(Guid id, string text, string? weather, int? temperatureCelsius, int? workersPresent,
        CancellationToken cancellationToken = default) =>
        PushOneAsync("diary.update", id, new { changes = new { text, weather, temperatureCelsius, workersPresent } }, cancellationToken);

    /// <summary>Eigenen Eintrag vollständig ändern (Text, Wetter, Mannschaft, Arbeitszeit, Fotos).</summary>
    public Task UpdateDiaryEntryAsync(Guid id, DiaryInput entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return PushOneAsync("diary.update", id, new
        {
            changes = new
            {
                text = entry.Text, weather = entry.Weather, temperatureCelsius = entry.TemperatureCelsius, workersPresent = entry.WorkersPresent,
                startTime = entry.StartTime, endTime = entry.EndTime, crew = entry.Crew, photoFileIds = entry.PhotoFileIds ?? [],
            },
        }, cancellationToken);
    }

    public Task DeleteDiaryEntryAsync(Guid id, CancellationToken cancellationToken = default) =>
        PushOneAsync("diary.delete", id, new { }, cancellationToken);

    public Task AddInitialInspectionAsync(Guid id, NewInitialInspection inspection, Guid? correctsInspectionId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        return PushOneAsync("inspection.create", id, new
        {
            bauakteId = inspection.BauakteId, kind = InspectionKind.InitialInspection, performedAt = inspection.PerformedAt,
            notRequired = inspection.NotRequired, checks = inspection.Checks, photoFileIds = inspection.PhotoFileIds ?? [],
            remarks = inspection.Remarks, correctsInspectionId,
        }, cancellationToken);
    }

    public Task AddCmMeasurementAsync(Guid id, NewCmMeasurement measurement, Guid? correctsInspectionId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        return PushOneAsync("inspection.create", id, new
        {
            bauakteId = measurement.BauakteId, kind = InspectionKind.CmMeasurement, performedAt = measurement.PerformedAt,
            location = measurement.Location, binder = measurement.Binder, heated = measurement.Heated, thicknessMm = measurement.ThicknessMm,
            measuredCmPercent = measurement.MeasuredCmPercent, remarks = measurement.Remarks, correctsInspectionId,
        }, cancellationToken);
    }

    // Kunden

    public Task<Contact> CreateContactAsync(SaveContactRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<Contact>(HttpMethod.Post, "contacts", request, cancellationToken: cancellationToken);

    /// <summary>Kontakt ändern; erfordert die zuletzt gelesene Version.</summary>
    public Task<Contact> UpdateContactAsync(Contact contact, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contact);
        return SendAsync<Contact>(HttpMethod.Put, $"contacts/{contact.Id}", new
        {
            version = contact.Version, kind = contact.Kind, name = contact.Name, customerNumber = contact.CustomerNumber, street = contact.Street,
            postalCode = contact.PostalCode, city = contact.City, phone = contact.Phone, email = contact.Email, notes = contact.Notes,
        }, cancellationToken: cancellationToken);
    }

    /// <summary>CSV-Import (Semikolon oder Komma, Kopfzeile); mit <paramref name="dryRun"/> nur prüfen.</summary>
    public async Task<ContactImportResult> ImportContactsAsync(string csv, bool dryRun, CancellationToken cancellationToken = default)
    {
        using var response = await SendRequestAsync(_http, () => new HttpRequestMessage(HttpMethod.Post, $"contacts/import?dryRun={(dryRun ? "true" : "false")}")
        {
            Content = new StringContent(csv, Encoding.UTF8, "text/csv"),
        }, authenticated: true, HttpCompletionOption.ResponseContentRead, cancellationToken);
        return await ReadAsync<ContactImportResult>(response, cancellationToken);
    }

    // Planung

    public Task<Assignment> SaveAssignmentAsync(Guid id, SaveAssignmentRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<Assignment>(HttpMethod.Put, $"planning/assignments/{id}", request, cancellationToken: cancellationToken);

    public Task DeleteAssignmentAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<object>(HttpMethod.Delete, $"planning/assignments/{id}", null, cancellationToken: cancellationToken);

    public Task<List<BacklogItem>> GetBacklogAsync(string? search = null, CancellationToken cancellationToken = default) =>
        GetAsync<List<BacklogItem>>("planning/backlog", new Dictionary<string, string?> { ["search"] = search }, cancellationToken);

    public Task<Crew> SaveCrewAsync(Guid id, SaveCrewRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<Crew>(HttpMethod.Put, $"planning/crews/{id}", request, cancellationToken: cancellationToken);

    public Task<Resource> SaveResourceAsync(Guid id, SaveResourceRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<Resource>(HttpMethod.Put, $"planning/resources/{id}", request, cancellationToken: cancellationToken);

    public Task<Absence> SaveAbsenceAsync(Guid id, SaveAbsenceRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<Absence>(HttpMethod.Put, $"planning/absences/{id}", request, cancellationToken: cancellationToken);

    public Task DeleteAbsenceAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<object>(HttpMethod.Delete, $"planning/absences/{id}", null, cancellationToken: cancellationToken);

    // Anrufe und Anfragen

    public Task<CallRecord> GetCallAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetAsync<CallRecord>($"calls/{id}", cancellationToken: cancellationToken);

    public Task DeleteCallAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<object>(HttpMethod.Delete, $"calls/{id}", null, cancellationToken: cancellationToken);

    public Task<List<CallRecord>> GetInquiriesAsync(InquiryStatus? status = null, bool open = false, string? search = null, int limit = 200,
        CancellationToken cancellationToken = default) =>
        GetAsync<List<CallRecord>>("calls", new Dictionary<string, string?>
        {
            ["kind"] = "inquiry", ["status"] = status is null ? null : JsonEnum(status.Value), ["open"] = open ? "true" : null, ["search"] = search,
            ["limit"] = limit.ToString(CultureInfo.InvariantCulture),
        }, cancellationToken);

    // Material

    public Task<Supplier> SaveSupplierAsync(SaveSupplierRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<Supplier>(HttpMethod.Put, $"suppliers/{request.Id}", request, cancellationToken: cancellationToken);

    public Task DeleteSupplierAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<object>(HttpMethod.Delete, $"suppliers/{id}", null, cancellationToken: cancellationToken);

    public Task<Material> SaveMaterialAsync(SaveMaterialRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<Material>(HttpMethod.Put, $"materials/{request.Id}", request, cancellationToken: cancellationToken);

    public Task DeleteMaterialAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<object>(HttpMethod.Delete, $"materials/{id}", null, cancellationToken: cancellationToken);

    public Task<MaterialOrder> GetOrderAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetAsync<MaterialOrder>($"material-orders/{id}", cancellationToken: cancellationToken);

    /// <summary>Entwurf oder stornierte Bestellung löschen.</summary>
    public Task DeleteOrderAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<object>(HttpMethod.Delete, $"material-orders/{id}", null, cancellationToken: cancellationToken);

    // Firma

    public Task<Company> SaveCompanyAsync(SaveCompanyRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<Company>(HttpMethod.Put, "company", request, cancellationToken: cancellationToken);

    // Helpdesk

    /// <param name="filter">open, mine, unassigned oder all.</param>
    public Task<List<TicketSummary>> GetTicketsAsync(string filter, TicketCategory? category, CancellationToken cancellationToken = default) =>
        GetAsync<List<TicketSummary>>("tickets", new Dictionary<string, string?>
        {
            ["filter"] = filter, ["category"] = category is null ? null : JsonEnum(category.Value),
        }, cancellationToken);

    public Task<TicketDetail> GetTicketAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetAsync<TicketDetail>($"tickets/{id}", cancellationToken: cancellationToken);

    public Task<TicketDetail> UpdateTicketAsync(Guid id, UpdateTicketRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<TicketDetail>(HttpMethod.Patch, $"tickets/{id}", request, cancellationToken: cancellationToken);

    public Task<TicketDetail> CommentTicketAsync(Guid id, string text, bool isInternal, CancellationToken cancellationToken = default) =>
        SendAsync<TicketDetail>(HttpMethod.Post, $"tickets/{id}/comments", new { text, isInternal }, cancellationToken: cancellationToken);

    public Task<TicketDetail> ReopenTicketAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<TicketDetail>(HttpMethod.Post, $"tickets/{id}/reopen", null, cancellationToken: cancellationToken);

    // Verwaltung: Benutzer und Rollen

    public Task<List<UserListItem>> GetUsersAsync(CancellationToken cancellationToken = default) =>
        GetAsync<List<UserListItem>>("users", cancellationToken: cancellationToken);

    public Task<UserWithPassword> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<UserWithPassword>(HttpMethod.Post, "users", request, cancellationToken: cancellationToken);

    public Task<UserListItem> UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<UserListItem>(HttpMethod.Put, $"users/{id}", request, cancellationToken: cancellationToken);

    /// <summary>Einmalpasswort vergeben; beendet alle Sitzungen der Person.</summary>
    public Task<UserWithPassword> ResetPasswordAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<UserWithPassword>(HttpMethod.Post, $"users/{id}/reset-password", null, cancellationToken: cancellationToken);

    public Task<UserListItem> SetUserActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default) =>
        SendAsync<UserListItem>(HttpMethod.Post, $"users/{id}/{(active ? "activate" : "deactivate")}", null, cancellationToken: cancellationToken);

    public Task SetUserRolesAsync(Guid userId, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken = default) =>
        SendAsync<object>(HttpMethod.Put, $"access/users/{userId}/roles", new { roleIds }, cancellationToken: cancellationToken);

    public Task<List<Role>> GetRolesAsync(CancellationToken cancellationToken = default) =>
        GetAsync<List<Role>>("access/roles", cancellationToken: cancellationToken);

    public Task<List<PermissionInfo>> GetPermissionCatalogAsync(CancellationToken cancellationToken = default) =>
        GetAsync<List<PermissionInfo>>("access/permissions", cancellationToken: cancellationToken);

    // Geräte

    public Task<List<Device>> GetDevicesAsync(CancellationToken cancellationToken = default) =>
        GetAsync<List<Device>>("devices", cancellationToken: cancellationToken);

    /// <param name="action">approve, lock, unlock oder retire.</param>
    public Task<Device> ChangeDeviceAsync(Guid id, string action, CancellationToken cancellationToken = default) =>
        SendAsync<Device>(HttpMethod.Post, $"devices/{id}/{action}", null, cancellationToken: cancellationToken);

    public Task<EnrollmentCode> CreateEnrollmentCodeAsync(DeviceKind kind, string? deviceName, CancellationToken cancellationToken = default) =>
        SendAsync<EnrollmentCode>(HttpMethod.Post, "devices/enrollment-codes", new { kind, deviceName }, cancellationToken: cancellationToken);

    // Sicherung und Protokoll

    public Task<BackupStatus> GetBackupStatusAsync(CancellationToken cancellationToken = default) =>
        GetAsync<BackupStatus>("backup/status", cancellationToken: cancellationToken);

    public Task RequestBackupAsync(CancellationToken cancellationToken = default) =>
        SendAsync<object>(HttpMethod.Post, "backup/run", null, cancellationToken: cancellationToken);

    public Task<AuditLogPage> GetAuditAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return GetAsync<AuditLogPage>("audit", new Dictionary<string, string?>
        {
            ["action"] = query.Action, ["actorUserId"] = query.ActorUserId?.ToString(), ["entityType"] = query.EntityType, ["entityId"] = query.EntityId,
            ["from"] = query.From?.ToString("O", CultureInfo.InvariantCulture), ["to"] = query.To?.ToString("O", CultureInfo.InvariantCulture),
            ["cursor"] = query.Cursor, ["limit"] = query.Limit.ToString(CultureInfo.InvariantCulture),
        }, cancellationToken);
    }

    /// <summary>Sendet einen Datenstrom und meldet den Fortschritt (gesendete Bytes).</summary>
    private sealed class ProgressContent(Stream stream, long length, IProgress<long>? progress) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream target, System.Net.TransportContext? context) =>
            await SerializeToStreamAsync(target, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream target, System.Net.TransportContext? context, CancellationToken cancellationToken)
        {
            var buffer = new byte[81920];
            long sent = 0;
            int read;
            while (sent < length && (read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length - sent)), cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                sent += read;
                progress?.Report(sent);
            }
        }

        protected override bool TryComputeLength(out long result)
        {
            result = length;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                stream.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

/// <summary>Dateiarten und Inhaltstypen wie der Server sie erwartet (Fotos, Videos, Dokumente).</summary>
public static class FileTypes
{
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".webp"] = "image/webp", [".heic"] = "image/heic",
        [".heif"] = "image/heic", [".mp4"] = "video/mp4", [".m4v"] = "video/mp4", [".mov"] = "video/quicktime", [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".odt"] = "application/vnd.oasis.opendocument.text", [".ods"] = "application/vnd.oasis.opendocument.spreadsheet",
        [".zip"] = "application/zip",
    };

    /// <summary>Endungen, die der Server annimmt (Prüfung des Inhalts erfolgt dort).</summary>
    public static IReadOnlyCollection<string> Supported => ByExtension.Keys;

    public static string ContentTypeOf(string fileName) =>
        ByExtension.GetValueOrDefault(Path.GetExtension(fileName ?? string.Empty), "application/octet-stream");

    public static bool IsSupported(string fileName) => ByExtension.ContainsKey(Path.GetExtension(fileName ?? string.Empty));

    public static FileKind KindOf(string contentType) => contentType switch
    {
        _ when contentType.StartsWith("image/", StringComparison.Ordinal) => FileKind.Photo,
        _ when contentType.StartsWith("video/", StringComparison.Ordinal) => FileKind.Video,
        _ => FileKind.Document,
    };

    public static bool IsPdf(string contentType) => contentType == "application/pdf";

    public static bool IsImage(string contentType) => contentType.StartsWith("image/", StringComparison.Ordinal);

    public static string Size(long bytes) => bytes switch
    {
        < 1024 * 1024 => $"{Math.Max(1, bytes / 1024)} KB",
        _ => (bytes / (1024.0 * 1024)).ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) + " MB",
    };
}
