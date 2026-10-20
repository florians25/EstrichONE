using EstrichOne.Hosting;
using EstrichOne.Hosting.Infrastructure;

if (args is ["healthcheck"])
{
    // Healthcheck des Containers ohne curl/wget im Image.
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
    try
    {
        using var response = await client.GetAsync(new Uri("http://127.0.0.1:8080/health"));
        return response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (HttpRequestException)
    {
        return 1;
    }
}

var app = HostingApp.Build(args);
await DataSeeder.InitializeAsync(app.Services, CancellationToken.None);
await app.RunAsync();
return 0;

public partial class Program;
