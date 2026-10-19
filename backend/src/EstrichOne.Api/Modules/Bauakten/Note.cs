using EstrichOne.Api.Infrastructure.Persistence;

namespace EstrichOne.Api.Modules.Bauakten;

/// <summary>
/// Notiz in der Bauakte (Verlauf im Ticket-Stil). Notizen werden angefügt; bearbeiten darf nur der Verfasser.
/// Das macht sie offline konfliktfrei (docs/reengineering/15, Abschnitt 2).
/// </summary>
public sealed class Note : IVersionedEntity, ISoftDeletable
{
    public const int MaxTextLength = 10_000;

    public Guid Id { get; private set; }
    public Guid BauakteId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public bool IsPinned { get; private set; }
    public string? Color { get; private set; }
    public DateTimeOffset? EditedAt { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private Note()
    {
    }

    public static Note Create(Guid id, Guid bauakteId, Guid authorUserId, string text) =>
        new() { Id = id, BauakteId = bauakteId, AuthorUserId = authorUserId, Text = Validate(text) };

    public void Edit(string text, DateTimeOffset at)
    {
        Text = Validate(text);
        EditedAt = at;
    }

    public void Pin(string? color)
    {
        IsPinned = true;
        Color = color;
    }

    public void Unpin() => IsPinned = false;

    public void Delete(DateTimeOffset at) => DeletedAt ??= at;

    private static string Validate(string text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        return trimmed.Length switch
        {
            0 => throw new ArgumentException("Die Notiz ist leer.", nameof(text)),
            > MaxTextLength => throw new ArgumentException($"Die Notiz ist länger als {MaxTextLength} Zeichen.", nameof(text)),
            _ => trimmed,
        };
    }
}
