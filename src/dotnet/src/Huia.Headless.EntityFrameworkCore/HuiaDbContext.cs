using Huia.EntityFrameworkCore;
using Huia.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Huia.Headless.EntityFrameworkCore;

/// <summary>
/// The single-tenant Huia database context: the common <c>Huia*</c>-renamed Identity schema (users,
/// roles, passkeys), no multi-tenancy, no OpenIddict. Provider-agnostic; ships no migrations.
/// </summary>
public class HuiaDbContext(DbContextOptions<HuiaDbContext> options) : IdentityDbContext<HuiaUser, HuiaRole, string>(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ConfigureHuiaIdentitySchema<HuiaUser, HuiaRole>();
    }
}
