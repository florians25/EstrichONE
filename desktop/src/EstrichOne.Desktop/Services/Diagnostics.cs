namespace EstrichOne.Desktop.Services;

/// <summary>Technisches Protokoll unter %LOCALAPPDATA%\EstrichONE\logs – ohne Fachinhalte, für den Helpdesk.</summary>
public static class Diagnostics
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EstrichONE", "logs");

    public static void Log(string message, Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}{(exception is null ? string.Empty : $": {exception.GetType().Name} {exception.Message}")}";
            File.AppendAllText(Path.Combine(Folder, $"desktop-{DateTime.Now:yyyy-MM-dd}.log"), line + Environment.NewLine);
        }
        catch (IOException)
        {
            // Protokoll ist Hilfe, keine Pflicht.
        }
    }
}
