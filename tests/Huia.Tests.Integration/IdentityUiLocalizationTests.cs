using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Huia.Tests.Integration;

/// <summary>
/// Proves every user-facing message the Identity UI's PageModel code-behind produces — not just the
/// page markup's own <c>@Localizer[...]</c> calls — actually renders in the requested culture: a plain
/// ModelState/TempData message built from <c>IStringLocalizer&lt;HuiaResources&gt;</c> directly, and a
/// DataAnnotations <c>[Required]</c>/<c>[EmailAddress]</c> message routed through
/// <c>AddDataAnnotationsLocalization</c>'s <c>DataAnnotationLocalizerProvider</c> (see
/// <c>ServiceCollectionExtensions.AddHuia</c>) — the latter is the one most likely to silently regress back to
/// English-only, since nothing about a missing resx entry fails loudly; it just falls back to the literal
/// attribute string.
/// </summary>
public class IdentityUiLocalizationTests(TodoApiFactory factory) : IClassFixture<TodoApiFactory>
{
    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });

    [Fact]
    public async Task Login_WrongPassword_WithArabicCulture_ShowsTheArabicModelStateMessage()
    {
        var email = $"ar-login-{Guid.NewGuid():N}@example.com";
        const string password = "P@ssw0rd123!";

        using var setupClient = CreateClient();
        using var registerResponse = await IdentityUiTestHelpers.RegisterAsync(setupClient, email, password);
        Assert.Equal(HttpStatusCode.Found, registerResponse.StatusCode);

        using var client = CreateClient();
        var loginUrl = "/identity/account/login?culture=ar&ui-culture=ar";
        var token = await IdentityUiTestHelpers.GetAntiforgeryTokenAsync(client, loginUrl);

        using var response = await client.PostAsync(loginUrl,
            new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Input.Email"] = email,
                ["Input.Password"] = "WrongPassword!1",
                ["culture"] = "ar",
                ["ui-culture"] = "ar",
                ["__RequestVerificationToken"] = token,
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        // "Invalid email or password." — never the English fallback.
        Assert.Contains("البريد الإلكتروني أو كلمة المرور غير صحيحة", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Invalid email or password.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_EmptyEmail_WithArabicCulture_ShowsTheArabicDataAnnotationsMessage()
    {
        using var client = CreateClient();
        var loginUrl = "/identity/account/login?culture=ar&ui-culture=ar";
        var token = await IdentityUiTestHelpers.GetAntiforgeryTokenAsync(client, loginUrl);

        using var response = await client.PostAsync(loginUrl,
            new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Input.Email"] = string.Empty,
                ["Input.Password"] = "whatever",
                ["culture"] = "ar",
                ["ui-culture"] = "ar",
                ["__RequestVerificationToken"] = token,
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        // [Required(ErrorMessage = "Email is required.")] on LoginModel.InputModel.Email — proves
        // AddDataAnnotationsLocalization's DataAnnotationLocalizerProvider is actually wired up, not just the
        // page's own explicit Localizer[...] calls.
        Assert.Contains("البريد الإلكتروني مطلوب", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Email is required.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Register_InvalidFirstName_WithArabicCulture_ShowsTheArabicPersonNameMessage()
    {
        using var client = CreateClient();
        var registerUrl = "/identity/account/register?culture=ar&ui-culture=ar";
        var token = await IdentityUiTestHelpers.GetAntiforgeryTokenAsync(client, registerUrl);

        using var response = await client.PostAsync(registerUrl,
            new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Input.FirstName"] = "Ada99",
                ["Input.LastName"] = "Valid",
                ["Input.Email"] = $"ar-register-{Guid.NewGuid():N}@example.com",
                ["Input.Password"] = "P@ssw0rd123!",
                ["Input.ConfirmPassword"] = "P@ssw0rd123!",
                ["culture"] = "ar",
                ["ui-culture"] = "ar",
                ["__RequestVerificationToken"] = token,
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        // [PersonName(ErrorMessage = "First name may only contain...")] on RegisterModel.InputModel.FirstName.
        Assert.Contains("يجب ألا يحتوي الاسم الأول إلا على أحرف ومسافات وشرطات وفواصل عليا", html,
            StringComparison.Ordinal);
        Assert.DoesNotContain("First name may only contain letters", html, StringComparison.Ordinal);
    }
}
