namespace EstrichOne.Client;

// Datentypen der API v1 (contracts/openapi/v1.json). JSON: camelCase, Aufzählungen als camelCase-Text.

public enum BauakteStatus { Inquiry, Planned, InProgress, Drying, Completed, Cancelled }

public enum WorkItemKind { Task, Defect }

public enum WorkItemStatus { Open, InProgress, Done }

public enum RoomStatus { Planned, Laid, Ready }

public enum ContactKind { Company, Person }

public enum ResourceKind { Vehicle, Pump, Machine }

public enum AbsenceKind { Vacation, Sick, Training, Other }

public enum TicketCategory { SignIn, Sync, AppError, DeviceDefect, OfficeIt, Access, Other }

public enum TicketPriority { Low, Normal, High, Urgent }

public enum TicketStatus { New, InProgress, WaitingForReporter, Resolved, Closed }

public enum TicketSource { Web, Desktop, Ios, Android }

public sealed record ServerInfo(
    string Product, string ServerName, string ServerVersion, IReadOnlyList<string> ApiVersions, string MinClientVersion,
    IReadOnlyList<string> AuthMethods, Guid InstanceEpoch, bool SetupRequired);

public sealed record UserSummary(Guid Id, string DisplayName, string UserName, string? Email);

public sealed record TokenResponse(
    string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt,
    UserSummary User, bool MustChangePassword);

public sealed record RefreshResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

public sealed record DirectoryUser(Guid Id, string DisplayName, bool IsActive);

public sealed record BauakteListItem(
    Guid Id, string? Number, string Title, BauakteStatus Status, string? Street, string? PostalCode, string? City,
    Guid? CustomerId, DateTimeOffset UpdatedAt, long Version, Guid? ResponsibleUserId, double? Latitude, double? Longitude);

public sealed record BauaktePage(IReadOnlyList<BauakteListItem> Items, string? NextCursor);

public sealed record BauakteDetail(
    Guid Id, string? Number, string Title, BauakteStatus Status, string? Street, string? PostalCode, string? City,
    double? Latitude, double? Longitude, string? Description, Guid? CustomerId, string? TemplateKey,
    Guid? ResponsibleUserId, Guid CreatedByUserId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version,
    DateOnly? PlannedStart = null, DateOnly? PlannedEnd = null, string? SiteContactName = null, string? SiteContactPhone = null,
    string? SiteContactEmail = null, string? SiteNotes = null, string? CancellationReason = null);

public enum BauakteHintSeverity { Info, Success, Warning, Danger }

/// <summary>Hinweiszeile der Bauakte („Einsatz heute“, „Erstbegehung offen“ …), vom Server berechnet.</summary>
public sealed record BauakteHint(string Kind, BauakteHintSeverity Severity, string Text);

public enum BauakteMemberRole { Responsible, Member }

public sealed record BauakteMember(Guid UserId, string DisplayName, string? Email, BauakteMemberRole Role, bool IsActive);

public sealed record CreateBauakteRequest(
    Guid Id, string Title, string? Street = null, string? PostalCode = null, string? City = null, double? Latitude = null,
    double? Longitude = null, string? Description = null, Guid? CustomerId = null, string? TemplateKey = null, BauakteStatus? Status = null);

/// <summary>Ersetzt alle bearbeitbaren Felder – deshalb immer mit <see cref="From"/> vom gelesenen Stand ausgehen.</summary>
public sealed record UpdateBauakteRequest(
    string Title, BauakteStatus Status, string? Street, string? PostalCode, string? City, double? Latitude, double? Longitude,
    string? Description, Guid? CustomerId, Guid? ResponsibleUserId, DateOnly? PlannedStart, DateOnly? PlannedEnd,
    string? SiteContactName, string? SiteContactPhone, string? SiteContactEmail, string? SiteNotes, string? CancellationReason)
{
    /// <summary>Alle bearbeitbaren Felder aus dem gelesenen Stand übernehmen.</summary>
    public static UpdateBauakteRequest From(BauakteDetail b) =>
        new(b.Title, b.Status, b.Street, b.PostalCode, b.City, b.Latitude, b.Longitude, b.Description, b.CustomerId, b.ResponsibleUserId,
            b.PlannedStart, b.PlannedEnd, b.SiteContactName, b.SiteContactPhone, b.SiteContactEmail, b.SiteNotes, b.CancellationReason);
}

public sealed record BauakteMapPoint(
    Guid Id, string? Number, string Title, BauakteStatus Status, string? Street, string? City, double Latitude, double Longitude,
    Guid? ResponsibleUserId);

public sealed record BauakteHistoryEntry(DateTimeOffset At, string Action, Guid? ActorUserId, string? ActorName, string Text);

public sealed record SectionResponse(Guid Id, string Name, int SortOrder);

