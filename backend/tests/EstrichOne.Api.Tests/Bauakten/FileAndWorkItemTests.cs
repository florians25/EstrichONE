using EstrichOne.Api.Modules.Documentation;
using EstrichOne.Api.Modules.Work;

namespace EstrichOne.Api.Tests.Bauakten;

public sealed class FileAndWorkItemTests
{
    private const string ValidHash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    [Fact]
    public void Announce_NormalizesHashToLowerCase()
    {
        var file = FileRecord.Announce(Guid.CreateVersion7(), null, FileKind.Photo, "IMG_0001.HEIC", "image/heic", 2_048,
            ValidHash.ToUpperInvariant(), Guid.CreateVersion7());

        file.Sha256.ShouldBe(ValidHash);
        file.State.ShouldBe(FileState.AwaitingUpload);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("zz86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")]
    public void Announce_WithInvalidHash_IsRejected(string hash) =>
        Should.Throw<ArgumentException>(() => FileRecord.Announce(Guid.CreateVersion7(), null, FileKind.Document,
            "a.pdf", "application/pdf", 1, hash, Guid.CreateVersion7()));

    [Fact]
    public void ChangeStatus_ToDoneAndBack_SetsAndClearsCompletion()
    {
        var item = WorkItem.Create(Guid.CreateVersion7(), WorkItemKind.Defect, "Riss im Estrich Flur", Guid.CreateVersion7());
        var at = DateTimeOffset.UtcNow;

        item.ChangeStatus(WorkItemStatus.Done, at);
        item.CompletedAt.ShouldBe(at);

        item.ChangeStatus(WorkItemStatus.Open, at.AddHours(1));
        item.CompletedAt.ShouldBeNull();
    }
}
