using Microsoft.AspNetCore.Authorization;

namespace EstrichOne.Api.Modules.Identity.Sessions;

/// <summary>Markiert Endpunkte, die auch mit noch nicht gewechseltem Einmalpasswort erreichbar sind.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AllowDuringPasswordChangeAttribute : Attribute;

/// <summary>
/// Serverseitige Pflicht zum Passwortwechsel: Solange ein Konto ein Einmalpasswort der Administration hat
/// (<c>mustChangePassword</c>), lehnt die API alle angemeldeten Aufrufe mit 403 <c>PASSWORD_CHANGE_REQUIRED</c> ab –
/// außer Passwort ändern, Abmelden, Anmeldestatus (<c>/auth/me</c>) und Zwei-Faktor-Status/-Einrichtung. Öffentliche
/// Endpunkte (<c>AllowAnonymous</c>) bleiben unberührt. Der Zustand kommt bei jeder Anfrage frisch aus der Datenbank
/// (<see cref="TokenAuthenticationHandler"/>), nach dem Wechsel gilt dieselbe Sitzung sofort wieder voll.
/// </summary>
public static class PasswordChangeGate
{
    public const string Code = "PASSWORD_CHANGE_REQUIRED";
    public const string ClaimType = "password_change";
    public const string Required = "required";

    public static TBuilder AllowDuringPasswordChange<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder => builder.WithMetadata(new AllowDuringPasswordChangeAttribute());

    public static bool IsAllowed(Endpoint? endpoint) =>
        endpoint is null
        || endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
        || endpoint.Metadata.GetMetadata<AllowDuringPasswordChangeAttribute>() is not null;

    public static IApplicationBuilder UsePasswordChangeGate(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (context.User.FindFirst(ClaimType)?.Value == Required && !IsAllowed(context.GetEndpoint()))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails =
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Passwortwechsel erforderlich",
                    Detail = "Bitte zuerst ein eigenes Passwort festlegen – das Einmalpasswort der Administration ist noch aktiv.",
                    Extensions = { ["code"] = Code },
                },
            });
            return;
        }

        await next();
    });
}
