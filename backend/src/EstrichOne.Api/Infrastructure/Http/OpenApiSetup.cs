using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;

namespace EstrichOne.Api.Infrastructure.Http;

/// <summary>OpenAPI-Dokument: Titel und Bearer-Schema, damit generierte Clients Tokens korrekt mitsenden.</summary>
public static class OpenApiSetup
{
    public const string BearerScheme = "bearer";

    public static IServiceCollection AddEstrichOneOpenApi(this IServiceCollection services) =>
        services.AddOpenApi("v1", options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "EstrichONE API";
                document.Info.Version = "v1";
                document.Info.Description = "Zentrale API für EstrichONE Web, Desktop, iOS und Android.";
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    Description = "Zugriffstoken (eo1_…) aus /api/v1/auth/login bzw. /api/v1/auth/refresh.",
                };
                return Task.CompletedTask;
            });

            options.AddOperationTransformer((operation, context, _) =>
            {
                var anonymous = context.Description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any();
                if (!anonymous)
                {
                    operation.Security ??= [];
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(BearerScheme, context.Document)] = [],
                    });
                    operation.Responses ??= new OpenApiResponses();
                    operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Nicht angemeldet oder Token abgelaufen" });
                }

                return Task.CompletedTask;
            });
        });
}
