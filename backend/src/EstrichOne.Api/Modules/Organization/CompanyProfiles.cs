using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Organization;

internal sealed class CompanyProfiles(EstrichOneDbContext db) : ICompanyProfiles
{
    public async Task CreateAsync(string companyName, CancellationToken cancellationToken)
    {
        if (await db.Set<CompanyProfile>().AnyAsync(cancellationToken))
        {
            throw new InvalidOperationException("Firmendaten sind bereits angelegt.");
        }

        db.Add(CompanyProfile.Create(companyName));
    }

    public Task<string?> GetNameAsync(CancellationToken cancellationToken) =>
        db.Set<CompanyProfile>().Select(profile => profile.Name).SingleOrDefaultAsync(cancellationToken);

    public Task<CompanyInfo?> GetProfileAsync(CancellationToken cancellationToken) =>
        db.Set<CompanyProfile>().AsNoTracking()
            .Select(p => new CompanyInfo(p.Name, p.Street, p.PostalCode, p.City, p.Phone, p.Email, p.Website))
            .SingleOrDefaultAsync(cancellationToken);
}
