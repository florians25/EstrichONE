using System.Net;
using System.Net.Http.Json;
using System.Text;
using EstrichOne.Api.Infrastructure.Text;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Contacts;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Contacts;

public sealed class CsvDocumentTests
{
    [Fact]
    public void Parse_HandlesQuotesSeparatorsAndLineBreaksInFields()
    {
        var rows = CsvDocument.Parse("Name;Notiz\r\n\"Bau; GmbH\";\"Zeile 1\nZeile 2\"\r\n\r\n\"Sagt \"\"Hallo\"\"\";x\n");

        rows.Count.ShouldBe(3);
        rows[1].Fields.ShouldBe(["Bau; GmbH", "Zeile 1\nZeile 2"]);
        rows[2].Line.ShouldBe(5);
        rows[2].Fields[0].ShouldBe("Sagt \"Hallo\"");
    }

    [Fact]
    public void Parse_DetectsCommaSeparator() =>
        CsvDocument.Parse("Name,Ort\nHuber,Landshut")[1].Fields.ShouldBe(["Huber", "Landshut"]);

    [Fact]
    public void Decode_FallsBackToWindows1252ForExcelExports()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(1252).GetBytes("Name\nMüller Straßenbau");

        CsvDocument.Decode(bytes).ShouldBe("Name\nMüller Straßenbau");
    }
}

public sealed class ContactImportTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Csv = """
        Name;Kundennummer;Straße;PLZ;Ort;Telefon;E-Mail;Art
        Bau GmbH;K-100;Industriestr. 1;84028;Landshut;0871 1;info@bau.example;Firma
        Max Huber;;Ringstr. 5;84034;Landshut;;;Person
        ;K-101;;;;;;
        Fehler AG;;;;;;keine-mail;
        Bau GmbH;K-100;;;;;;
        Doppelt intern;K-200;;;;;;
        Doppelt intern;K-200;;;;;;
        """;

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
    public async Task DryRun_ReportsErrorsAndDuplicates_WithoutSaving()
    {
        var result = await ImportAsync(dryRun: true);

        result.TotalRows.ShouldBe(7);
        result.WouldImport.ShouldBe(3);
        result.Imported.ShouldBe(0);
        result.Errors.Select(e => e.Line).ShouldBe([4, 5]);
        result.Duplicates.Select(d => d.Line).ShouldBe([6, 8]);
        (await SearchAsync("")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Import_SavesValidRows_AndSecondRunFindsThemAsDuplicates()
    {
        var first = await ImportAsync(dryRun: false);
        var second = await ImportAsync(dryRun: false);

        first.Imported.ShouldBe(3);
        second.Imported.ShouldBe(0);
        second.Duplicates.Count.ShouldBe(5);
        var found = await SearchAsync("landshut");
        found.Select(c => c.Name).ShouldBe(["Bau GmbH", "Max Huber"]);
    }

    [Fact]
    public async Task Import_WithoutNameColumn_IsRejected()
    {
        using var content = new StringContent("Firma1;Ort\nx;y", Encoding.UTF8, "text/csv");
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");

        using var response = await _office.PostAsync(new Uri("/api/v1/contacts/import?dryRun=true", UriKind.Relative), content, TestContext.Current.CancellationToken);
        var result = await response.Content.ReadFromJsonAsync<ContactImportResult>(TestJson.Options, TestContext.Current.CancellationToken);

        result!.Errors.ShouldHaveSingleItem().Message.ShouldContain("Name");
    }

    [Fact]
    public async Task Import_RequiresEditPermission()
    {
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var content = new StringContent(Csv, Encoding.UTF8, "text/csv");

        using var response = await worker.PostAsync(new Uri("/api/v1/contacts/import?dryRun=false", UriKind.Relative), content, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private async Task<ContactImportResult> ImportAsync(bool dryRun)
    {
        using var content = new StringContent(Csv, Encoding.UTF8, "text/csv");
        using var response = await _office.PostAsync(new Uri($"/api/v1/contacts/import?dryRun={dryRun}", UriKind.Relative), content);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ContactImportResult>(TestJson.Options))!;
    }

    private async Task<List<ContactResponse>> SearchAsync(string search) =>
        (await _office.GetFromJsonAsync<List<ContactResponse>>($"/api/v1/contacts?search={Uri.EscapeDataString(search)}", TestJson.Options))!;
}
