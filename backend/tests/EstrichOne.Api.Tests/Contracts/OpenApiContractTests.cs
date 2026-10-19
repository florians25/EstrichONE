using System.Text.Json;
using System.Text.Json.Nodes;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Contracts;

/// <summary>
/// Die API-Beschreibung ist der Vertrag mit allen Clients (iOS, Android, Web, Desktop).
/// Weicht die erzeugte Beschreibung von <c>contracts/openapi/v1.json</c> ab, schlägt der Test fehl.
/// Bewusste Änderungen übernehmen: <c>ESTRICHONE_UPDATE_CONTRACTS=1 dotnet test</c> und die Datei mit einchecken.
/// </summary>
public sealed class OpenApiContractTests(PostgresFixture postgres)
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Fact]
    public async Task OpenApiDocument_MatchesCheckedInContract()
    {
        await using var factory = new ApiFactory(await postgres.CreateDatabaseAsync());
        using var client = factory.CreateClient();

        var json = await client.GetStringAsync(new Uri("/api/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);
        var actual = Normalize(json);
        var contractPath = Path.Combine(RepositoryRoot(), "contracts", "openapi", "v1.json");

        if (Environment.GetEnvironmentVariable("ESTRICHONE_UPDATE_CONTRACTS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(contractPath)!);
            await File.WriteAllTextAsync(contractPath, actual + "\n", TestContext.Current.CancellationToken);
        }

        File.Exists(contractPath).ShouldBeTrue($"Vertrag fehlt: {contractPath}");
        var expected = (await File.ReadAllTextAsync(contractPath, TestContext.Current.CancellationToken)).TrimEnd();
        actual.ShouldBe(expected, "Die API weicht vom eingecheckten Vertrag ab. Bewusste Änderung? ESTRICHONE_UPDATE_CONTRACTS=1 dotnet test");
    }

    /// <summary>Entfernt umgebungsabhängige Angaben (Serveradresse).</summary>
    private static string Normalize(string json)
    {
        var document = JsonNode.Parse(json)!.AsObject();
        document.Remove("servers");
        return document.ToJsonString(Indented);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository-Wurzel (global.json) nicht gefunden.");
    }
}
