#pragma warning disable xUnit1051
using System.Security.Cryptography;
using EstrichOne.Archive;
using Microsoft.Extensions.Time.Testing;

namespace EstrichOne.Api.Tests.Offload;

/// <summary>Ablage des Storage (Rolle offload): Prüfsumme vor dem Ablegen, kein Überschreiben, Papierkorb mit Frist, Prüflauf.</summary>
public sealed class OffloadStoreTests : IDisposable
{
    private readonly string _data = Path.Combine(Path.GetTempPath(), "estrichone-offload-store", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly OffloadStore _store;

    public OffloadStoreTests()
    {
        Directory.CreateDirectory(_data);
        _store = new OffloadStore(new ArchiveOptions { DataPath = _data, Role = ServerRole.Offload, OffloadTrashDays = 30 }, _clock);
    }

    public void Dispose()
    {
        _store.Dispose();
        Directory.Delete(_data, recursive: true);
    }

    [Fact]
    public async Task Put_VerifiesChecksum_IsIdempotent_RefusesOtherContent()
    {
        var id = OffloadStore.NormalizeId(Guid.NewGuid().ToString())!;
        var content = RandomNumberGenerator.GetBytes(5000);
        var sha = Convert.ToHexStringLower(SHA256.HashData(content));

        (await _store.PutAsync("client", id, new string('0', 64), content.Length, new MemoryStream(content), CancellationToken.None)).Outcome
            .ShouldBe(OffloadPutOutcome.ChecksumMismatch);
        _store.Info("client", id).ShouldBeNull();

        var (created, info) = await _store.PutAsync("client", id, sha, content.Length, new MemoryStream(content), CancellationToken.None);
        created.ShouldBe(OffloadPutOutcome.Created);
        info!.Size.ShouldBe(5000);
        (await _store.PutAsync("client", id, sha, content.Length, new MemoryStream(content), CancellationToken.None)).Outcome.ShouldBe(OffloadPutOutcome.AlreadyStored);
        var other = RandomNumberGenerator.GetBytes(10);
        (await _store.PutAsync("client", id, Convert.ToHexStringLower(SHA256.HashData(other)), other.Length, new MemoryStream(other), CancellationToken.None)).Outcome
            .ShouldBe(OffloadPutOutcome.Conflict);

        // Andere Kopplung sieht das Objekt nicht.
        _store.Info("anderer", id).ShouldBeNull();
        _store.Open("anderer", id).ShouldBeNull();
        OffloadStore.NormalizeId("../../etc/passwd").ShouldBeNull();
    }

    [Fact]
    public async Task Delete_MovesToTrash_UndeleteRestores_PurgeAfterRetention_VerifyFindsCorruption()
    {
        var id = OffloadStore.NormalizeId(Guid.NewGuid().ToString())!;
        var content = RandomNumberGenerator.GetBytes(100);
        await _store.PutAsync("c", id, Convert.ToHexStringLower(SHA256.HashData(content)), null, new MemoryStream(content), CancellationToken.None);

        _store.Delete("c", id).ShouldBeTrue();
        _store.Info("c", id)!.Deleted.ShouldBeTrue();
        await using (var stream = _store.Open("c", id)!)
        {
            stream.ShouldNotBeNull(); // auch aus dem Papierkorb lesbar
        }

        _store.Statistics("c").TrashObjects.ShouldBe(1);
        _store.Undelete("c", id).ShouldBeTrue();
        _store.Info("c", id)!.Deleted.ShouldBeFalse();

        (await _store.VerifyAsync(CancellationToken.None)).Ok.ShouldBeTrue();
        var file = Directory.EnumerateFiles(_data, id, SearchOption.AllDirectories).Single();
        var bytes = await File.ReadAllBytesAsync(file);
        bytes[0] ^= 0xFF;
        await File.WriteAllBytesAsync(file, bytes);
        var report = await _store.VerifyAsync(CancellationToken.None);
        report.Ok.ShouldBeFalse();
        _store.LatestVerification()!.Errors.Count.ShouldBe(1);

        _store.Delete("c", id).ShouldBeTrue();
        _store.Purge().ShouldBe(0);
        _clock.Advance(TimeSpan.FromDays(31));
        _store.Purge().ShouldBe(1);
        _store.Info("c", id).ShouldBeNull();
    }
}
