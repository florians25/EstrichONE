using System.Globalization;
using System.Text.Json;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Lexware;

/// <summary>Felder, die zwischen „Artikel und Leistungen“ und den Artikeln in Lexware Office abgeglichen werden.</summary>
public static class LexwareArticleFields
{
    public const string Title = "title";
    public const string Description = "description";
    public const string Kind = "type";
    public const string ArticleNumber = "articleNumber";
    public const string Gtin = "gtin";
    public const string Unit = "unitName";
    public const string NetPrice = "netPrice";
    public const string TaxRate = "taxRate";

    public static IReadOnlyList<string> All { get; } = [Title, Description, Kind, ArticleNumber, Gtin, Unit, NetPrice, TaxRate];

    public static string Label(string field) => field switch
    {
        Title => "Bezeichnung",
        Description => "Beschreibung",
        Kind => "Art",
        ArticleNumber => "Artikelnummer",
        Gtin => "GTIN",
        Unit => "Einheit",
        NetPrice => "Preis netto",
        TaxRate => "Steuersatz",
        _ => field,
    };
}

/// <summary>Möglicher Treffer zwischen einem Eintrag in EstrichONE und einem Artikel in Lexware Office.</summary>
/// <param name="Reasons">„gleiche Artikelnummer“, „gleiche Bezeichnung“.</param>
public sealed record LexwareArticleMatch(Guid CatalogItemId, string LexwareId, IReadOnlyList<string> Reasons);

/// <summary>
/// Artikelabgleich als reine Funktionen: Feldwerte beider Seiten, Dublettenprüfung über Artikelnummer und Bezeichnung und
/// Dreiwege-Abgleich gegen den zuletzt abgeglichenen Stand (gleiche Regeln wie beim Kundenabgleich). Kein Netz, keine Datenbank.
/// </summary>
public static class LexwareArticleRules
{
    public static string Kind(CatalogItemKind kind) => kind == CatalogItemKind.Product ? "PRODUCT" : "SERVICE";

    public static CatalogItemKind Kind(string? type) => type == "PRODUCT" ? CatalogItemKind.Product : CatalogItemKind.Service;

    public static string Number(decimal? value) => value is { } v ? v.ToString("0.####", CultureInfo.InvariantCulture) : string.Empty;

    public static Dictionary<string, string?> Local(CatalogItem item) => new(StringComparer.Ordinal)
    {
        [LexwareArticleFields.Title] = LexwareContactSync.Clean(item.Title),
        [LexwareArticleFields.Description] = LexwareContactSync.Clean(item.Description),
        [LexwareArticleFields.Kind] = Kind(item.Kind),
        [LexwareArticleFields.ArticleNumber] = LexwareContactSync.Clean(item.ArticleNumber),
        [LexwareArticleFields.Gtin] = LexwareContactSync.Clean(item.Gtin),
        [LexwareArticleFields.Unit] = LexwareContactSync.Clean(item.Unit),
        [LexwareArticleFields.NetPrice] = Number(item.NetPrice),
        [LexwareArticleFields.TaxRate] = Number(item.TaxRate),
    };

    public static Dictionary<string, string?> Remote(LexwareArticle article) => new(StringComparer.Ordinal)
    {
        [LexwareArticleFields.Title] = LexwareContactSync.Clean(article.Title),
        [LexwareArticleFields.Description] = LexwareContactSync.Clean(article.Description),
        [LexwareArticleFields.Kind] = article.Type,
        [LexwareArticleFields.ArticleNumber] = LexwareContactSync.Clean(article.ArticleNumber),
        [LexwareArticleFields.Gtin] = LexwareContactSync.Clean(article.Gtin),
        [LexwareArticleFields.Unit] = LexwareContactSync.Clean(article.UnitName),
        [LexwareArticleFields.NetPrice] = Number(article.NetPrice ?? 0m),
        [LexwareArticleFields.TaxRate] = Number(article.TaxRate ?? 0m),
    };

