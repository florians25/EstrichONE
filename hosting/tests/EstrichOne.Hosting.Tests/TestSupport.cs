namespace EstrichOne.Hosting.Tests;

public sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "estrichone-hosting-tests", Guid.NewGuid().ToString("N"));

    public TempDirectory() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Aufräumen ist nicht testrelevant.
        }
    }
}

public static class TestPaths
{
    /// <summary>Repository-Wurzel (enthält global.json).</summary>
    public static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "global.json")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("Repository-Wurzel nicht gefunden.");
        }
    }

    private static readonly Lazy<string> TemplateDirectory = new(() =>
    {
        // Einmal je Testlauf in ein eigenes Verzeichnis kopieren (Tests laufen parallel).
        var target = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "estrichone-hosting-tests", "templates-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        File.Copy(System.IO.Path.Combine(RepositoryRoot, "deploy", "compose.yml"), System.IO.Path.Combine(target, "compose.yml"));
        File.Copy(System.IO.Path.Combine(RepositoryRoot, "deploy", "compose.proxy.yml"), System.IO.Path.Combine(target, "compose.proxy.yml"));
        File.Copy(System.IO.Path.Combine(RepositoryRoot, "docker", "installer", "compose.override.yml"), System.IO.Path.Combine(target, "compose.override.yml"));
        return target;
    });

    /// <summary>Compose-Vorlagen der Suite – genau die Dateien, die auch ins Konsolen-Image kopiert werden.</summary>
    public static string Templates => TemplateDirectory.Value;
}
