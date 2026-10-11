using Huia.Admin;
using Xunit;

namespace Huia.Tests;

public class HuiaUserAdministrationTests
{
    [Theory]
    [InlineData("admin", true)]
    [InlineData("tenant:editor.v2_x-1", true)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData(null, false)]
    public void Role_names_are_validated(string? name, bool expected)
        => Assert.Equal(expected, HuiaUserAdministration.IsValidRoleName(name));

    [Fact]
    public void An_inline_claim_and_a_list_are_combined_and_trimmed()
    {
        var ok = HuiaUserAdministration.TryNormalizeClaims(
            " dept ", " sales ", [new HuiaClaimInput("level", " 3 ")], out var claims, out _);

        Assert.True(ok);
        Assert.Collection(claims,
            c => { Assert.Equal("dept", c.Type); Assert.Equal("sales", c.Value); },
            c => { Assert.Equal("level", c.Type); Assert.Equal("3", c.Value); });
    }

    [Fact]
    public void A_claim_without_a_type_is_rejected()
    {
        var ok = HuiaUserAdministration.TryNormalizeClaims(
            null, null, [new HuiaClaimInput(" ", "x")], out _, out var problem);

        Assert.False(ok);
        Assert.Equal(HuiaAdminOutcomeKind.Invalid, problem.Kind);
        Assert.Equal("claims", problem.Field);
    }

    [Fact]
    public void Supplying_no_claims_at_all_is_rejected()
    {
        var ok = HuiaUserAdministration.TryNormalizeClaims(null, null, null, out _, out var problem);

        Assert.False(ok);
        Assert.Equal("claims", problem.Field);
    }
}
