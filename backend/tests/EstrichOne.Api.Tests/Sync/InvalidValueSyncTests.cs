using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Sync.Exports;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Sync;

/// <summary>
/// Nicht lesbare Feldwerte (unbekannte Auswahlwerte, falscher JSON-Typ) lehnen genau die betroffene Operation mit
/// VALIDATION_FAILED ab; die Anfrage bleibt 200 und alle übrigen Operationen werden normal verarbeitet.
/// </summary>
public sealed class InvalidValueSyncTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _office = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateMigratedAsync(postgres);
        _office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
    }

    public async ValueTask DisposeAsync()
    {
        _office.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task InvalidFieldValues_RejectOnlyTheAffectedOperation_ForEveryOperationType()
    {
        var bauakte = Guid.CreateVersion7();
        var task = Guid.CreateVersion7();
        var contact = Guid.CreateVersion7();
        var room = Guid.CreateVersion7();
        var file = Guid.CreateVersion7();
        var setup = (await _office.PushAsync(
            SyncClient.Op("bauakte.create", bauakte, new { title = "Ungültige Werte" }),
            SyncClient.Op("workitem.create", task, new { bauakteId = bauakte, kind = "task", title = "Aufgabe" }),
            SyncClient.Op("contact.create", contact, new { name = "Kunde" }),
            SyncClient.Op("room.create", room, new { bauakteId = bauakte, name = "Flur" }),
            SyncClient.Op("file.announce", file, new { bauakteId = bauakte, kind = "document", originalName = "a.pdf",
                contentType = "application/pdf", sizeBytes = 10, sha256 = new string('b', 64) }))).Results;
        setup.Select(r => r.Status).ShouldAllBe(status => status == SyncOperationStatus.Applied);

        static object Update(string field, object? value, object? baseValue) =>
            new { changes = new Dictionary<string, object?> { [field] = value }, @base = new Dictionary<string, object?> { [field] = baseValue } };

        (string Type, Guid Id, object Payload, string Field)[] invalid =
        [
            ("bauakte.create", Guid.CreateVersion7(), new { title = "X", latitude = "nördlich" }, "latitude"),
            ("bauakte.create", Guid.CreateVersion7(), new { title = "X", plannedStart = "morgen" }, "plannedStart"),
            ("bauakte.update", bauakte, Update("status", "explodiert", "planned"), "status"),
            ("bauakte.update", bauakte, Update("latitude", "weit", null), "latitude"),
            ("bauakte.update", bauakte, Update("city", 42, null), "city"),
            ("bauakte.update", bauakte, new { changes = new[] { 1 } }, "changes"),
            ("bauakte.setStatus", bauakte, new { status = "kaputt", baseStatus = "planned" }, "status"),
            ("note.create", Guid.CreateVersion7(), new { bauakteId = "keine-uuid", text = "Hallo" }, "bauakteId"),
            ("workitem.create", Guid.CreateVersion7(), new { bauakteId = bauakte, kind = "bug", title = "Fehler" }, "kind"),
            ("workitem.update", task, Update("status", "fertig", "open"), "status"),
            ("workitem.update", task, Update("dueDate", "bald", null), "dueDate"),
            ("contact.create", Guid.CreateVersion7(), new { name = "Firma", kind = "firma" }, "kind"),
            ("contact.update", contact, Update("kind", 1, "person"), "kind"),
            ("contact.update", contact, Update("name", true, "Kunde"), "name"),
            ("room.create", Guid.CreateVersion7(), new { bauakteId = bauakte, name = "Bad", status = "nass" }, "status"),
            ("room.update", room, Update("areaSquareMeters", "groß", null), "areaSquareMeters"),
            ("file.announce", Guid.CreateVersion7(), new { bauakteId = bauakte, kind = "hologramm", originalName = "a.bin",
                contentType = "application/octet-stream", sizeBytes = 10, sha256 = new string('c', 64) }, "kind"),
            ("file.update", file, Update("caption", 5, null), "caption"),
            ("diary.create", Guid.CreateVersion7(), new { bauakteId = bauakte, text = "Tag", weather = "regenbogen" }, "weather"),
            ("inspection.create", Guid.CreateVersion7(), new { bauakteId = bauakte, kind = "zauberei" }, "kind"),
            ("acceptance.create", Guid.CreateVersion7(), new { bauakteId = bauakte, result = "vielleicht" }, "result"),
            ("bauakteParty.save", Guid.CreateVersion7(), new { bauakteId = bauakte, role = "zuschauer", name = "Max" }, "role"),
            ("ticket.create", Guid.CreateVersion7(), new { title = "Problem", category = "weltuntergang" }, "category"),
        ];

        var valid = Guid.CreateVersion7();
        var operations = invalid.Select(op => SyncClient.Op(op.Type, op.Id, op.Payload))
            .Append(SyncClient.Op("bauakte.create", valid, new { title = "Gültig trotz Nachbarn" })).ToArray();
        var results = (await _office.PushAsync(operations)).Results;

        results.Count.ShouldBe(invalid.Length + 1);
        for (var i = 0; i < invalid.Length; i++)
        {
            var (type, _, _, field) = invalid[i];
            results[i].Status.ShouldBe(SyncOperationStatus.Rejected, $"{type} ({field}): {results[i].Code} {results[i].Message}");
            results[i].Code.ShouldBe("VALIDATION_FAILED", $"{type} ({field}): {results[i].Message}");
            results[i].Message!.ShouldContain(field, Case.Sensitive, $"{type}: {results[i].Message}");
        }

        results[^1].Status.ShouldBe(SyncOperationStatus.Applied);

        // Unbekannte Auswahlwerte nennen die erlaubten Werte.
        results[2].Message!.ShouldContain("inProgress");

        // Abgelehnt ist endgültig: erneutes Senden liefert dasselbe Ergebnis, ohne 500.
        var again = (await _office.PushAsync(SyncClient.Op(invalid[2].Type, invalid[2].Id, invalid[2].Payload, results[2].OpId))).Results.Single();
        again.Code.ShouldBe("VALIDATION_FAILED");
    }
}
