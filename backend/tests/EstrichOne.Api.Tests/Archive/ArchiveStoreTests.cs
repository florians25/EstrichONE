// Integrationstests laufen unter Linux (CI); kurze Aufrufe ohne Abbruch-Token.
#pragma warning disable xUnit1051, CA1416
using System.Security.Cryptography;
using System.Text;
using EstrichOne.Archive;
using Microsoft.Extensions.Time.Testing;

namespace EstrichOne.Api.Tests.Archive;

/// <summary>WORM-Ablage des Archivservers ohne Netzwerk: Fristen, Löschsperre, Hash-Kette.</summary>
public sealed class ArchiveStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "estrichone-archive-store", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        if (!Directory.Exists(_dir))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        Directory.Delete(_dir, recursive: true);
    }

    private ArchiveStore Store(int minYears = 0) => new(new ArchiveOptions { DataPath = _dir, MinRetentionYears = minYears }, _clock);

    private static (byte[] Content, string Sha) Doc(string text)
    {
        var content = Encoding.UTF8.GetBytes(text);
        return (content, Convert.ToHexStringLower(SHA256.HashData(content)));
    }

    [Fact]
    public void RetentionEndsAtEndOfCalendarYear() =>
        ArchiveStore.RetainUntilFor(new DateOnly(2026, 3, 1), 10).ShouldBe(new DateOnly(2036, 12, 31));

    [Fact]
    public async Task Store_IsIdempotent_DeleteOnlyAfterRetention_LedgerSurvivesRestart()
    {
        using var store = Store();
        var id = Guid.NewGuid().ToString("D");
        var (content, sha) = Doc("Rechnung");
        var description = new ObjectDescription("r.pdf", "application/pdf", "invoice", 6, new DateOnly(2026, 2, 1), null);

        var (created, receipt) = await store.StoreAsync("c1", id, sha, description, new MemoryStream(content), CancellationToken.None);
        created.ShouldBe(StoreOutcome.Created);
        receipt!.RetainUntil.ShouldBe(new DateOnly(2032, 12, 31));
        (await store.StoreAsync("c1", id, sha, description, new MemoryStream(content), CancellationToken.None)).Outcome.ShouldBe(StoreOutcome.AlreadyArchived);
        var (wrong, _) = Doc("anders");
        (await store.StoreAsync("c1", Guid.NewGuid().ToString("D"), sha, description, new MemoryStream(wrong), CancellationToken.None))
            .Outcome.ShouldBe(StoreOutcome.ChecksumMismatch);

        (await store.DeleteAsync("c1", id, null, CancellationToken.None)).Outcome.ShouldBe(DeleteOutcome.RetentionActive);
        await store.ExtendRetentionAsync("c1", id, new DateOnly(2035, 12, 31), "Betriebsprüfung", CancellationToken.None);
        await store.ExtendRetentionAsync("c1", id, new DateOnly(2030, 12, 31), "kürzer geht nicht", CancellationToken.None);
        store.Receipt("c1", id)!.RetainUntil.ShouldBe(new DateOnly(2035, 12, 31));

        _clock.SetUtcNow(new DateTimeOffset(2036, 1, 2, 0, 0, 0, TimeSpan.Zero));
        (await store.DeleteAsync("c1", id, "Frist abgelaufen", CancellationToken.None)).Outcome.ShouldBe(DeleteOutcome.Deleted);

        using var reopened = Store();
        reopened.Receipt("c1", id)!.Deleted.ShouldBeTrue();
        var report = await reopened.VerifyAsync(CancellationToken.None);
        report.Ok.ShouldBeTrue(string.Join("; ", report.Errors));
        report.LedgerEntries.ShouldBe(3); // store, retention (nur die Verlängerung), delete
    }

    [Fact]
    public async Task TamperedLedgerLine_IsDetected()
    {
        using (var store = Store())
        {
            var (content, sha) = Doc("Plan");
            await store.StoreAsync("c1", Guid.NewGuid().ToString("D"), sha, new ObjectDescription("p.pdf", null, "plan", 10, null, null),
                new MemoryStream(content), CancellationToken.None);
        }

        var ledger = Path.Combine(_dir, "ledger.jsonl");
        await File.WriteAllTextAsync(ledger, (await File.ReadAllTextAsync(ledger)).Replace("\"plan\"", "\"other\"", StringComparison.Ordinal));

        using var reopened = Store();
        var report = await reopened.VerifyAsync(CancellationToken.None);
        report.Ok.ShouldBeFalse();
        report.Errors.ShouldContain(e => e.Contains("Hash stimmt nicht", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MinimumRetention_IsEnforced()
    {
        using var store = Store(minYears: 10);
        var (content, sha) = Doc("Angebot");
        (await store.StoreAsync("c1", Guid.NewGuid().ToString("D"), sha, new ObjectDescription("a.pdf", null, "offer", 6, null, null),
            new MemoryStream(content), CancellationToken.None)).Outcome.ShouldBe(StoreOutcome.RetentionTooShort);
    }

    [Fact]
    public void PairingProof_IsSameInSuiteAndArchive()
    {
        var suite = EstrichOne.Api.Infrastructure.Peers.PeerClient.Proof("abcd-efgh-jkmn", "client", new string('a', 64), "n1");
        PairingProof.Compute("ABCDEFGHJKMN", "client", new string('a', 64), "n1").ShouldBe(suite);
        PairingProof.Compute("ABCDEFGHJKMN", "server", new string('a', 64), "n1").ShouldNotBe(suite);
    }
}
