using Huia.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Huia.Headless.EntityFrameworkCore;

/// <summary>
/// The single-tenant Huia database context: the common <c>Huia*</c>-renamed Identity schema (users,
/// roles, passkeys), no multi-tenancy, no OpenIddict. Provider-agnostic; ships no migrations.
/// </summary>
/// <typeparam name="TUser">The Huia user entity.</typeparam>
/// <typeparam name="TRole">The Huia role entity.</typeparam>
/// <typeparam name="TId">The Identity key marker; Huia's built-in entities use <see cref="string"/> keys.</typeparam>
public class HuiaDbContext<TUser, TRole, TId>(DbContextOptions options)
    : IdentityDbContext<TUser, TRole, string>(options)
    where TUser : HuiaUser
    where TRole : HuiaRole
    where TId : notnull
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ConfigureHuiaIdentitySchema<TUser, TRole>();
    }
}
