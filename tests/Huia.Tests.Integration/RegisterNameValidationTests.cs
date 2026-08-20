using System.Net;
using Huia.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Tests.Integration;

/// <summary>
/// Covers <c>RegisterModel</c>'s <c>InputModel.FirstName</c>/<c>LastName</c> validation (required, at most
/// <c>PersonNameValidator.MaxLength</c> characters, letters/spaces/hyphens/apostrophes only) through the real
/// form post, the same way <see cref="IdentityUiTestHelpers.RegisterAsync"/> does for the happy path — that
/// helper hardcodes valid names ("Test"/"User"), so this drives the form directly to submit invalid ones.
/// </summary>
public class RegisterNameValidationTests(TodoApiFactory factory) : IClassFixture<TodoApiFactory>
{
    private const string Password = "P@ssw0rd123!";
    private const string RegisterUrl = "/identity/account/register";

    [Theory]
    [InlineData("Ada99", "Valid")]
    [InlineData("Valid", "Ada_Lovelace")]
    [InlineData("", "Valid")]
    public async Task Register_WithInvalidName_DoesNotCreateAccount(string firstName, string lastName)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        var email = $"invalid-name-{Guid.NewGuid():N}@example.com";

        var token = await IdentityUiTestHelpers.GetAntiforgeryTokenAsync(client, RegisterUrl);
        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Input.FirstName"] = firstName,
            ["Input.LastName"] = lastName,
            ["Input.Email"] = email,
            ["Input.Password"] = Password,
            ["Input.ConfirmPassword"] = Password,
            ["__RequestVerificationToken"] = token,
        };

        using var response = await client.PostAsync(RegisterUrl, new FormUrlEncodedContent(form));

        // A successful registration redirects (302) to RegisterConfirmation or signs in; a validation failure
        // re-renders the same page (200) with the error instead.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<HuiaUser>>();
        Assert.Null(await userManager.FindByEmailAsync(email));
    }
}