public sealed record RoomResponse(
    Guid Id, Guid? SectionId, string Name, string? Floor, decimal? AreaSquareMeters, Guid? ScreedTypeId, string? ScreedTypeName,
    int? ThicknessMillimeters, int? InsulationMillimeters, bool UnderfloorHeating, RoomStatus Status, string? Notes, long Version);

public sealed record RoomOverview(
    IReadOnlyList<SectionResponse> Sections, IReadOnlyList<RoomResponse> Rooms, decimal TotalAreaSquareMeters, decimal HeatedAreaSquareMeters);

public sealed record WorkItem(
    Guid Id, Guid? BauakteId, WorkItemKind Kind, WorkItemStatus Status, string Title, string? Description, DateOnly? DueDate,
    Guid? AssigneeUserId, Guid CreatedByUserId, DateTimeOffset? CompletedAt, DateTimeOffset UpdatedAt, long Version);

public sealed record CreateWorkItemRequest(
    Guid Id, Guid BauakteId, WorkItemKind Kind, string Title, string? Description = null, DateOnly? DueDate = null, Guid? AssigneeUserId = null);

public sealed record Note(
    Guid Id, Guid BauakteId, Guid AuthorUserId, string Text, bool IsPinned, string? Color, DateTimeOffset CreatedAt, DateTimeOffset? EditedAt, long Version);

public sealed record FileItem(
    Guid Id, Guid? BauakteId, string Kind, string State, string Variants, string OriginalName, string ContentType, long SizeBytes,
    string? Caption, DateTimeOffset? CapturedAt, DateTimeOffset CreatedAt);

public sealed record Inspection(
    Guid Id, Guid BauakteId, string Kind, Guid PerformedByUserId, DateTimeOffset PerformedAt, string? Remarks, bool NotRequired,
    string? Location, decimal? MeasuredCmPercent, decimal? LimitCmPercent, bool? IsReadyForCovering);

public sealed record DiaryEntry(
    Guid Id, Guid BauakteId, Guid AuthorUserId, DateOnly Day, string Text, string? Weather, int? TemperatureCelsius, int? WorkersPresent,
    DateTimeOffset CreatedAt, DateTimeOffset? EditedAt);

public sealed record Contact(
    Guid Id, ContactKind Kind, string Name, string? CustomerNumber, string? Street, string? PostalCode, string? City, string? Phone,
    string? Email, string? Notes, long Version);

public sealed record Crew(Guid Id, string Name, Guid? LeaderUserId, IReadOnlyList<Guid> MemberUserIds, string? Color, bool IsActive, int SortOrder, long Version);

public sealed record Resource(Guid Id, ResourceKind Kind, string Name, string? Identifier, string? Notes, bool IsActive, long Version);

public sealed record AssignmentPerson(Guid Id, string Name);

public sealed record AssignmentResource(Guid Id, string Name, ResourceKind Kind);

public enum AssignmentKind { Screed, InitialInspection, SecondInspection, CmMeasurement, Acceptance, Survey, Check, CustomerAppointment, Delivery, Other }

public sealed record Assignment(
    Guid Id, DateOnly Day, TimeOnly? StartTime, string Activity, Guid BauakteId, string? BauakteNumber, string BauakteTitle,
    string? BauakteAddress, Guid? CrewId, string? CrewName, string? CrewColor, IReadOnlyList<AssignmentPerson> People,
    IReadOnlyList<AssignmentResource> Resources, string? Notes, IReadOnlyList<string> Warnings, long Version,
    AssignmentKind Kind = AssignmentKind.Screed, TimeOnly? EndTime = null, BauakteStatus? BauakteStatus = null);

public sealed record Absence(Guid Id, Guid UserId, string UserName, DateOnly From, DateOnly To, AbsenceKind Kind, string? Note);

public sealed record PlanningRange(DateOnly From, DateOnly To, IReadOnlyList<Assignment> Assignments, IReadOnlyList<Absence> Absences);

public sealed record TicketSummary(
    Guid Id, string Number, string Title, TicketCategory Category, TicketPriority Priority, TicketStatus Status, bool ReporterBlocked,
    Guid? ReporterUserId, string? ReporterDisplayName, Guid? AssigneeUserId, string? AssigneeName, TicketSource Source,
    DateTimeOffset CreatedAt, DateTimeOffset LastActivityAt, long Version);

public sealed record TicketComment(Guid Id, Guid AuthorUserId, string? AuthorName, string Text, bool IsInternal, DateTimeOffset CreatedAt);

public sealed record TicketDetail(
    TicketSummary Summary, string? Description, Guid? DeviceId, string? DeviceName, Guid? BauakteId, string? Resolution,
    DateTimeOffset? ResolvedAt, DateTimeOffset? ClosedAt, IReadOnlyList<Guid> AttachmentFileIds, IReadOnlyList<TicketComment> Comments);

