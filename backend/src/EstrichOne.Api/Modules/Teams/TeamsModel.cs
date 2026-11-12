using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Teams.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Teams;

/// <summary>Anmeldung der App-Registrierung bei Microsoft Entra ID.</summary>
public enum TeamsAuthKind
{
    /// <summary>Geheimer Clientschlüssel (läuft nach höchstens 24 Monaten ab).</summary>
    Secret,

    /// <summary>Zertifikat (PEM mit privatem Schlüssel) – empfohlen.</summary>
    Certificate,
}

/// <summary>Art der Teams-Unterhaltung einer Bauakte.</summary>
public enum TeamsLinkKind
{
    /// <summary>Standardkanal im Team „Baustellen“.</summary>
    Channel,

    /// <summary>Gruppenchat mit dem Team der Bauakte.</summary>
    GroupChat,
}

/// <summary>Wie Ereigniskarten nach Teams gelangen.</summary>
public enum TeamsPostVia
{
    Off,

    /// <summary>Microsoft Graph mit einem verbundenen Dienstkonto (delegiert) in die verknüpfte Unterhaltung.</summary>
    Graph,

    /// <summary>Teams-Workflows-Webhook (ohne App-Registrierung); je Bauakte überschreibbar.</summary>
    Webhook,
}

/// <summary>Einstellungen der Teams-Anbindung (eine Zeile). Geheimnisse nur verschlüsselt (Data Protection).</summary>
public sealed class TeamsSettingsRow : IVersionedEntity
{
    public const int SingletonId = 1;

    public int Id { get; private set; } = SingletonId;

    public TeamsChatMode ChatMode { get; set; } = TeamsChatMode.EstrichOne;

    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public TeamsAuthKind AuthKind { get; set; } = TeamsAuthKind.Secret;
    public string? ProtectedClientSecret { get; set; }
    public string? ProtectedCertificate { get; set; }
    public string? CertificateThumbprint { get; set; }
    public DateTimeOffset? CertificateExpiresAt { get; set; }

    /// <summary>Team „Baustellen“ (Gruppen-ID), in dem Kanäle angelegt werden.</summary>
    public string? SiteTeamId { get; set; }
    public string? SiteTeamName { get; set; }
    public string? SiteTeamUrl { get; set; }

    /// <summary>Art beim automatischen Anlegen.</summary>
    public TeamsLinkKind DefaultLinkKind { get; set; } = TeamsLinkKind.Channel;

    /// <summary>Neue Bauakten (angelegt ab <see cref="AutoLinkSince"/>) automatisch verknüpfen.</summary>
    public bool AutoLink { get; set; }
    public DateTimeOffset? AutoLinkSince { get; set; }

    /// <summary>Mitglieder abgleichen (Gruppenchat: hinzufügen und von EstrichONE Hinzugefügte entfernen; Kanal: ins Team aufnehmen).</summary>
    public bool SyncMembers { get; set; } = true;

    public TeamsPostVia PostVia { get; set; } = TeamsPostVia.Off;
    public string? ProtectedWebhookUrl { get; set; }
    public bool PostDefects { get; set; } = true;
    public bool PostAcceptance { get; set; } = true;
    public bool PostSchedule { get; set; } = true;

    /// <summary>Dienstkonto für Nachrichten (delegiert, Gerätecode-Anmeldung); Aktualisierungstoken verschlüsselt.</summary>
    public string? ProtectedSenderToken { get; set; }
    public string? SenderName { get; set; }
    public string? SenderUserId { get; set; }
    public DateTimeOffset? SenderConnectedAt { get; set; }
    public string? ProtectedDeviceCode { get; set; }
    public DateTimeOffset? DeviceCodeExpiresAt { get; set; }

    public DateTimeOffset? LastTestAt { get; set; }
    public bool? LastTestOk { get; set; }
    public string? LastTestError { get; set; }
    public DateTimeOffset? LastPostAt { get; set; }
    public string? LastPostError { get; set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>App-Registrierung vollständig (Tenant, Client und Schlüssel bzw. Zertifikat).</summary>
    public bool GraphConfigured =>
        !string.IsNullOrWhiteSpace(TenantId) && !string.IsNullOrWhiteSpace(ClientId)
        && (AuthKind == TeamsAuthKind.Secret ? ProtectedClientSecret is not null : ProtectedCertificate is not null);
}

/// <summary>Zuordnung Benutzer ↔ Microsoft-Konto (automatisch über E-Mail/UPN oder von Hand).</summary>
public sealed class TeamsUserLink
{
    public Guid UserId { get; set; }

