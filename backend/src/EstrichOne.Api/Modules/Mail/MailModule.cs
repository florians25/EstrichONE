using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Modules.Mail.Exports;

namespace EstrichOne.Api.Modules.Mail;

/// <summary>E-Mail-Konto (SMTP), Vorlagen, Versandprotokoll und Versand für andere Module (<see cref="IMailService"/>).</summary>
public sealed class MailModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<SmtpSender>();
        services.AddSingleton<MailSignal>();
        services.AddScoped<MailService>();
        services.AddScoped<IMailService>(provider => provider.GetRequiredService<MailService>());
        services.AddHostedService<MailWorker>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => MailEndpoints.Map(api);
}
