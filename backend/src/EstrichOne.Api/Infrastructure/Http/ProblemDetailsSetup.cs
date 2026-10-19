using System.Diagnostics;

namespace EstrichOne.Api.Infrastructure.Http;

/// <summary>
/// Einheitliches Fehlerformat nach RFC 9457 (<c>application/problem+json</c>).
/// Jede Antwort enthält <c>code</c> und <c>traceId</c>; interne Details (Ausnahmetext, Datei, Zeile)
/// gelangen nie zum Client, sondern nur ins Server-Log.
/// </summary>
public static class ProblemDetailsSetup
{
    public static IServiceCollection AddEstrichOneProblemDetails(this IServiceCollection services) =>
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            var status = problem.Status ?? StatusCodes.Status500InternalServerError;

            problem.Extensions.TryAdd("code", problem is HttpValidationProblemDetails
                ? ErrorCodes.ValidationFailed
                : ErrorCodes.FromStatus(status));
            problem.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
            problem.Instance ??= context.HttpContext.Request.Path;

            if (context.Exception is not null)
            {
                // Unbehandelte Ausnahme: Ausnahmetext und Stacktrace nie an den Client (nur ins Log).
                problem.Title = "Interner Serverfehler";
                problem.Detail = "Die Anfrage konnte nicht verarbeitet werden. Bitte die traceId an den Support melden.";
                problem.Extensions.Remove("exception");
                problem.Extensions.Remove("errors");
            }
        });

    public static IResult Problem(int status, string code, string title, string? detail = null) =>
        TypedResults.Problem(
            statusCode: status,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
