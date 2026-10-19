using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EstrichOne.Api.Modules.Setup;

public sealed record SetupStatus(bool SetupRequired);

public sealed record SetupAdministrator(
    [property: Required, MaxLength(User.MaxNameLength)] string DisplayName,
    [property: Required, MaxLength(User.MaxUserNameLength)] string UserName,
    [property: MaxLength(User.MaxEmailLength), EmailAddress] string? Email,
    [property: Required, MaxLength(128)] string Password);

public sealed record CompleteSetupRequest(
    [property: Required, MaxLength(20)] string SetupCode,
    [property: Required, MaxLength(200)] string CompanyName,
    [property: Required] SetupAdministrator Administrator);

internal static class SetupEndpoints
{
    public const string RateLimitPolicy = "auth";

    public static void Map(IEndpointRouteBuilder api)
    {
        var setup = api.MapGroup("/setup").WithTags("Ersteinrichtung").AllowAnonymous();

        setup.MapGet("/status", async (SetupService service, CancellationToken ct) =>
                TypedResults.Ok(new SetupStatus(await service.IsSetupRequiredAsync(ct))))
            .WithName("GetSetupStatus");

        setup.MapPost("/", CompleteAsync)
            .WithName("CompleteSetup")
            .WithSummary("Firma und ersten Administrator anlegen – nur mit dem Einrichtungscode aus dem Server-Log, nur einmal")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireRateLimiting(RateLimitPolicy);
    }

    private static async Task<Results<Created<SetupStatus>, ProblemHttpResult, ValidationProblem>> CompleteAsync(
        CompleteSetupRequest request, SetupService service, CancellationToken cancellationToken)
    {
        try
        {
            var (_, failure) = await service.CompleteAsync(request.SetupCode, request.CompanyName,
                request.Administrator.DisplayName, request.Administrator.UserName, request.Administrator.Email,
                request.Administrator.Password, cancellationToken);
            return failure switch
            {
                SetupFailure.AlreadyCompleted => Problem(StatusCodes.Status409Conflict, "SETUP_ALREADY_COMPLETED",
                    "EstrichONE ist bereits eingerichtet."),
                SetupFailure.InvalidCode => Problem(StatusCodes.Status403Forbidden, "INVALID_SETUP_CODE",
                    "Der Einrichtungscode ist ungültig oder abgelaufen. Den aktuellen Code zeigt: docker compose logs api"),
                _ => TypedResults.Created("/api/v1/setup/status", new SetupStatus(SetupRequired: false)),
            };
        }
        catch (PasswordRejectedException exception)
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["administrator.password"] = [exception.Message] },
                extensions: new Dictionary<string, object?> { ["code"] = "VALIDATION_FAILED" });
        }
    }

    private static ProblemHttpResult Problem(int status, string code, string title) =>
        TypedResults.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}
