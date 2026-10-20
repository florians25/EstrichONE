using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Domain;
using EstrichOne.Hosting.Infrastructure;
using EstrichOne.Hosting.Instances;
using EstrichOne.Hosting.Operations;
using EstrichOne.Hosting.Status;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Hosting.Api;

/// <summary>Mandanten-Logik: Prüfung der Stammdaten, Anlegen, Ändern, Abbildung für die Oberfläche.</summary>
public sealed class TenantService(
    HostingDbContext db,
    StatusService status,
    InstanceLayout layout,
    ConsoleSettings settings,
    IOptions<HostingOptions> options,
    TimeProvider clock)
{
    private HostingOptions Settings => options.Value;

    /// <summary>Vorgeschlagene Domain: <c>&lt;kürzel&gt;.&lt;basis-domain&gt;</c>.</summary>
    public string DefaultDomain(string slug) => $"{slug}.{Validation.NormalizeDomain(Settings.BaseDomain)}";

    public sealed record ValidatedInput(
        string CompanyName, string? CustomerNumber, string? ContactName, string? ContactEmail, string? ContactPhone,
        string Slug, string Domain, Guid PlanId, string Version, string? Notes);

    /// <summary>Prüft alle Eingaben; Kürzel und Domain müssen eindeutig sein (bei Änderung ohne den Mandanten selbst).</summary>
    public async Task<(ValidatedInput? Value, Dictionary<string, string> Errors)> ValidateAsync(TenantInput input, Tenant? existing,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        var company = input.CompanyName?.Trim();
        if (Validation.CheckText(company, "Der Firmenname", 120, true, envSafe: true) is { } companyError) errors["companyName"] = companyError;
        var customer = Blank(input.CustomerNumber);
        if (Validation.CheckText(customer, "Die Kundennummer", 40, false) is { } customerError) errors["customerNumber"] = customerError;
        var contact = Blank(input.ContactName);
        if (Validation.CheckText(contact, "Der Ansprechpartner", 120, false) is { } contactError) errors["contactName"] = contactError;
        var email = Blank(input.ContactEmail);
        if (Validation.CheckEmail(email, required: true) is { } emailError) errors["contactEmail"] = emailError;
        var phone = Blank(input.ContactPhone);
        if (Validation.CheckText(phone, "Die Telefonnummer", 40, false) is { } phoneError) errors["contactPhone"] = phoneError;
        var notes = Blank(input.Notes);
        if (notes is { Length: > 4000 }) errors["notes"] = "Die Notiz ist zu lang (höchstens 4000 Zeichen).";

        var slug = existing?.Slug ?? (input.Slug ?? string.Empty).Trim().ToLowerInvariant();
        if (existing is null)
        {
            if (Validation.CheckSlug(slug) is { } slugError)
            {
                errors["slug"] = slugError;
            }
            else if (await db.Tenants.AnyAsync(t => t.Slug == slug, cancellationToken)
                     || Directory.Exists(layout.Directory(slug)))
            {
                errors["slug"] = $"Das Kürzel „{slug}“ ist bereits vergeben.";
            }
        }

        var domain = Validation.NormalizeDomain(string.IsNullOrWhiteSpace(input.Domain) && Validation.IsValidSlug(slug) ? DefaultDomain(slug) : input.Domain);
        if (Validation.CheckDomain(domain) is { } domainError)
        {
            errors["domain"] = domainError;
        }
        else if (domain == Validation.NormalizeDomain(Settings.ConsoleDomain))
        {
            errors["domain"] = "Diese Domain gehört zur Hosting-Konsole.";
        }
        else if (await db.Tenants.AnyAsync(t => t.Domain == domain && (existing == null || t.Id != existing.Id), cancellationToken))
        {
            errors["domain"] = $"Die Domain {domain} ist bereits einem anderen Mandanten zugeordnet.";
        }

        var planId = input.PlanId ?? existing?.PlanId;
        if (planId is null || !await db.Plans.AnyAsync(p => p.Id == planId, cancellationToken))
        {
            errors["planId"] = "Bitte einen Tarif wählen.";
        }

        var version = existing?.Version ?? (string.IsNullOrWhiteSpace(input.Version) ? await settings.GetDefaultVersionAsync(cancellationToken) : input.Version.Trim());
        if (Validation.CheckVersion(version) is { } versionError) errors["version"] = versionError;

        return errors.Count > 0
            ? (null, errors)
            : (new ValidatedInput(company!, customer, contact, email, phone, slug, domain, planId!.Value, version, notes), errors);
    }

    public Tenant Create(ValidatedInput input)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var tenant = new Tenant
        {
            Slug = input.Slug,
            CompanyName = input.CompanyName,
            CustomerNumber = input.CustomerNumber,
            ContactName = input.ContactName,
            ContactEmail = input.ContactEmail,
            ContactPhone = input.ContactPhone,
            Domain = input.Domain,
            PlanId = input.PlanId,
            Version = input.Version,
            Notes = input.Notes,
            State = TenantState.Provisioning,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Tenants.Add(tenant);
        return tenant;
    }

    /// <summary>Übernimmt geänderte Stammdaten. Gibt zurück, ob die Instanz neu konfiguriert werden muss.</summary>
    public static bool Apply(Tenant tenant, ValidatedInput input)
    {
        var reconfigure = tenant.Domain != input.Domain || tenant.PlanId != input.PlanId || tenant.CompanyName != input.CompanyName;
        tenant.CompanyName = input.CompanyName;
        tenant.CustomerNumber = input.CustomerNumber;
        tenant.ContactName = input.ContactName;
        tenant.ContactEmail = input.ContactEmail;
        tenant.ContactPhone = input.ContactPhone;
        tenant.Domain = input.Domain;
        tenant.PlanId = input.PlanId;
        tenant.Notes = input.Notes;
        tenant.UpdatedAt = DateTime.UtcNow;
        return reconfigure;
    }

    public TenantListItem ToListItem(Tenant tenant, Plan plan, string defaultVersion, Operation? open)
    {
        var current = status.Get(tenant.Slug);
        return new TenantListItem(
            tenant.Id, tenant.Slug, tenant.CompanyName, tenant.CustomerNumber, tenant.ContactName, tenant.ContactEmail, tenant.Domain,
            InstanceFiles.PublicUrl(tenant.Domain, Settings), tenant.PlanId, plan.Name, tenant.Version,
            SemanticVersion.IsOlder(tenant.Version, defaultVersion), tenant.State, tenant.LockReason,
            current.CollectedAt == DateTime.MinValue ? HealthLevel.Unknown : current.Health, current.Problems,
            current.CpuPercent, current.MemoryBytes, current.DiskBytes, (long)plan.StorageGb * 1024 * 1024 * 1024,
            current.ActiveUsers, plan.MaxUsers, current.Backup?.Configured == true ? current.Backup.Healthy : null,
            current.Backup?.LastSuccess?.At, current.SetupRequired, open is null ? null : ToSummary(open), tenant.LastError, tenant.CreatedAt,
            current.CollectedAt == DateTime.MinValue ? null : current.CollectedAt);
    }

    public static OperationSummary ToSummary(Operation operation) =>
        new(operation.Id, operation.Kind, OperationService.Labels[operation.Kind], operation.Status, operation.RequestedBy,
            operation.QueuedAt, operation.StartedAt, operation.FinishedAt, operation.Error);

    public static OperationDetail ToDetail(Operation operation) =>
        new(operation.Id, operation.TenantId, operation.Kind, OperationService.Labels[operation.Kind], operation.Status, operation.RequestedBy,
            operation.QueuedAt, operation.StartedAt, operation.FinishedAt, operation.Error, operation.Log);

    public async Task<Dictionary<Guid, Operation>> OpenOperationsAsync(CancellationToken cancellationToken) =>
        (await db.Operations.AsNoTracking()
            .Where(o => o.Status == OperationStatus.Queued || o.Status == OperationStatus.Running)
            .ToListAsync(cancellationToken))
        .GroupBy(o => o.TenantId)
        .ToDictionary(g => g.Key, g => g.OrderBy(o => o.QueuedAt).First());

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
