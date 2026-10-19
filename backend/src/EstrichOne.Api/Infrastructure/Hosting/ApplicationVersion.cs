using System.Reflection;

namespace EstrichOne.Api.Infrastructure.Hosting;

public static class ApplicationVersion
{
    /// <summary>Semantische Version ohne Build-Metadaten, z. B. <c>0.1.0</c>.</summary>
    public static string Current { get; } = Read();

    private static string Read()
    {
        var informational = typeof(ApplicationVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }
}
