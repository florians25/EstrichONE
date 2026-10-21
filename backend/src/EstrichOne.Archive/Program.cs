namespace EstrichOne.Archive;

internal static class EntryPoint
{
    public static async Task<int> Main(string[] args)
    {
        var options = ArchiveOptions.FromEnvironment();
        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "serve";
        if (command != "serve")
        {
            return await Cli.RunAsync(command, [.. args.Skip(1)], options);
        }

        Directory.CreateDirectory(options.DataPath);
        await using var app = ArchiveServer.Build(options, args);
        await app.RunAsync();
        return 0;
    }
}
