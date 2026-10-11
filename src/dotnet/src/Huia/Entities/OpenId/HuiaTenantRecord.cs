namespace Huia.OpenId.Entities;

/// <summary>
/// A persisted, runtime-defined tenant. The configuration is an opaque, Data-Protection-protected payload
/// (see <c>HuiaTenantSerializer</c>) because it carries client and SMTP / SMS secrets.
/// </summary>
public class HuiaTenantRecord
{
    /// <summary>The tenant id (also the base-path segment). Primary key.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>The protected configuration payload.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>A counter that increases on every save.</summary>
    public long Version { get; set; }

    /// <summary>When the tenant was first saved.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When the tenant was last saved.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
