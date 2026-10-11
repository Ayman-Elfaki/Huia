using Xunit;

namespace Huia.Tests;

/// <summary>Guards the package layering: persistence adapters depend on the server, never the other way round.</summary>
public class ArchitectureTests
{
    [Fact]
    public void Huia_OpenId_does_not_reference_the_EntityFrameworkCore_adapter()
    {
        var references = typeof(Huia.OpenId.Multitenancy.HuiaTenantInfo).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        Assert.DoesNotContain("Huia.OpenId.EntityFrameworkCore", references);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", references);
    }

    [Fact]
    public void Huia_core_does_not_reference_EntityFrameworkCore_OpenIddict_or_Finbuckle()
    {
        var references = typeof(Huia.OpenId.Entities.HuiaUser).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(references, n => n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(references, n => n.StartsWith("OpenIddict", StringComparison.Ordinal));
        Assert.DoesNotContain(references, n => n.StartsWith("Finbuckle", StringComparison.Ordinal));
    }
}