    /// <summary>Objekt-ID in Entra ID; <c>null</c> = kein Konto gefunden.</summary>
    public string? AadUserId { get; set; }
    public string? UserPrincipalName { get; set; }
    public string? DisplayName { get; set; }

    /// <summary>Von Hand gesetzt: der automatische Abgleich ändert sie nicht.</summary>
    public bool Manual { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
    public string? Error { get; set; }
}

/// <summary>Verknüpfte Teams-Unterhaltung einer Bauakte.</summary>
public sealed class TeamsBauakteLink
{
    public const int MaxName = 200;

    public Guid BauakteId { get; set; }
    public TeamsLinkKind Kind { get; set; }

    /// <summary>Von EstrichONE angelegt (sonst per Link verknüpft).</summary>
    public bool Created { get; set; }

    public string? TeamId { get; set; }
    public string? ChannelId { get; set; }
    public string? ChatId { get; set; }
    public string? TenantId { get; set; }
    public string WebUrl { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public bool SyncMembers { get; set; } = true;

    /// <summary>Microsoft-Konten, die EstrichONE hinzugefügt hat – nur diese entfernt der Abgleich wieder.</summary>
    public List<string> ManagedMemberIds { get; set; } = [];

    /// <summary>Webhook nur für diese Bauakte (verschlüsselt); sonst der allgemeine.</summary>
    public string? ProtectedWebhookUrl { get; set; }

    public Guid LinkedByUserId { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
    public string? LastSyncError { get; set; }
    public int SyncedMembers { get; set; }
    public int UnmappedMembers { get; set; }
    public DateTimeOffset? LastPostAt { get; set; }
    public string? LastPostError { get; set; }
    public bool SyncPending { get; set; }

    /// <summary>Prüfsumme der zuletzt abgeglichenen Soll-Mitglieder (unverändert → kein erneuter Abgleich nötig).</summary>
    public string? DesiredHash { get; set; }
}

internal sealed class TeamsSettingsRowConfiguration : IEntityTypeConfiguration<TeamsSettingsRow>
{
    public void Configure(EntityTypeBuilder<TeamsSettingsRow> builder)
    {
        builder.ToTable("teams_settings");
        builder.ConfigureVersioned();
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.ChatMode).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.AuthKind).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.DefaultLinkKind).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.PostVia).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.TenantId).HasMaxLength(100);
        builder.Property(s => s.ClientId).HasMaxLength(100);
        builder.Property(s => s.CertificateThumbprint).HasMaxLength(100);
        builder.Property(s => s.SiteTeamId).HasMaxLength(100);
        builder.Property(s => s.SiteTeamName).HasMaxLength(TeamsBauakteLink.MaxName);
        builder.Property(s => s.SiteTeamUrl).HasMaxLength(2000);
        builder.Property(s => s.SenderName).HasMaxLength(TeamsBauakteLink.MaxName);
        builder.Property(s => s.SenderUserId).HasMaxLength(100);
        builder.Property(s => s.LastTestError).HasMaxLength(2000);
        builder.Property(s => s.LastPostError).HasMaxLength(2000);
        builder.Ignore(s => s.GraphConfigured);
    }
}

internal sealed class TeamsUserLinkConfiguration : IEntityTypeConfiguration<TeamsUserLink>
{
    public void Configure(EntityTypeBuilder<TeamsUserLink> builder)
    {
        builder.ToTable("teams_user_links");
        builder.HasKey(l => l.UserId);
        builder.Property(l => l.AadUserId).HasMaxLength(100);
        builder.Property(l => l.UserPrincipalName).HasMaxLength(320);
        builder.Property(l => l.DisplayName).HasMaxLength(TeamsBauakteLink.MaxName);
        builder.Property(l => l.Error).HasMaxLength(500);
    }
}

internal sealed class TeamsBauakteLinkConfiguration : IEntityTypeConfiguration<TeamsBauakteLink>
{
    public void Configure(EntityTypeBuilder<TeamsBauakteLink> builder)
    {
        builder.ToTable("teams_bauakte_links");
        builder.HasKey(l => l.BauakteId);
        builder.Property(l => l.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.TeamId).HasMaxLength(100);
        builder.Property(l => l.ChannelId).HasMaxLength(300);
        builder.Property(l => l.ChatId).HasMaxLength(300);
        builder.Property(l => l.TenantId).HasMaxLength(100);
        builder.Property(l => l.WebUrl).HasMaxLength(2000);
        builder.Property(l => l.DisplayName).HasMaxLength(TeamsBauakteLink.MaxName);
        builder.Property(l => l.LastSyncError).HasMaxLength(2000);
        builder.Property(l => l.LastPostError).HasMaxLength(2000);
        builder.Property(l => l.DesiredHash).HasMaxLength(64);
    }
}
