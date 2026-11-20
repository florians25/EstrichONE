namespace EstrichOne.Api.Modules.Bauakten.Exports;

public static class BauakteWriteEndpointExtensions
{
    /// <summary>
    /// Änderung nur an zugeteilten Bauakten (oder mit „Alle Bauakten bearbeiten“): Liest die Bauakte-ID aus dem
    /// Routenparameter und antwortet vor dem Handler mit 404 BAUAKTE_NOT_FOUND bzw. 403 BAUAKTE_NOT_ASSIGNED.
    /// </summary>
    public static RouteHandlerBuilder RequireBauakteWrite(this RouteHandlerBuilder builder, string routeParameter = "id") =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (http.Request.RouteValues.TryGetValue(routeParameter, out var raw) && Guid.TryParse(raw?.ToString(), out var bauakteId))
            {
                var access = http.RequestServices.GetRequiredService<IBauakteAccess>();
                if (BauakteWriteProblems.For(await access.CheckWriteAsync(bauakteId, http.RequestAborted)) is { } problem)
                {
                    return problem;
                }
            }

            return await next(context);
        }).ProducesProblem(StatusCodes.Status403Forbidden);
}
