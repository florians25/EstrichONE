using System.Security.Claims;
using System.Text.Encodings.Web;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Devices.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace EstrichOne.Api.Modules.Identity.Sessions;

/// <summary>
/// Prüft <c>Authorization: Bearer eo1_…</c> bzw. im Browser das HttpOnly-Cookie gegen die Sitzungstabelle.
/// Gesperrte Benutzer und beendete Sitzungen verlieren den Zugriff mit der nächsten Anfrage (ADR 0011).
/// Mit Cookie angemeldete, zustandsändernde Anfragen brauchen den CSRF-Header (WebSessionEndpoints).
/// </summary>
public sealed class TokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    EstrichOneDbContext db,
    TimeProvider clock,
    IProblemDetailsService problemDetails)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "EstrichOneToken";
    public const string SessionClaim = "sid";
    private static readonly TimeSpan LastSeenResolution = TimeSpan.FromMinutes(5);
    private const string FailureCodeItem = "EstrichOne.AuthFailureCode";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        string token;
        if (!string.IsNullOrEmpty(header) && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = header["Bearer ".Length..].Trim();
        }
        else if (Request.Cookies[WebSessionEndpoints.AccessCookie] is { Length: > 0 } cookie)
        {
            if (!HttpMethods.IsGet(Request.Method) && !HttpMethods.IsHead(Request.Method) && !HttpMethods.IsOptions(Request.Method)
                && !WebSessionEndpoints.HasValidCsrfToken(Request))
            {
                return Fail("CSRF_INVALID", "Der Sicherheitstoken der Anfrage fehlt oder ist ungültig.");
            }

            token = cookie;
        }
        else
        {
            return AuthenticateResult.NoResult();
        }

        if (!token.StartsWith(TokenFactory.AccessPrefix, StringComparison.Ordinal))
        {
            return Fail("UNAUTHORIZED", "Ungültiges Zugriffstoken.");
        }

        var hash = TokenFactory.Hash(token);
        var match = await db.Set<Session>()
            .Where(session => session.AccessTokenHash == hash)
            .Select(session => new
            {
                session.Id,
                session.UserId,
                session.AccessTokenExpiresAt,
                session.RevokedAt,
                session.LastSeenAt,
                session.DeviceId,
                session.ClientKind,
                UserActive = db.Set<User>().Where(user => user.Id == session.UserId).Select(user => user.IsActive).FirstOrDefault(),
                DeviceStatus = db.Set<Device>().Where(device => device.Id == session.DeviceId).Select(device => (DeviceStatus?)device.Status).FirstOrDefault(),
            })
            .SingleOrDefaultAsync(Context.RequestAborted);

        var now = clock.GetUtcNow();
        if (match is null || match.RevokedAt is not null)
        {
            return Fail("UNAUTHORIZED", "Die Sitzung ist nicht (mehr) gültig. Bitte neu anmelden.");
        }

        if (!match.UserActive)
        {
            return Fail("USER_DISABLED", "Das Benutzerkonto ist gesperrt.");
        }

        if (match.DeviceStatus is DeviceStatus.Locked)
        {
            return Fail("DEVICE_LOCKED", "Dieses Gerät ist gesperrt. Bitte an die IT-Administration wenden.");
        }

        if (match.DeviceStatus is DeviceStatus.Retired)
        {
            return Fail("DEVICE_REVOKED", "Dieses Gerät wurde aus EstrichONE entfernt.");
        }

        if (match.AccessTokenExpiresAt <= now)
        {
            return Fail("ACCESS_TOKEN_EXPIRED", "Das Zugriffstoken ist abgelaufen. Bitte mit dem Refresh-Token erneuern.");
        }

        if (now - match.LastSeenAt > LastSeenResolution)
        {
            await db.Set<Session>().Where(s => s.Id == match.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.LastSeenAt, now), Context.RequestAborted);
        }

        var deviceState = match.DeviceStatus switch
        {
            DeviceStatus.Active => DeviceApprovedRequirement.Approved,
            DeviceStatus.Pending => DeviceApprovedRequirement.Pending,
            _ when match.ClientKind is ClientKind.Ios or ClientKind.Android => DeviceApprovedRequirement.Pending,
            _ => DeviceApprovedRequirement.NotRequired,
        };
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, match.UserId.ToString()),
                new Claim(SessionClaim, match.Id.ToString()),
                new Claim(DeviceApprovedRequirement.ClaimType, deviceState),
            ],
            SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var code = Context.Items[FailureCodeItem] as string ?? "UNAUTHORIZED";
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer";
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = Context,
            ProblemDetails =
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Nicht angemeldet",
                Detail = code switch
                {
                    "UNAUTHORIZED" => "Für diese Anfrage ist eine Anmeldung erforderlich.",
                    "CSRF_INVALID" => "Der Sicherheitstoken der Anfrage fehlt oder ist ungültig. Bitte die Seite neu laden.",
                    _ => null,
                },
                Extensions = { ["code"] = code },
            },
        });
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        var pending = Context.User.FindFirst(DeviceApprovedRequirement.ClaimType)?.Value == DeviceApprovedRequirement.Pending;
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = Context,
            ProblemDetails =
            {
                Status = StatusCodes.Status403Forbidden,
                Title = pending ? "Gerät noch nicht freigegeben" : "Keine Berechtigung",
                Detail = pending
                    ? "Dieses Gerät wartet auf die Freigabe durch die IT-Administration."
                    : "Für diese Aktion fehlt die nötige Berechtigung.",
                Extensions = { ["code"] = pending ? "DEVICE_NOT_APPROVED" : "PERMISSION_DENIED" },
            },
        }).AsTask();
    }

    private AuthenticateResult Fail(string code, string message)
    {
        Context.Items[FailureCodeItem] = code;
        return AuthenticateResult.Fail(message);
    }
}

internal sealed class CurrentUser(IHttpContextAccessor http) : ICurrentUser
{
    public Guid UserId => Guid.Parse(Claim(ClaimTypes.NameIdentifier));

    public Guid SessionId => Guid.Parse(Claim(TokenAuthenticationHandler.SessionClaim));

    private string Claim(string type) =>
        http.HttpContext?.User.FindFirstValue(type)
        ?? throw new InvalidOperationException("Kein angemeldeter Benutzer in dieser Anfrage.");
}
