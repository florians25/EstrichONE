using System.Text.Json;
using EstrichOne.Client;
using Windows.Security.Credentials;

namespace EstrichOne.Desktop.Services;

/// <summary>Refresh-Token in der Windows-Anmeldeinformationsverwaltung (verschlüsselt je Benutzerkonto).</summary>
public sealed class PasswordVaultTokenStore : ITokenStore
{
    private const string Resource = "EstrichONE";
    private const string UserName = "refresh-token";

    public string? LoadRefreshToken()
    {
        try
        {
            var credential = new PasswordVault().Retrieve(Resource, UserName);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or KeyNotFoundException)
        {
            return null;
        }
    }

    public void SaveRefreshToken(string? refreshToken)
    {
        var vault = new PasswordVault();
        try
        {
            vault.Remove(vault.Retrieve(Resource, UserName));
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or KeyNotFoundException)
        {
            // Kein gespeichertes Token.
        }

        if (!string.IsNullOrEmpty(refreshToken))
        {
            vault.Add(new PasswordCredential(Resource, UserName, refreshToken));
        }
    }
}

/// <summary>Einstellungen als JSON unter %LOCALAPPDATA%\EstrichONE.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EstrichONE", "desktop.json");
    private readonly Dictionary<string, string?> _values;

    public JsonSettingsStore()
    {
        try
        {
            _values = File.Exists(_path) ? JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(_path)) ?? [] : [];
        }
        catch (JsonException)
        {
            _values = [];
        }
    }

    public string? Read(string key) => _values.GetValueOrDefault(key);

    public void Write(string key, string? value)
    {
        _values[key] = value;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_values));
    }
}
