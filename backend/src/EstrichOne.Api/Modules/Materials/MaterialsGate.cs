using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Organization.Exports;

namespace EstrichOne.Api.Modules.Materials;

/// <summary>
/// Materialmodul abschaltbar (Einstellungen → Module): Die Endpunkte antworten dann mit 409 MODULE_DISABLED.
/// Administratoren (Einstellungen verwalten) dürfen vorhandene Daten weiter lesen. Daten bleiben erhalten.
/// </summary>
internal static class MaterialsGate
{
    public static RouteGroupBuilder RequireMaterialsModule(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var settings = await http.RequestServices.GetRequiredService<ISystemSettings>().GetAsync(http.RequestAborted);
            if (settings.Modules.Materials)
            {
                return await next(context);
            }

            if (HttpMethods.IsGet(http.Request.Method)
                && await http.RequestServices.GetRequiredService<IUserPermissions>().HasAsync(Permissions.SettingsManage, http.RequestAborted))
            {
                return await next(context);
            }

            return Disabled();
        });
        return group;
    }

    public static IResult Disabled() => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Modul deaktiviert",
        detail: "Das Materialmodul ist deaktiviert (Einstellungen → Module).", extensions: new Dictionary<string, object?> { ["code"] = "MODULE_DISABLED" });
}
