using System.Globalization;

namespace EstrichOne.Client;

/// <summary>Fachliche Aufrufe: Bauakten, Räume, Aufgaben, Notizen, Dateien, Prüfungen, Planung, Kunden, Helpdesk.</summary>
public sealed partial class EstrichOneClient
{
    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // Bauakten

    public Task<BauaktePage> ListBauaktenAsync(string? search = null, BauakteStatus? status = null, string? cursor = null, int limit = 100,
        CancellationToken cancellationToken = default) =>
        GetAsync<BauaktePage>("bauakten", new Dictionary<string, string?>
        {
            ["search"] = search, ["status"] = status is null ? null : JsonEnum(status.Value), ["cursor"] = cursor,
            ["limit"] = limit.ToString(CultureInfo.InvariantCulture),
        }, cancellationToken);

    public Task<BauakteDetail> GetBauakteAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetAsync<BauakteDetail>($"bauakten/{id}", cancellationToken: cancellationToken);

    public Task<BauakteDetail> CreateBauakteAsync(CreateBauakteRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<BauakteDetail>(HttpMethod.Post, "bauakten", request, cancellationToken: cancellationToken);

    /// <summary>Speichern mit der gelesenen Version; bei zwischenzeitlicher Änderung <see cref="ApiException"/> mit Code VERSION_CONFLICT.</summary>
    public Task<BauakteDetail> UpdateBauakteAsync(Guid id, long version, UpdateBauakteRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<BauakteDetail>(HttpMethod.Put, $"bauakten/{id}", request,
            headers: new Dictionary<string, string> { ["If-Match"] = $"\"{version.ToString(CultureInfo.InvariantCulture)}\"" },
            cancellationToken: cancellationToken);

    public Task<List<BauakteHint>> GetHintsAsync(Guid bauakteId, CancellationToken cancellationToken = default) =>
        GetAsync<List<BauakteHint>>($"bauakten/{bauakteId}/hints", cancellationToken: cancellationToken);

    public Task<List<BauakteMember>> GetMembersAsync(Guid bauakteId, CancellationToken cancellationToken = default) =>
        GetAsync<List<BauakteMember>>($"bauakten/{bauakteId}/members", cancellationToken: cancellationToken);

    public Task<List<BauakteMember>> SetMembersAsync(Guid bauakteId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default) =>
        SendAsync<List<BauakteMember>>(HttpMethod.Put, $"bauakten/{bauakteId}/members", new { userIds }, cancellationToken: cancellationToken);

    public Task<List<BauakteMapPoint>> GetMapPointsAsync(bool all = false, CancellationToken cancellationToken = default) =>
        GetAsync<List<BauakteMapPoint>>("bauakten/map", new Dictionary<string, string?> { ["all"] = all ? "true" : null }, cancellationToken);

    public Task<List<BauakteHistoryEntry>> GetHistoryAsync(Guid bauakteId, CancellationToken cancellationToken = default) =>
        GetAsync<List<BauakteHistoryEntry>>($"bauakten/{bauakteId}/history", cancellationToken: cancellationToken);

    public Task<RoomOverview> GetRoomsAsync(Guid bauakteId, CancellationToken cancellationToken = default) =>
        GetAsync<RoomOverview>($"bauakten/{bauakteId}/rooms", cancellationToken: cancellationToken);

    public Task<List<WorkItem>> GetWorkItemsAsync(Guid bauakteId, CancellationToken cancellationToken = default) =>
        GetAsync<List<WorkItem>>($"bauakten/{bauakteId}/work-items", cancellationToken: cancellationToken);

    public Task<List<WorkItem>> GetMyWorkItemsAsync(CancellationToken cancellationToken = default) =>
        GetAsync<List<WorkItem>>("work-items/mine", cancellationToken: cancellationToken);

    public Task<WorkItem> CreateWorkItemAsync(CreateWorkItemRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<WorkItem>(HttpMethod.Post, "work-items", request, cancellationToken: cancellationToken);

    public Task<WorkItem> SetWorkItemStatusAsync(Guid id, WorkItemStatus status, CancellationToken cancellationToken = default) =>
        SendAsync<WorkItem>(HttpMethod.Patch, $"work-items/{id}", new { status }, cancellationToken: cancellationToken);

    public Task<List<Note>> GetNotesAsync(Guid bauakteId, CancellationToken cancellationToken = default) =>
        GetAsync<List<Note>>($"bauakten/{bauakteId}/notes", cancellationToken: cancellationToken);

    public Task<Note> AddNoteAsync(Guid bauakteId, string text, CancellationToken cancellationToken = default) =>
        SendAsync<Note>(HttpMethod.Post, $"bauakten/{bauakteId}/notes", new { id = Guid.CreateVersion7(), text }, cancellationToken: cancellationToken);

    public Task<List<FileItem>> GetFilesAsync(Guid bauakteId, CancellationToken cancellationToken = default) =>
        GetAsync<List<FileItem>>($"bauakten/{bauakteId}/files", cancellationToken: cancellationToken);

    public Task<List<Inspection>> GetInspectionsAsync(Guid bauakteId, CancellationToken cancellationToken = default) =>
        GetAsync<List<Inspection>>($"bauakten/{bauakteId}/inspections", cancellationToken: cancellationToken);

    public Task<List<DiaryEntry>> GetDiaryAsync(Guid bauakteId, CancellationToken cancellationToken = default) =>
        GetAsync<List<DiaryEntry>>($"bauakten/{bauakteId}/diary", cancellationToken: cancellationToken);

    /// <summary>Dateiinhalt (z. B. Vorschaubild <c>thumb</c>) als Datenstrom; der Aufrufer schließt ihn.</summary>
    public async Task<byte[]> GetFileContentAsync(Guid fileId, string variant = "thumb", CancellationToken cancellationToken = default)
    {
        await EnsureFreshTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"files/{fileId}/content?variant={variant}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>Vor Binärabrufen sicherstellen, dass das Zugriffstoken gilt (ein günstiger Aufruf erneuert es bei Bedarf).</summary>
    private Task<UserSummary> EnsureFreshTokenAsync(CancellationToken cancellationToken) => GetAsync<UserSummary>("auth/me", cancellationToken: cancellationToken);

    // Stammdaten und Verzeichnis

    public Task<List<DirectoryUser>> GetDirectoryAsync(CancellationToken cancellationToken = default) =>
        GetAsync<List<DirectoryUser>>("directory/users", cancellationToken: cancellationToken);

    public Task<List<Contact>> SearchContactsAsync(string? search = null, int limit = 100, CancellationToken cancellationToken = default) =>
        GetAsync<List<Contact>>("contacts", new Dictionary<string, string?>
        {
            ["search"] = search, ["limit"] = limit.ToString(CultureInfo.InvariantCulture),
        }, cancellationToken);

    public Task<Contact> GetContactAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetAsync<Contact>($"contacts/{id}", cancellationToken: cancellationToken);

    // Planung

    public Task<PlanningRange> GetPlanningAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        GetAsync<PlanningRange>("planning/range", new Dictionary<string, string?> { ["from"] = Day(from), ["to"] = Day(to) }, cancellationToken);

    public Task<List<Crew>> GetCrewsAsync(CancellationToken cancellationToken = default) =>
        GetAsync<List<Crew>>("planning/crews", cancellationToken: cancellationToken);

    public Task<List<Resource>> GetResourcesAsync(CancellationToken cancellationToken = default) =>
        GetAsync<List<Resource>>("planning/resources", cancellationToken: cancellationToken);

    public Task<PlanningRange> GetMyPlanningAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        GetAsync<PlanningRange>("planning/mine", new Dictionary<string, string?> { ["from"] = Day(from), ["to"] = Day(to) }, cancellationToken);

    // Helpdesk

    /// <param name="filter">open, mine, unassigned oder all (nur Helpdesk-Bearbeitung).</param>
    public Task<List<TicketSummary>> GetTicketsAsync(string filter = "open", CancellationToken cancellationToken = default) =>
        GetAsync<List<TicketSummary>>("tickets", new Dictionary<string, string?> { ["filter"] = filter }, cancellationToken);

    public Task<List<TicketSummary>> GetMyTicketsAsync(CancellationToken cancellationToken = default) =>
        GetAsync<List<TicketSummary>>("tickets/mine", cancellationToken: cancellationToken);

    public Task<TicketDetail> ReportProblemAsync(CreateTicketRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<TicketDetail>(HttpMethod.Post, "tickets", request, cancellationToken: cancellationToken);

    private static string JsonEnum<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
