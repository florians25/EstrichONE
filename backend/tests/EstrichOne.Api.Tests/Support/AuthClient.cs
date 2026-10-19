using System.Net.Http.Headers;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Identity;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Support;

public static class AuthClient
{
    public const string DefaultPassword = "Estrich-Test-2026!";

    public static async Task<User> CreateUserAsync(this ApiFactory factory, string userName = "m.huber",
        string password = DefaultPassword, bool mustChangePassword = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IUserAccounts>()
            .CreateLocalUserAsync("Max Huber", userName, $"{userName}@example.org", password, mustChangePassword, CancellationToken.None);
    }

    public static Task<HttpResponseMessage> LoginAsync(this HttpClient client, string login, string password = DefaultPassword) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { login, password, client = "ios", clientName = "iPhone Test" });

    public static async Task<TokenResponse> LoginOkAsync(this HttpClient client, string login, string password = DefaultPassword)
    {
        using var response = await client.LoginAsync(login, password);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    public static HttpClient WithToken(this HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}
