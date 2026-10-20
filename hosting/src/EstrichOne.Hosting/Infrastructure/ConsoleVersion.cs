using System.Reflection;

namespace EstrichOne.Hosting.Infrastructure;

public static class ConsoleVersion
{
    public static readonly string Current =
        typeof(ConsoleVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";
}
