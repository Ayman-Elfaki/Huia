using System.Text.Json;
using Huia.Options;
using Microsoft.AspNetCore.DataProtection;

namespace Huia.Tenants;

/// <summary>
/// Turns a runtime tenant into the opaque payload stored by <see cref="Huia.Stores.IHuiaTenantStore"/> and back.
/// The payload is validated on the way in and protected with ASP.NET Core Data Protection on the way out,
/// because it carries client secrets and SMTP / SMS credentials.
/// </summary>
public sealed class HuiaTenantSerializer(IDataProtectionProvider dataProtection)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private readonly IDataProtector _protector = dataProtection.CreateProtector("Huia.Tenants.v1");

    /// <summary>
    /// Validates <paramref name="tenant"/> and returns its protected payload.
    /// </summary>
    /// <exception cref="HuiaOptionsException">The tenant does not validate.</exception>
    public string Protect(HuiaTenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        var errors = new List<string>();
        tenant.Validate($"Tenants:{tenant.Identifier}", errors);
        if (errors.Count > 0)
        {
            throw new HuiaOptionsException(errors);
        }

        var json = JsonSerializer.Serialize(HuiaTenantDocument.From(tenant), JsonOptions);
        return _protector.Protect(json);
    }

    /// <summary>Reads a payload produced by <see cref="Protect"/> back into tenant <paramref name="tenantId"/>.</summary>
    public HuiaTenant Unprotect(string tenantId, string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrEmpty(payload);

        var json = _protector.Unprotect(payload);
        var document = JsonSerializer.Deserialize<HuiaTenantDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Tenant '{tenantId}' has an empty stored document.");
        return document.ToTenant(tenantId);
    }
}