    /// <summary>
    /// Dreiwege-Abgleich je Feld. Gleiche Werte: nichts zu tun. Nur eine Seite hat seit dem letzten Abgleich geändert: ihr
    /// Wert gilt, wenn die Richtung das zulässt (nach Lexware Office nur bei <see cref="LexwareArticleSync.Both"/>). Beide
    /// geändert, kein abgeglichener Stand oder Richtung gesperrt: Abweichung, überschrieben wird nichts. Die Bezeichnung
    /// wird nie durch einen leeren Wert ersetzt. <paramref name="choices"/> (Feld auf <c>estrichOne</c> bzw. <c>lexware</c>)
    /// sind Entscheidungen des Nutzers; nach Lexware Office gehen sie nur, wenn die Richtung es erlaubt.
    /// </summary>
    public static LexwareMergeResult Merge(IReadOnlyDictionary<string, string?>? baseline, IReadOnlyDictionary<string, string?> local,
        IReadOnlyDictionary<string, string?> remote, LexwareArticleSync direction, IReadOnlyDictionary<string, string>? choices = null)
    {
        var next = new Dictionary<string, string?>(StringComparer.Ordinal);
        var toLexware = new Dictionary<string, string?>(StringComparer.Ordinal);
        var toEstrichOne = new Dictionary<string, string?>(StringComparer.Ordinal);
        var deviations = new List<LexwareFieldDeviation>();
        var push = direction == LexwareArticleSync.Both;
        var pull = direction is LexwareArticleSync.Both or LexwareArticleSync.FromLexware;
        foreach (var field in LexwareArticleFields.All)
        {
            var mine = LexwareContactSync.Clean(local.GetValueOrDefault(field));
            var theirs = LexwareContactSync.Clean(remote.GetValueOrDefault(field));
            if (mine == theirs)
            {
                next[field] = mine;
                continue;
            }

            var known = baseline is not null && baseline.ContainsKey(field);
            var before = known ? LexwareContactSync.Clean(baseline![field]) : null;
            var localChanged = !known || mine != before;
            var remoteChanged = !known || theirs != before;
            var choice = choices?.GetValueOrDefault(field);
            var keepsTitle = field != LexwareArticleFields.Title;
            if (choice == LexwareContactSync.EstrichOne && push && (keepsTitle || mine is not null))
            {
                toLexware[field] = mine;
                next[field] = mine;
            }
            else if (choice == LexwareContactSync.Lexware && (keepsTitle || theirs is not null))
            {
                toEstrichOne[field] = theirs;
                next[field] = theirs;
            }
            else if (known && push && localChanged && !remoteChanged && (keepsTitle || mine is not null))
            {
                toLexware[field] = mine;
                next[field] = mine;
            }
            else if (known && pull && remoteChanged && !localChanged && (keepsTitle || theirs is not null))
            {
                toEstrichOne[field] = theirs;
                next[field] = theirs;
            }
            else
            {
                deviations.Add(new LexwareFieldDeviation(field, mine, theirs));
                if (known)
                {
                    next[field] = before;
                }
            }
        }

        return new LexwareMergeResult(next, toLexware, toEstrichOne, deviations);
    }

    /// <summary>Werte aus Lexware Office in den Eintrag übernehmen.</summary>
    public static void Apply(CatalogItem item, IReadOnlyDictionary<string, string?> values)
    {
        foreach (var (field, value) in values)
        {
            switch (field)
            {
                case LexwareArticleFields.Title when value is not null:
                    item.Title = Cut(value, CatalogItem.MaxTitle)!;
                    break;
                case LexwareArticleFields.Description:
                    item.Description = Cut(value, CatalogItem.MaxDescription);
                    break;
                case LexwareArticleFields.Kind:
                    item.Kind = Kind(value);
                    break;
                case LexwareArticleFields.ArticleNumber:
                    item.ArticleNumber = Cut(value, 50);
                    break;
                case LexwareArticleFields.Gtin:
                    item.Gtin = Cut(value, 20);
                    break;
                case LexwareArticleFields.Unit:
                    item.Unit = Cut(value, 50);
                    break;
                case LexwareArticleFields.NetPrice:
                    item.NetPrice = Parse(value) ?? 0m;
                    break;
                case LexwareArticleFields.TaxRate:
                    item.TaxRate = Parse(value) ?? 0m;
                    break;
            }
        }
    }

