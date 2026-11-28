using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Lexware;

/// <summary>Von Lexware Office abgelehnte Steuerart je Belegart (gemerkt, bis sie erneut geprüft wird).</summary>
public sealed record LexwareTaxTypeRejection(string VoucherType, string TaxType, DateTimeOffset At);

/// <summary>Steuerart zur Auswahl in einer Belegmaske.</summary>
/// <param name="Allowed">Wählbar; sonst ausgegraut mit <paramref name="Hint"/>.</param>
public sealed record LexwareTaxTypeOption(string Key, string Text, bool Allowed, string? Hint);

/// <summary>Stand der erlaubten Steuerarten (Einstellungen und Belegmasken).</summary>
/// <param name="SmallBusiness">Kleinunternehmer laut Profil (<c>null</c>: Profil liefert die Angabe nicht).</param>
/// <param name="ProfileTaxType">Besteuerungsart laut Profil (<c>net</c>, <c>gross</c>, <c>vatfree</c>), falls geliefert.</param>
/// <param name="CheckedAt">Letzte Prüfung über <c>GET /v1/profile</c>.</param>
public sealed record LexwareTaxTypeState(bool? SmallBusiness, string? ProfileTaxType, DateTimeOffset? CheckedAt, IReadOnlyList<LexwareTaxTypeRejection> Rejections);

/// <summary>
/// Erlaubte Steuerarten je Belegart. Grundlage: Profil der Organisation (<c>GET /v1/profile</c>: <c>smallBusiness</c>,
/// <c>taxType</c>) und die von Lexware Office beim Senden abgelehnten Steuerarten. Kleinunternehmer senden nur steuerfrei.
/// Reine Funktionen (mit Unit-Tests geprüft).
/// </summary>
public static class LexwareTaxTypes
{
    public const string Net = "net";
    public const string Gross = "gross";
    public const string Vatfree = "vatfree";
    public const string Construction13b = "constructionService13b";

    /// <summary>Steuerarten, die EstrichONE in Belegen anbietet (Reihenfolge der Auswahl).</summary>
    public static IReadOnlyList<string> Supported { get; } = [Net, Construction13b, Vatfree];

    public static string Text(string key) => key switch
    {
        Net => "Netto (Umsatzsteuer wird ausgewiesen)",
        Gross => "Brutto (Preise enthalten die Umsatzsteuer)",
        Vatfree => "Steuerfrei (zum Beispiel Kleinunternehmer nach § 19 UStG)",
        Construction13b => "Bauleistung nach § 13b UStG (Steuerschuld beim Leistungsempfänger)",
        _ => LexwareErrors.TaxTypeName(key),
    };

    /// <summary>Positionen ohne Umsatzsteuer (Steuersatz 0 an jeder Position).</summary>
    public static bool WithoutTax(string? taxType) => taxType is Construction13b or Vatfree;

    public static bool IsSmallBusiness(bool? smallBusiness, string? profileTaxType) => smallBusiness == true || profileTaxType == Vatfree;

    public static string VoucherName(string voucherType) => voucherType switch
    {
        LexwareVoucherTypes.Invoice => "Rechnungen",
        LexwareVoucherTypes.CreditNote => "Gutschriften",
        LexwareVoucherTypes.OrderConfirmation => "Auftragsbestätigungen",
        _ => "Angebote",
    };

    public static IReadOnlyList<LexwareTaxTypeOption> Options(string voucherType, LexwareTaxTypeState state)
    {
        var small = IsSmallBusiness(state.SmallBusiness, state.ProfileTaxType);
        return Supported.Select(key =>
        {
            if (small && key != Vatfree)
            {
                return new LexwareTaxTypeOption(key, Text(key), false,
                    "Ihr Unternehmen ist in Lexware Office als Kleinunternehmer nach § 19 UStG eingerichtet. Belege werden deshalb steuerfrei gesendet.");
            }

            if (state.Rejections.FirstOrDefault(r => r.VoucherType == voucherType && r.TaxType == key) is { } rejected)
            {
                return new LexwareTaxTypeOption(key, Text(key), false,
                    $"Lexware Office hat diese Steuerart am {rejected.At.ToOffset(TimeSpan.FromHours(1)).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)} für {VoucherName(voucherType)} abgelehnt. Unter Verwaltung › Integrationen › Lexware Office können Sie die erlaubten Steuerarten erneut prüfen.");
            }

            return new LexwareTaxTypeOption(key, Text(key), true, null);
        }).ToList();
    }

    /// <summary>Vorgabe für neue Belege: Kleinunternehmer steuerfrei, sonst Netto.</summary>
    public static string Default(LexwareTaxTypeState state) => IsSmallBusiness(state.SmallBusiness, state.ProfileTaxType) ? Vatfree : Net;

