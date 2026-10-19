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
}
