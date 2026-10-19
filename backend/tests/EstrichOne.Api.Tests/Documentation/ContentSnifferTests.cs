using System.Text;
using EstrichOne.Api.Modules.Documentation;

namespace EstrichOne.Api.Tests.Documentation;

public sealed class ContentSnifferTests
{
    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }, "image/png")]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31 }, "application/pdf")]
    [InlineData(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0 }, "application/zip")]
    public void Detect_RecognizesSignatures(byte[] head, string expected) =>
        ContentSniffer.Detect(head).ShouldBe(expected);

    [Theory]
    [InlineData("heic", "image/heic")]
    [InlineData("qt  ", "video/quicktime")]
    [InlineData("isom", "video/mp4")]
    public void Detect_RecognizesIsoMediaBrands(string brand, string expected) =>
        ContentSniffer.Detect([0, 0, 0, 0x18, .. "ftyp"u8, .. Encoding.ASCII.GetBytes(brand), 0, 0]).ShouldBe(expected);

    [Fact]
    public void Detect_UnknownContent_ReturnsNull() =>
        ContentSniffer.Detect("<html><script>"u8).ShouldBeNull();

    [Theory]
    [InlineData(FileKind.Photo, "application/pdf", false)]
    [InlineData(FileKind.Photo, "image/jpeg", true)]
    [InlineData(FileKind.Video, "image/jpeg", false)]
    [InlineData(FileKind.Document, "application/pdf", true)]
    public void IsAllowed_DependsOnKind(FileKind kind, string detected, bool allowed) =>
        ContentSniffer.IsAllowed(kind, detected).ShouldBe(allowed);
}
