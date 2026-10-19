using System.Net.Http.Headers;
using System.Net.Http.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Identity;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Support;

public static class AuthClient
{
    public const string DefaultPassword = "Estrich-Test-2026!";

    public static async Task<User> CreateUserAsync(this ApiFactory factory, string userName = "m.huber",
        string password = DefaultPassword, bool mustChangePassword = false, params Guid[] roleIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<IUserAccounts>()
            .CreateLocalUserAsync("Max Huber", userName, $"{userName}@example.org", password, mustChangePassword, CancellationToken.None);
        if (roleIds.Length > 0)
        {
            await scope.ServiceProvider.GetRequiredService<IRoleAssignments>().SetRolesAsync(user.Id, roleIds, CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>().SaveChangesAsync();
        }

        return user;
    }

    /// <summary>Anmeldung; Standard ist der Web-Client (ohne Gerätepflicht). Mobile Clients siehe Gerätetests.</summary>
    public static Task<HttpResponseMessage> LoginAsync(this HttpClient client, string login, string password = DefaultPassword,
        string clientKind = "web") =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { login, password, client = clientKind, clientName = "Test" });

    public static async Task<TokenResponse> LoginOkAsync(this HttpClient client, string login, string password = DefaultPassword,
        string clientKind = "web")
    {
        using var response = await client.LoginAsync(login, password, clientKind);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>(TestJson.Options))!;
    }

    /// <summary>Neuer Client mit Benutzer und Rolle, angemeldet als Web-Client.</summary>
    public static async Task<HttpClient> CreateClientAsAsync(this ApiFactory factory, string userName, params Guid[] roleIds)
    {
        await factory.CreateUserAsync(userName, roleIds: roleIds);
        var client = factory.CreateClient();
        var tokens = await client.LoginOkAsync(userName);
        return client.WithToken(tokens.AccessToken);
    }

    public static HttpClient WithToken(this HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}
