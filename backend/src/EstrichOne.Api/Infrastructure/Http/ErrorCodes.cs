namespace EstrichOne.Api.Infrastructure.Http;

/// <summary>
/// Stabile, maschinenlesbare Fehlercodes (Feld <c>code</c> in jeder Problem-Antwort).
/// Clients verzweigen ausschließlich über diese Codes, nie über den Meldungstext.
/// </summary>
public static class ErrorCodes
{
    public const string BadRequest = "BAD_REQUEST";
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string PermissionDenied = "PERMISSION_DENIED";
    public const string NotFound = "NOT_FOUND";
    public const string MethodNotAllowed = "METHOD_NOT_ALLOWED";
    public const string Conflict = "CONFLICT";
    public const string TooManyRequests = "TOO_MANY_REQUESTS";
    public const string InternalError = "INTERNAL_ERROR";

    public static string FromStatus(int status) => status switch
    {
        StatusCodes.Status400BadRequest => BadRequest,
        StatusCodes.Status401Unauthorized => Unauthorized,
        StatusCodes.Status403Forbidden => PermissionDenied,
        StatusCodes.Status404NotFound => NotFound,
        StatusCodes.Status405MethodNotAllowed => MethodNotAllowed,
        StatusCodes.Status409Conflict => Conflict,
        StatusCodes.Status422UnprocessableEntity => ValidationFailed,
        StatusCodes.Status429TooManyRequests => TooManyRequests,
        _ => status >= 500 ? InternalError : BadRequest,
    };
}
