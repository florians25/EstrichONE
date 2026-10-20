
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using EstrichOne.Hosting.Api;
using EstrichOne.Hosting.Auth;
using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Docker;
using EstrichOne.Hosting.Infrastructure;
using EstrichOne.Hosting.Instances;
using EstrichOne.Hosting.Operations;
using EstrichOne.Hosting.Proxy;
using EstrichOne.Hosting.Status;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Hosting;

/// <summary>Aufbau der Anwendung (auch von den Integrationstests verwendet).</summary>
public static class HostingApp
{
    private static readonly string[] PrivateNetworks = ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8", "fc00::/7", "::1/128"];

    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
            o.UseUtcTimestamp = true;
        });
        builder.Services.Configure<HostingOptions>(builder.Configuration.GetSection(HostingOptions.SectionName));
        configure?.Invoke(builder);

        var hosting = builder.Configuration.GetSection(HostingOptions.SectionName).Get<HostingOptions>() ?? new HostingOptions();
        // Datenbank (Passwort-Hashes, TOTP-Geheimnisse) und Sitzungsschlüssel: nur für root lesbar.
        Directory.CreateDirectory(hosting.ConsoleDataDirectory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(hosting.ConsoleDataDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var connection = builder.Configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connection))
        {
            connection = $"Data Source={Path.Combine(hosting.ConsoleDataDirectory, "hosting.db")}";
        }

        // Schlüssel für Sitzungscookies dauerhaft speichern (Container ist schreibgeschützt; Neustart beendet keine Sitzungen).
        builder.Services.AddDataProtection()
            .SetApplicationName("estrichone-hosting")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(hosting.ConsoleDataDirectory, "keys")));
        builder.Services.AddDbContext<HostingDbContext>(o => o.UseSqlite(connection));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHttpContextAccessor();
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddProblemDetails();

        // Docker-Steuerung: echt oder simuliert (Tests, Vorführung).
        if (hosting.IsFake)
        {
            builder.Services.AddSingleton<FakeDockerHost>();
            builder.Services.AddSingleton<IDockerHost>(sp => sp.GetRequiredService<FakeDockerHost>());
            builder.Services.AddSingleton<IInstanceProbe, FakeInstanceProbe>();
        }
        else
        {
            builder.Services.AddSingleton<IDockerHost, DockerCliHost>();
            builder.Services.AddHttpClient<IInstanceProbe, HttpInstanceProbe>(c => c.Timeout = TimeSpan.FromSeconds(8))
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false });
        }

        builder.Services.AddHttpClient<VersionCatalog>();
        builder.Services.AddSingleton<VersionCatalog>(sp => new VersionCatalog(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(VersionCatalog)),
            sp.GetRequiredService<IOptions<HostingOptions>>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<VersionCatalog>>()));
        builder.Services.AddSingleton<InstanceLayout>();
        builder.Services.AddSingleton<InstanceFiles>();
        builder.Services.AddSingleton<ProxyManager>();
        builder.Services.AddSingleton<StatusService>();
        builder.Services.AddSingleton<OperationDispatcher>();
        builder.Services.AddScoped<AuditLog>();
        builder.Services.AddScoped<OperationService>();
        builder.Services.AddScoped<TenantLifecycle>();
        builder.Services.AddScoped<TenantService>();
        builder.Services.AddScoped<ConsoleSettings>();
        builder.Services.AddScoped<SetupCodeService>();
        builder.Services.AddHostedService<SetupCodeAnnouncer>();
        builder.Services.AddHostedService<OperationWorker>();
        builder.Services.AddHostedService<ProxyStartup>();
        if (!builder.Configuration.GetValue<bool>("Hosting:DisableStatusCollector"))
        {
            builder.Services.AddHostedService<StatusCollector>();
        }

        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
        {
            o.Cookie.Name = hosting.InsecureCookies ? "estrichone-hosting" : "__Host-estrichone-hosting";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = hosting.InsecureCookies ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            o.Cookie.Path = "/";
            o.ExpireTimeSpan = TimeSpan.FromHours(8);
            o.SlidingExpiration = true;
            o.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            o.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
            // Passwortänderung oder gelöschter Benutzer beendet bestehende Sitzungen.
            o.Events.OnValidatePrincipal = async context =>
            {
                var db = context.HttpContext.RequestServices.GetRequiredService<HostingDbContext>();
                var id = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var stamp = context.Principal?.FindFirst(AuthEndpoints.StampClaim)?.Value;
                var admin = Guid.TryParse(id, out var guid) ? await db.Admins.AsNoTracking().FirstOrDefaultAsync(a => a.Id == guid) : null;
                if (admin is null || admin.SecurityStamp.ToString(System.Globalization.CultureInfo.InvariantCulture) != stamp)
                {
                    context.RejectPrincipal();
                }
            };
        });
        builder.Services.AddAuthorization();

        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(AuthEndpoints.LoginRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unbekannt",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
            o.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    title = "Zu viele Anmeldeversuche",
                    status = 429,
                    detail = "Zu viele Anmeldeversuche von dieser Adresse. Bitte in einigen Minuten erneut versuchen.",
                }, token);
            };
        });

        builder.Services.Configure<ForwardedHeadersOptions>(o =>
        {
            // Nur der eigene Proxy im Docker-Netz (private Adressen) darf Client-Adresse und Protokoll setzen.
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
            foreach (var network in PrivateNetworks)
            {
                o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }

            o.ForwardLimit = 1;
        });

        var app = builder.Build();
        app.UseForwardedHeaders();
        app.UseSecurityHeaders();
        app.UseDefaultFiles();
        var contentTypes = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
        contentTypes.Mappings[".properties"] = "text/plain; charset=utf-8";
        app.UseStaticFiles(new StaticFileOptions
        {
            ContentTypeProvider = contentTypes,
            OnPrepareResponse = context =>
            {
                // Unter /ui-<hash>/ ändert sich nie etwas (neuer Build = neuer Pfad); index.html immer neu laden.
                var path = context.Context.Request.Path.Value ?? string.Empty;
                context.Context.Response.Headers.CacheControl =
                    path.StartsWith("/ui-", StringComparison.Ordinal) ? "public, max-age=31536000, immutable"
                    : path.EndsWith(".html", StringComparison.Ordinal) || path == "/" ? "no-cache"
                    : "public, max-age=3600";
            },
        });
        app.UseAuthentication();
        app.UseCsrfProtection();
        app.UseRateLimiter();
        app.UseAuthorization();

        app.MapGet("/health", () => Results.Ok(new { status = "ok", version = ConsoleVersion.Current })).AllowAnonymous();
        AuthEndpoints.Map(app);
        TenantEndpoints.Map(app);
        AdministrationEndpoints.Map(app);
        app.MapFallback("/api/{**rest}", () => ApiErrors.NotFound("Unbekannter API-Pfad."));
        return app;
    }
}

