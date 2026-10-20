using System.Threading.RateLimiting;
using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Identity.Passwords;
using EstrichOne.Api.Modules.Identity.Sessions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace EstrichOne.Api.Modules.Identity;

/// <summary>Benutzer, lokale Anmeldung, Sitzungen (docs/reengineering/17, ADR 0011).</summary>
public sealed class IdentityModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthenticationSettings>().Bind(configuration.GetSection(AuthenticationSettings.SectionName));
        services.AddSingleton<PasswordHasher>();
        services.AddScoped<SessionService>();
        services.AddScoped<LoginService>();
        services.AddScoped<IUserAccounts, UserAccounts>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<ISessionDevices, SessionDevices>();
        services.AddSingleton<IAuthorizationHandler, DeviceApprovedHandler>();

        services.AddAuthentication(TokenAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TokenAuthenticationHandler>(TokenAuthenticationHandler.SchemeName, null);

        // Alles erfordert eine Anmeldung (und bei mobilen Clients ein freigegebenes Gerät),
        // sofern ein Endpunkt nicht ausdrücklich AllowAnonymous bzw. AllowPendingDevice trägt.
        var approved = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireApprovedDevice().Build();
        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(approved)
            .SetFallbackPolicy(approved)
            .AddPolicy(DeviceAuthorization.AllowPendingPolicy, policy => policy.RequireAuthenticatedUser());

        // Zusätzliche Bremse vor der Kontosperre: je IP-Adresse höchstens 20 Anfragen pro Minute an /auth.
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(AuthEndpoints.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unbekannt",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        AuthEndpoints.Map(api);
        WebSessionEndpoints.Map(api);
        DirectoryEndpoints.Map(api);
    }
}
