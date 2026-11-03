using System.Text.RegularExpressions;

namespace EstrichOne.Api.Infrastructure.Http;

/// <summary>
/// Geheimnisse, die Teil eines Pfads sind (öffentlicher Abnahme-Link für Kunden), erscheinen nie im Request-Log.
/// Query-Strings protokolliert die Anwendung ohnehin nicht (Serilog-Standard), Tokens stehen in Headern/Cookies.
/// </summary>
public static partial class LogRedaction
{
    public static string Path(string path) => PublicToken().Replace(path, "$1***");

    [GeneratedRegex("(/public/acceptances/)[^/]+", RegexOptions.IgnoreCase)]
    private static partial Regex PublicToken();
}