    /// <summary>Werte für Lexware Office: der gelesene Artikel, überschrieben mit den Feldern aus EstrichONE.</summary>
    public static LexwareArticleDraft Draft(LexwareArticle current, IReadOnlyDictionary<string, string?> changes)
    {
        string? Value(string field, string? fallback) => changes.TryGetValue(field, out var value) ? value : fallback;
        return new LexwareArticleDraft(Value(LexwareArticleFields.Title, current.Title) ?? current.Title, Value(LexwareArticleFields.Description, current.Description),
            Value(LexwareArticleFields.Kind, current.Type) ?? current.Type, Value(LexwareArticleFields.ArticleNumber, current.ArticleNumber),
            Value(LexwareArticleFields.Gtin, current.Gtin), Value(LexwareArticleFields.Unit, current.UnitName),
            Parse(Value(LexwareArticleFields.NetPrice, Number(current.NetPrice ?? 0m))) ?? 0m, Parse(Value(LexwareArticleFields.TaxRate, Number(current.TaxRate ?? 0m))) ?? 0m);
    }

    public static LexwareArticleDraft Draft(CatalogItem item) =>
        new(item.Title, item.Description, Kind(item.Kind), item.ArticleNumber, item.Gtin, item.Unit, item.NetPrice, item.TaxRate);

    /// <summary>Neuer Eintrag aus einem Lexware-Artikel.</summary>
    public static CatalogItem Import(LexwareArticle article, Guid id)
    {
        var item = new CatalogItem { Id = id, Origin = CatalogItem.OriginLexware, LexwareId = article.Id, LexwareVersion = article.Version };
        Apply(item, Remote(article));
        item.GrossPrice = article.GrossPrice;
        return item;
    }

