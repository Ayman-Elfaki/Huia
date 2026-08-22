using System.Net;
using Huia.Identity;
using Huia.TodoApi.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.Tests.Integration;

/// <summary>
/// Covers <c>huia.DisableRegistration()</c>: the Register page becomes unreachable, Login stops linking to
/// it, and passwordless phone sign-in from a never-seen number is rejected instead of silently provisioning
/// a new account. See <see cref="RegistrationDisabledTestFactory"/>.
/// </summary>
public class RegistrationDisabledTests(RegistrationDisabledTestFactory factory)
    : IClassFixture<RegistrationDisabledTestFactory>
{
    private static int _phoneCounter;

    [Fact]
    public async Task Register_Get_ReturnsNotFound()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/identity/account/register");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Register_Post_ReturnsNotFoundAndDoesNotCreateAccount()
    {
        using var client = CreateClient();
        var email = $"registration-disabled-{Guid.NewGuid():N}@example.com";

        // The Register page's own GET is blocked (see Register_Get_ReturnsNotFound), so there's no legitimate
        // way to scrape a fresh token from it — antiforgery tokens aren't page-specific, though, so one
        // grabbed from Login still validates here, proving OnPostAsync's own guard (not just routing) rejects
        // this independently of the GET block.
        var token = await IdentityUiTestHelpers.GetAntiforgeryTokenAsync(client, "/identity/account/login");
        using var response = await client.PostAsync("/identity/account/register",
            new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Input.FirstName"] = "Test",
                ["Input.LastName"] = "User",
                ["Input.Email"] = email,
                ["Input.Password"] = "P@ssw0rd123!",
                ["Input.ConfirmPassword"] = "P@ssw0rd123!",
                ["__RequestVerificationToken"] = token,
            }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<HuiaUserManager>();
        Assert.Null(await userManager.FindByEmailAsync(email));
    }

    [Fact]
    public async Task Login_Get_DoesNotRenderCreateAccountLink()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/identity/account/login");

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("/identity/account/register", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PhoneLogin_NeverSeenNumber_DoesNotSendOtpOrCreateAccount()
    {
        using var client = CreateClient();
        var phoneNumber = NewPhoneNumber();

        var token = await IdentityUiTestHelpers.GetAntiforgeryTokenAsync(client, "/identity/account/login");
        using var response = await client.PostAsync("/identity/account/phonelogin",
            new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Input.CountryCode"] = "US",
                ["Input.PhoneNumber"] = phoneNumber,
                ["__RequestVerificationToken"] = token,
            }));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains("/identity/account/login", response.Headers.Location!.ToString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(factory.SentOtpCodes, c => string.Equals(c.PhoneNumberE164, phoneNumber, StringComparison.Ordinal));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HuiaAppDbContext>();
        Assert.False(await db.Users.AnyAsync(u => u.NormalizedPhoneNumber == phoneNumber));
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });

    private static string NewPhoneNumber()
    {
        var n = Interlocked.Increment(ref _phoneCounter);
        var suffix = 200 + n % 9700;
        return $"+1415555{suffix:D4}";
    }
}