public sealed record CreateTicketRequest(
    Guid Id, string Title, string? Description, TicketCategory Category, TicketSource Source, bool ReporterBlocked, Guid? BauakteId = null,
    IReadOnlyDictionary<string, object?>? Diagnostics = null);

// Büro: Anrufe und Anfragen

public enum CallKind { Call, Inquiry }

public enum CallDirection { Inbound, Outbound }

public enum CallSource { Phone, Email, Website, Personal, Other }

public enum CallPriority { Low, Normal, High }

public enum InquiryStatus { Open, InProgress, Converted, Cancelled }

public sealed record CallRecord(
    Guid Id, CallKind Kind, CallDirection Direction, CallSource Source, CallPriority Priority, string? CallerName, string? CallerPhone,
    string? CallerEmail, string? Company, Guid? CustomerId, string? CustomerName, Guid? BauakteId, string? BauakteLabel, string? Subject,
    string? Notes, bool FollowUp, DateOnly? DueDate, string? NextStep, InquiryStatus InquiryStatus, decimal? AreaSquareMeters, string? ScreedType,
    DateOnly? DesiredStart, string? SiteStreet, string? SitePostalCode, string? SiteCity, Guid? ConvertedBauakteId, string? ConvertedBauakteNumber,
    Guid HandledByUserId, string? HandledByName, DateTimeOffset CalledAt, DateTimeOffset UpdatedAt, long Version);

/// <summary>Anruf oder Anfrage speichern (PUT ersetzt alle Felder).</summary>
public sealed record SaveCallRequest(
    Guid Id, CallKind Kind, CallDirection Direction = CallDirection.Inbound, CallSource Source = CallSource.Phone,
    CallPriority Priority = CallPriority.Normal, string? CallerName = null, string? CallerPhone = null, string? CallerEmail = null,
    string? Company = null, Guid? CustomerId = null, Guid? BauakteId = null, string? Subject = null, string? Notes = null, bool FollowUp = false,
    DateOnly? DueDate = null, string? NextStep = null, decimal? AreaSquareMeters = null, string? ScreedType = null, DateOnly? DesiredStart = null,
    string? SiteStreet = null, string? SitePostalCode = null, string? SiteCity = null, DateTimeOffset? CalledAt = null);

public sealed record ConvertInquiryResponse(CallRecord Inquiry, Guid BauakteId, string? BauakteNumber);

// Material

public enum MaterialCategory { Screed, Binder, Aggregate, Insulation, Membrane, EdgeStrip, Additive, Other }

public enum OrderStatus { Draft, Ordered, Confirmed, Delivered, Cancelled }

public sealed record Supplier(
    Guid Id, string Name, string? CustomerNumber, string? ContactName, string? Phone, string? Email, string? Street, string? PostalCode,
    string? City, string? OrderNotes, bool IsActive, long Version);

public sealed record Material(
    Guid Id, string Name, string? ArticleNumber, MaterialCategory Category, string Unit, Guid? SupplierId, string? SupplierName, decimal? Price,
    string? Notes, bool IsActive, long Version);

public sealed record OrderLine(
    Guid Id, int Position, Guid? MaterialId, string? ArticleNumber, string Description, decimal Quantity, string Unit, decimal? UnitPrice, decimal? Total);

public sealed record MaterialOrder(
    Guid Id, string Number, OrderStatus Status, Guid SupplierId, string SupplierName, string? SupplierCustomerNumber, string? SupplierEmail,
    string? SupplierPhone, Guid? BauakteId, string? BauakteNumber, string? BauakteTitle, DateOnly? DeliveryDate, string? DeliveryTime,
    string? DeliveryStreet, string? DeliveryPostalCode, string? DeliveryCity, string? SiteContact, string? Notes, IReadOnlyList<OrderLine> Lines,
    decimal? Total, Guid CreatedByUserId, string? CreatedByName, DateTimeOffset? OrderedAt, DateTimeOffset? DeliveredAt, DateTimeOffset UpdatedAt,
    long Version);

public sealed record SaveOrderLineRequest(Guid Id, Guid? MaterialId, string Description, decimal Quantity, string Unit, decimal? UnitPrice = null);

public sealed record SaveOrderRequest(
    Guid Id, Guid SupplierId, Guid? BauakteId = null, DateOnly? DeliveryDate = null, string? DeliveryTime = null, string? DeliveryStreet = null,
    string? DeliveryPostalCode = null, string? DeliveryCity = null, string? SiteContact = null, string? Notes = null,
    IReadOnlyList<SaveOrderLineRequest>? Lines = null);

public sealed record Company(string Name, string? Street, string? PostalCode, string? City, string? Phone, string? Email, string? Website, long Version);
