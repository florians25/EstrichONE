namespace EstrichOne.Api.Modules.Bauakten;

public sealed record NoteDto(
    Guid Id, Guid BauakteId, Guid AuthorUserId, string Text, bool IsPinned, string? Color, DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt, long Version)
{
    public static NoteDto From(Note note) => new(note.Id, note.BauakteId, note.AuthorUserId, note.Text, note.IsPinned,
        note.Color, note.CreatedAt, note.EditedAt, note.Version);
}

public sealed record ScreedTypeDto(Guid Id, string Name, string? Designation, ScreedBinder Binder, bool IsActive, long Version)
{
    public static ScreedTypeDto From(ScreedType type) => new(type.Id, type.Name, type.Designation, type.Binder, type.IsActive, type.Version);
}
