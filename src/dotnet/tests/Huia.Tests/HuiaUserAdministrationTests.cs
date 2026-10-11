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

    [Fact]
    public void An_email_and_password_account_is_planned_with_trimmed_values()
    {
        var ok = HuiaUserAdministration.TryPlanUserCreation(
            " a@b.test ", "Password1!", null, true, null, " Ada ", null, out var plan, out _);

        Assert.True(ok);
        Assert.Equal("a@b.test", plan.UserName);
        Assert.Equal("a@b.test", plan.Email);
        Assert.True(plan.EmailConfirmed);
        Assert.Null(plan.PhoneNumber);
        Assert.Equal("Ada", plan.FirstName);
        Assert.Equal(string.Empty, plan.LastName);
        Assert.True(plan.HasPassword);
        Assert.Equal(HuiaConstants.AuthenticationMethods.Password, plan.AuthenticationMethod);
    }

    [Fact]
    public void A_phone_account_defaults_to_a_confirmed_number_and_reports_the_sms_method()
    {
        var ok = HuiaUserAdministration.TryPlanUserCreation(
            null, null, " +966500000000 ", null, null, null, null, out var plan, out _);

        Assert.True(ok);
        Assert.Equal("+966500000000", plan.UserName);
        Assert.True(plan.PhoneNumberConfirmed);
        Assert.False(plan.HasPassword);
        Assert.Equal(HuiaConstants.AuthenticationMethods.Sms, plan.AuthenticationMethod);
    }

    [Theory]
    [InlineData("a@b.test", "Password1!", "+966500000000")] // phone mixed with email/password
    [InlineData("a@b.test", null, null)]                    // email without a password
    [InlineData(null, "Password1!", null)]                  // password without an email
    [InlineData(null, null, null)]                          // nothing at all
    public void Inconsistent_contact_details_are_rejected(string? email, string? password, string? phone)
    {
        var ok = HuiaUserAdministration.TryPlanUserCreation(
            email, password, phone, null, null, null, null, out _, out var problem);

        Assert.False(ok);
        Assert.Equal(HuiaAdminOutcomeKind.Invalid, problem.Kind);
        Assert.Equal("contact", problem.Field);
    }

    [Fact]
    public void A_plan_is_applied_onto_the_user_entity()
    {
        HuiaUserAdministration.TryPlanUserCreation(
            "a@b.test", "Password1!", null, false, null, "Ada", "Lovelace", out var plan, out _);

        var user = new Huia.Entities.HuiaUser();
        plan.ApplyTo(user);

        Assert.Equal("a@b.test", user.UserName);
        Assert.Equal("a@b.test", user.Email);
        Assert.False(user.EmailConfirmed);
        Assert.Equal("Ada", user.FirstName);
        Assert.Equal("Lovelace", user.LastName);
    }
}
