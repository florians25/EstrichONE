namespace EstrichOne.Hosting.Api;

/// <summary>Einheitliche Fehlerantworten (RFC 7807) mit deutscher Meldung in <c>detail</c>.</summary>
public static class ApiErrors
{
    public static IResult BadRequest(string detail) =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Ungültige Eingabe", detail: detail);

    public static IResult Validation(IDictionary<string, string> errors) =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Ungültige Eingabe",
            detail: string.Join(" ", errors.Values),
            extensions: new Dictionary<string, object?> { ["fields"] = errors });

    public static IResult NotFound(string detail = "Nicht gefunden.") =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Nicht gefunden", detail: detail);

    public static IResult Conflict(string detail) =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Konflikt", detail: detail);

    public static IResult Unauthorized(string detail, string? code = null) =>
        Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Anmeldung fehlgeschlagen", detail: detail,
            extensions: code is null ? null : new Dictionary<string, object?> { ["code"] = code });

    public static IResult Forbidden(string detail) =>
        Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Nicht erlaubt", detail: detail);
}
