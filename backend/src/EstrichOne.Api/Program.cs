using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Http;
using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;
using Serilog.Formatting.Compact;

if (CommandLine.TryGetCommand(args, out var command) && command == "healthcheck")
{
    return await CommandLine.HealthCheckAsync();
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, logger) =>
{
    logger.ReadFrom.Configuration(builder.Configuration).Enrich.FromLogContext();
    if (builder.Environment.IsDevelopment())
    {
        logger.WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture);
    }
    else
    {
        logger.WriteTo.Console(new RenderedCompactJsonFormatter());
    }
});

builder.Services.AddOptions<EstrichOneOptions>().Bind(builder.Configuration.GetSection(EstrichOneOptions.SectionName));
var options = builder.Configuration.GetSection(EstrichOneOptions.SectionName).Get<EstrichOneOptions>() ?? new();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.ConfigureHttpJsonOptions(json =>
{
    // Zahlen nur als JSON-Zahlen, Aufzählungen als Text – eindeutige Schemas für die generierten Clients.
    json.SerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict;
    json.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
});
builder.Services.AddValidation();
builder.Services.AddEstrichOneProblemDetails();
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddDbContextCheck<EstrichOneDbContext>("database", tags: ["ready"]);
builder.Services.AddEstrichOneOpenApi();
builder.Services.AddEstrichOneDataProtection(builder.Configuration);
builder.Services.AddModules(builder.Configuration);

if (options.TrustForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
    {
        forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // Die API ist im Docker-Stack nur über das interne Netz erreichbar; der Proxy hat keine feste IP.
        forwarded.KnownIPNetworks.Clear();
        forwarded.KnownProxies.Clear();
        // Hinter einem weiteren Proxy (Nginx Proxy Manager) lautet die Kette „Client, Proxy“. Caddy übernimmt fremde
        // Einträge nur von ESTRICHONE_TRUSTED_PROXIES; im Direktbetrieb steht dort deshalb nur der Client.
        forwarded.ForwardLimit = 2;
    });
}

var app = builder.Build();

if (command.Length > 0)
{
    return await CommandLine.RunAsync(command, app);
}

if (options.MigrateOnStartup)
{
    await CommandLine.MigrateAsync(app.Services, app.Logger, app.Lifetime.ApplicationStopping);
}

if (options.TrustForwardedHeaders)
{
    app.UseForwardedHeaders();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseSerilogRequestLogging(logging =>
    logging.GetLevel = (context, _, exception) =>
        exception is not null || context.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
        : context.Request.Path.StartsWithSegments("/health") ? Serilog.Events.LogEventLevel.Verbose
        : Serilog.Events.LogEventLevel.Information);

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

if (options.ExposeOpenApi)
{
    app.MapOpenApi("/api/openapi/{documentName}.json").AllowAnonymous();
}

app.MapModules();

await app.RunAsync();
return 0;

/// <summary>Einstiegspunkt; öffentlich für <c>WebApplicationFactory</c> in den Integrationstests.</summary>
public partial class Program;
