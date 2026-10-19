using Microsoft.AspNetCore.Authorization;

namespace EstrichOne.Api.Modules.Identity.Exports;

/// <summary>
/// Zugriff auf Fachdaten nur mit freigegebenem Gerät (bzw. aus Web/Desktop). Gilt für die Standard- und
/// Fallback-Richtlinie und für jedes <c>RequirePermission</c>. Ausnahmen markiert <see cref="AllowPendingDevice{TBuilder}"/>.
/// </summary>
public sealed class DeviceApprovedRequirement : IAuthorizationRequirement
{
    public const string ClaimType = "device_state";
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string NotRequired = "not_required";
}

public static class DeviceAuthorization
{
    public const string AllowPendingPolicy = "AllowPendingDevice";

    /// <summary>Für Endpunkte, die ein noch nicht freigegebenes Gerät braucht (Registrierung, Status, Abmelden).</summary>
    public static TBuilder AllowPendingDevice<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder => builder.RequireAuthorization(AllowPendingPolicy);

    public static AuthorizationPolicyBuilder RequireApprovedDevice(this AuthorizationPolicyBuilder policy) =>
        policy.AddRequirements(new DeviceApprovedRequirement());
}

internal sealed class DeviceApprovedHandler : AuthorizationHandler<DeviceApprovedRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, DeviceApprovedRequirement requirement)
    {
        var state = context.User.FindFirst(DeviceApprovedRequirement.ClaimType)?.Value;
        if (state is DeviceApprovedRequirement.Approved or DeviceApprovedRequirement.NotRequired)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