    /// <summary>Bezeichnung für den Vergleich: ohne Groß- und Kleinschreibung, Satzzeichen und Mehrfachleerzeichen.</summary>
    public static string Normalize(string? title) =>
        string.Join(' ', new string((title ?? string.Empty).ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Gründe, aus denen ein Eintrag und ein Lexware-Artikel derselbe Artikel sein könnten (leer = kein Treffer).</summary>
    public static List<string> Reasons(CatalogItem item, LexwareArticle article)
    {
        var reasons = new List<string>();
        if (LexwareContactSync.Clean(item.ArticleNumber) is { } number && string.Equals(number, LexwareContactSync.Clean(article.ArticleNumber), StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("gleiche Artikelnummer");
        }

        if (Normalize(item.Title) is { Length: > 0 } title && title == Normalize(article.Title))
        {
            reasons.Add("gleiche Bezeichnung");
        }

        return reasons;
    }

    public static decimal? Parse(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static string? Cut(string? value, int length) => LexwareService.Cut(value, length);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value);

    public static Dictionary<string, string?>? Baseline(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<Dictionary<string, string?>>(json);

    public static List<LexwareFieldDeviation> Deviations(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<LexwareFieldDeviation>>(json) ?? [];
}

/// <summary>Ergebnis eines Laufs des Artikelabgleichs.</summary>
/// <param name="Active">Module eingeschaltet, Schlüssel hinterlegt und Richtung nicht aus.</param>
/// <param name="Imported">Aus Lexware Office neu übernommene Artikel.</param>
/// <param name="Created">In Lexware Office neu angelegte Artikel (nur Richtung „beide“).</param>
/// <param name="Updated">Einträge, deren Werte geändert wurden (in EstrichONE oder in Lexware Office).</param>
/// <param name="Duplicates">Artikel, die wegen eines möglichen Treffers nicht automatisch übernommen bzw. angelegt wurden.</param>
/// <param name="Error">Lexware Office war nicht erreichbar oder hat abgelehnt; der Lauf wurde abgebrochen.</param>
public sealed record LexwareArticleSyncRun(bool Active, int Imported, int Created, int Updated, int Deviations, int Duplicates, string? Error);

/// <summary>
/// Artikelabgleich zwischen „Artikel und Leistungen“ und Lexware Office. Richtung einstellbar (<see cref="LexwareArticleSync"/>):
/// Vorgabe ist die Übernahme aus Lexware Office. Artikel ohne möglichen Treffer werden übernommen bzw. (nur bei „beide“)
/// angelegt; bei Treffern über Artikelnummer oder Bezeichnung entscheidet der Nutzer. Verknüpfte Einträge werden je Feld
/// gegen den zuletzt abgeglichenen Stand abgeglichen. Gelöscht wird nie, weder in EstrichONE noch in Lexware Office.
/// </summary>
public sealed class LexwareArticleSyncService(LexwareService lexware, IAuditLog audit, ISystemSettings system, ILogger<LexwareArticleSyncService> logger)
{
    /// <summary>Eingeschaltet: Module „Lexware Office“ und „Lexware Office: Artikel“, Schlüssel hinterlegt, Richtung nicht aus.</summary>
    public async Task<bool> ActiveAsync(LexwareSettingsRow row, CancellationToken cancellationToken)
    {
        var modules = (await system.GetAsync(cancellationToken)).Modules;
        return modules.IsEnabled(AppModules.Lexware) && modules.IsEnabled(AppModules.LexwareArticles) && row.Configured && row.ArticleSync != LexwareArticleSync.Off;
    }

    /// <summary>Alle Artikel aus Lexware Office (alle Seiten), kurz zwischengespeichert.</summary>
    public Task<(IReadOnlyList<LexwareArticle> Articles, DateTimeOffset LoadedAt)> RemoteAsync(LexwareSettingsRow row, bool refresh, CancellationToken cancellationToken) =>
        lexware.ArticlesAsync(row, refresh, cancellationToken);

    /// <summary>
    /// Ein Lauf über alle Artikel. Ein Lauf zur selben Zeit je Server. <paramref name="actor"/> ist der Benutzer bei
    /// „Jetzt abgleichen“, sonst der Hintergrunddienst.
    /// </summary>
    public async Task<LexwareArticleSyncRun> RunAsync(Guid? actor, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        var row = await lexware.SettingsAsync(cancellationToken);
        if (!await ActiveAsync(row, cancellationToken))
        {
            return new LexwareArticleSyncRun(false, 0, 0, 0, 0, 0, null);
        }

        var gate = lexware.ArticleGate;
        await gate.WaitAsync(cancellationToken);
        int imported = 0, created = 0, updated = 0, duplicates = 0;
        try
        {
            var connection = lexware.Connection(row);
            var (remote, _) = await lexware.ArticlesAsync(row, refresh: true, cancellationToken);
            var byId = remote.ToDictionary(a => a.Id, StringComparer.Ordinal);
            var items = await db.Set<CatalogItem>().ToListAsync(cancellationToken);
            var direction = row.ArticleSync;
            var now = lexware.Clock.GetUtcNow();

            // Verknüpfte Einträge.
            foreach (var item in items.Where(i => i.LexwareId is not null))
            {
                if (!byId.TryGetValue(item.LexwareId!, out var article))
                {
                    if (item.RemoteState != "missing")
                    {
                        item.RemoteState = "missing";
                        item.Deviations = null;
                        item.DeviationCount = 0;
                    }

                    continue;
                }

                try
                {
                    if (await SyncItemAsync(row, connection, item, article, direction, null, cancellationToken))
                    {
                        updated++;
                    }
                }
                catch (LexwareException exception) when (!LexwareChangeSyncService.Global(exception))
                {
                    item.SyncError = LexwareService.Cut(exception.Message, 2000);
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            // Nur in Lexware Office: übernehmen, wenn es keinen möglichen Treffer gibt.
            var linked = items.Where(i => i.LexwareId is not null).Select(i => i.LexwareId!).ToHashSet(StringComparer.Ordinal);
            var unlinkedLocal = items.Where(i => i.LexwareId is null).ToList();
            foreach (var article in remote.Where(a => !a.Archived && !linked.Contains(a.Id)))
            {
                if (unlinkedLocal.Any(i => LexwareArticleRules.Reasons(i, article).Count > 0))
                {
                    duplicates++;
                    continue;
                }

                var item = LexwareArticleRules.Import(article, Guid.CreateVersion7());
                Settle(item, LexwareArticleRules.Remote(article), article, now);
                db.Add(item);
                imported++;
            }

            await db.SaveChangesAsync(cancellationToken);

            // Nur in EstrichONE: in Lexware Office anlegen (nur „beide“), wenn es dort keinen möglichen Treffer gibt.
            if (direction == LexwareArticleSync.Both)
            {
                var unlinkedRemote = remote.Where(a => !linked.Contains(a.Id)).ToList();
                foreach (var item in unlinkedLocal.Where(i => i.IsActive))
                {
                    if (unlinkedRemote.Any(a => LexwareArticleRules.Reasons(item, a).Count > 0))
                    {
                        duplicates++;
                        continue;
                    }

                    try
                    {
                        await CreateInLexwareAsync(row, connection, item, actor, cancellationToken);
                        created++;
                    }
                    catch (LexwareException exception) when (!LexwareChangeSyncService.Global(exception))
                    {
                        item.SyncError = LexwareService.Cut(exception.Message, 2000);
                        await db.SaveChangesAsync(cancellationToken);
                    }
                }
            }

            await db.Set<LexwareSettingsRow>().ExecuteUpdateAsync(s => s.SetProperty(r => r.LastArticleSyncAt, now)
                .SetProperty(r => r.LastArticleSyncError, (string?)null), cancellationToken);
            if (imported + created > 0)
            {
                audit.Record("lexware.articles_synced", actor, "settings", "lexware", new Dictionary<string, string?>
                {
                    ["imported"] = imported.ToString(CultureInfo.InvariantCulture), ["created"] = created.ToString(CultureInfo.InvariantCulture),
                    ["auto"] = actor is null ? "true" : null,
                });
                await db.SaveChangesAsync(cancellationToken);
            }

            var deviations = await db.Set<CatalogItem>().CountAsync(i => i.DeviationCount > 0, cancellationToken);
            return new LexwareArticleSyncRun(true, imported, created, updated, deviations, duplicates, null);
        }
        catch (LexwareException exception)
        {
            logger.LogWarning("Lexware-Artikelabgleich abgebrochen: {Code}", exception.Code);
            var message = LexwareService.Cut(exception.Message, 2000);
            db.ChangeTracker.Clear();
            await db.Set<LexwareSettingsRow>().ExecuteUpdateAsync(s => s.SetProperty(r => r.LastArticleSyncError, message), cancellationToken);
            return new LexwareArticleSyncRun(true, imported, created, updated, 0, duplicates, exception.Message);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Einen verknüpften Eintrag abgleichen. Geschrieben wird in Lexware Office nur bei Richtung „beide“ und immer erst nach
    /// frischem Lesen mit Version. Liefert, ob Werte übernommen oder übertragen wurden.
    /// </summary>
    public async Task<bool> SyncItemAsync(LexwareSettingsRow row, LexwareConnection connection, CatalogItem item, LexwareArticle article,
        LexwareArticleSync direction, IReadOnlyDictionary<string, string>? choices, CancellationToken cancellationToken)
    {
        var baseline = LexwareArticleRules.Baseline(item.SyncedFields);
        var local = LexwareArticleRules.Local(item);
        var merge = LexwareArticleRules.Merge(baseline, local, LexwareArticleRules.Remote(article), direction, choices);
        var current = article;
        var changed = false;
        if (merge.ToLexware.Count > 0)
        {
            LexwareMergeResult? fresh = null;
            var (written, wrote) = await lexware.Client.UpdateArticleAsync(connection, item.LexwareId!, latest =>
            {
                fresh = LexwareArticleRules.Merge(baseline, local, LexwareArticleRules.Remote(latest), direction, choices);
                return fresh.ToLexware.Count == 0 ? null : LexwareArticleRules.Draft(latest, fresh.ToLexware);
            }, cancellationToken);
            current = written;
            merge = fresh ?? merge;
            if (wrote)
            {
                changed = true;
                audit.Record("lexware.article_synced", null, "catalog_item", item.Id.ToString(), new Dictionary<string, string?>
                {
                    ["title"] = item.Title, ["direction"] = "EstrichONE nach Lexware Office",
                    ["fields"] = string.Join(", ", merge.ToLexware.Keys.Select(LexwareArticleFields.Label)),
                });
            }
        }

        if (merge.ToEstrichOne.Count > 0)
        {
            LexwareArticleRules.Apply(item, merge.ToEstrichOne);
            changed = true;
        }

        if (current.Archived)
        {
            item.IsActive = false;
        }

        // Nur tatsächlich Geändertes setzen: Ein Lauf ohne Unterschiede erhöht die Version des Eintrags nicht.
        var baselineJson = LexwareArticleRules.Serialize(merge.Baseline);
        var deviationsJson = merge.Deviations.Count == 0 ? null : LexwareArticleRules.Serialize(merge.Deviations);
        var settled = item.SyncedFields != baselineJson || item.Deviations != deviationsJson || item.LexwareVersion != current.Version
            || item.RemoteState != (current.Archived ? "archived" : null) || item.SyncError is not null || item.SyncedAt is null;
        if (current.GrossPrice is { } gross && item.GrossPrice != gross)
        {
            item.GrossPrice = gross;
        }

        if (changed || settled)
        {
            item.SyncedFields = baselineJson;
            item.Deviations = deviationsJson;
            item.DeviationCount = merge.Deviations.Count;
            item.LexwareVersion = current.Version;
            item.RemoteState = current.Archived ? "archived" : null;
            item.SyncError = null;
            item.SyncedAt = lexware.Clock.GetUtcNow();
        }

        return changed;
    }

    /// <summary>Eintrag in Lexware Office anlegen und verknüpfen.</summary>
    public async Task CreateInLexwareAsync(LexwareSettingsRow row, LexwareConnection connection, CatalogItem item, Guid? actor, CancellationToken cancellationToken)
    {
        var created = await lexware.Client.CreateArticleAsync(connection, LexwareArticleRules.Draft(item), cancellationToken);
        item.LexwareId = created.Id;
        item.LexwareVersion = created.Version;
        Settle(item, LexwareArticleRules.Local(item), null, lexware.Clock.GetUtcNow());
        lexware.ForgetArticles();
        audit.Record("lexware.article_created", actor, "catalog_item", item.Id.ToString(), new Dictionary<string, string?> { ["title"] = item.Title });
        await lexware.Db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Abgeglichener Stand nach Übernahme bzw. Anlage: beide Seiten gleich.</summary>
    public static void Settle(CatalogItem item, IReadOnlyDictionary<string, string?> values, LexwareArticle? article, DateTimeOffset now)
    {
        item.SyncedFields = LexwareArticleRules.Serialize(values);
        item.Deviations = null;
        item.DeviationCount = 0;
        item.RemoteState = null;
        item.SyncError = null;
        item.SyncedAt = now;
        if (article is not null)
        {
            item.LexwareVersion = article.Version;
            item.GrossPrice = article.GrossPrice;
        }
    }
}
