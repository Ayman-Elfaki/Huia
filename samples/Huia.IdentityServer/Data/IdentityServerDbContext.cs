using Huia.EntityFrameworkCore.Common;
using Microsoft.EntityFrameworkCore;

namespace Huia.IdentityServer.Data;

/// <summary>
/// This sample's own Huia store — a completely separate database and user pool from
/// <c>Huia.TodoApi</c>'s, so this app is a genuinely independent identity provider rather than sharing
/// TodoApi's accounts.
/// </summary>
public sealed class IdentityServerDbContext(DbContextOptions<IdentityServerDbContext> options) : HuiaDbContext(options);
