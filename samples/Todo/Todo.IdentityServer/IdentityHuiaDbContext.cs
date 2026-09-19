using System;
using Finbuckle.MultiTenant.Abstractions;
using Huia.OpenId.EntityFrameworkCore;
using Huia.OpenId.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Todo.IdentityServer;

public class IdentityHuiaDbContext(IMultiTenantContextAccessor multiTenantContextAccessor, DbContextOptions<IdentityHuiaDbContext> options)
    : HuiaDbContext<HuiaUser, HuiaRole, string>(multiTenantContextAccessor, options)
{
}