    /// <summary>Meldung, wenn die gewählte Steuerart nicht erlaubt ist (sonst <c>null</c>).</summary>
    public static string? Problem(string voucherType, string taxType, LexwareTaxTypeState state, string voucher) =>
        Options(voucherType, state).FirstOrDefault(o => o.Key == taxType) is { Allowed: false } option
            ? (IsSmallBusiness(state.SmallBusiness, state.ProfileTaxType)
                ? $"Ihr Unternehmen ist in Lexware Office als Kleinunternehmer eingerichtet. Bitte senden Sie {voucher} mit der Steuerart Steuerfrei."
                : LexwareErrors.TaxTypeText(option.Key, voucher))
            : null;

    public static IReadOnlyList<LexwareTaxTypeRejection> ReadRejections(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<LexwareTaxTypeRejection>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string? WriteRejections(IReadOnlyList<LexwareTaxTypeRejection> rejections) =>
        rejections.Count == 0 ? null : JsonSerializer.Serialize(rejections, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static LexwareTaxTypeState State(LexwareSettingsRow row) =>
        new(row.ProfileSmallBusiness, row.ProfileTaxType, row.TaxTypesCheckedAt, ReadRejections(row.RejectedTaxTypes));
}

/// <summary>Erlaubte Steuerarten lesen, Ablehnungen merken und erneut prüfen (Datenbank und <c>GET /v1/profile</c>).</summary>
public static class LexwareTaxTypeStore
{
    /// <summary>Nach dieser Zeit wird das Profil beim Öffnen einer Belegmaske neu gelesen.</summary>
    public static readonly TimeSpan ProfileMaxAge = TimeSpan.FromHours(24);

    /// <summary>Stand für eine Belegmaske; liest das Profil, wenn es noch nie oder vor mehr als einem Tag gelesen wurde (Fehler werden übergangen).</summary>
    public static async Task<LexwareTaxTypeState> StateAsync(LexwareService lexware, CancellationToken cancellationToken)
    {
        var row = await lexware.SettingsAsync(cancellationToken);
        if (row.Configured && (row.TaxTypesCheckedAt is null || row.TaxTypesCheckedAt < lexware.Clock.GetUtcNow() - ProfileMaxAge))
        {
            try
            {
                return await ReadProfileAsync(lexware, row, clearRejections: false, cancellationToken);
            }
            catch (LexwareException)
            {
                // Ohne Profil gelten die gemerkten Ablehnungen; die Maske bleibt nutzbar.
            }
        }

        return LexwareTaxTypes.State(row);
    }

    /// <summary>„Erlaubte Steuerarten erneut prüfen“: Profil lesen und gemerkte Ablehnungen verwerfen.</summary>
    public static async Task<LexwareTaxTypeState> RecheckAsync(LexwareService lexware, CancellationToken cancellationToken)
    {
        var row = await lexware.SettingsAsync(cancellationToken);
        return await ReadProfileAsync(lexware, row, clearRejections: true, cancellationToken);
    }

    private static async Task<LexwareTaxTypeState> ReadProfileAsync(LexwareService lexware, LexwareSettingsRow row, bool clearRejections,
        CancellationToken cancellationToken)
    {
        var profile = await lexware.Client.ProfileAsync(lexware.Connection(row), cancellationToken);
        var now = lexware.Clock.GetUtcNow();
        var taxType = LexwareService.Cut(profile.TaxType, 30);
        // Ohne Versionswechsel: Das Speichern der Einstellungen im Browser soll daran nicht scheitern.
        var query = lexware.Db.Set<LexwareSettingsRow>().Where(r => r.Id == LexwareSettingsRow.SingletonId);
        if (clearRejections)
        {
            await query.ExecuteUpdateAsync(s => s.SetProperty(r => r.ProfileSmallBusiness, profile.SmallBusiness).SetProperty(r => r.ProfileTaxType, taxType)
                .SetProperty(r => r.TaxTypesCheckedAt, now).SetProperty(r => r.RejectedTaxTypes, (string?)null), cancellationToken);
            return new LexwareTaxTypeState(profile.SmallBusiness, taxType, now, []);
        }

        await query.ExecuteUpdateAsync(s => s.SetProperty(r => r.ProfileSmallBusiness, profile.SmallBusiness).SetProperty(r => r.ProfileTaxType, taxType)
            .SetProperty(r => r.TaxTypesCheckedAt, now), cancellationToken);
        return new LexwareTaxTypeState(profile.SmallBusiness, taxType, now, LexwareTaxTypes.ReadRejections(row.RejectedTaxTypes));
    }

    /// <summary>Ablehnung durch Lexware Office merken (je Belegart und Steuerart einmal).</summary>
    public static async Task RememberRejectionAsync(LexwareService lexware, string voucherType, string taxType, CancellationToken cancellationToken)
    {
        var row = await lexware.SettingsAsync(cancellationToken);
        var list = LexwareTaxTypes.ReadRejections(row.RejectedTaxTypes).Where(r => !(r.VoucherType == voucherType && r.TaxType == taxType)).ToList();
        list.Add(new LexwareTaxTypeRejection(voucherType, taxType, lexware.Clock.GetUtcNow()));
        var json = LexwareTaxTypes.WriteRejections(list);
        await lexware.Db.Set<LexwareSettingsRow>().Where(r => r.Id == LexwareSettingsRow.SingletonId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RejectedTaxTypes, json), cancellationToken);
    }
}
