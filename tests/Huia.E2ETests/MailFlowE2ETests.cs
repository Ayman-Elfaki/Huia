using System.Net;
using System.Text.RegularExpressions;

namespace Huia.E2ETests;

/// <summary>
/// The forgot-password and confirm-email flows on the identity server, end to end against a real SMTP
/// sink. <c>Huia.AppHost</c> wires Mailpit as a container and points <c>Huia:Email</c> at it, so these
/// specs post the identity server's own Razor forms and then read the resulting message — and follow its
/// link — through Mailpit's REST API.
/// </summary>
[Trait("Category", "E2E")]
[Collection("apphost")]
public sealed partial class MailFlowE2ETests(AppHostFixture host)
{
    [SkippableFact]
    public async Task Forgot_password_emails_a_working_reset_link()
    {
        Skip.IfNot(host.Started, host.SkipReason ?? "AppHost not started");
        await host.Mailpit.ClearAsync();

        using var client = host.CreateIdpClient();

        var sent = await PostFormAsync(client, "e2e/identity/account/forgotpassword",
            new() { ["Email"] = "e2e-user@huia.local" });
        ((int)sent.StatusCode).ShouldBeInRange(200, 399);

        var resetUrl = await host.Mailpit.WaitForActionUrlAsync("e2e-user@huia.local");

        var reset = await PostFormAsync(client, Relative(resetUrl), new()
        {
            ["Input.Password"] = "Rotated1!Pass",
            ["Input.ConfirmPassword"] = "Rotated1!Pass",
        });
        ((int)reset.StatusCode).ShouldBeInRange(200, 399);
        var resetBody = await Follow(client, reset);
        resetBody.ShouldContain("data-testid=\"reset-done\"");
    }

    [SkippableFact]
    public async Task Register_sends_a_confirmation_link_that_activates_the_account()
    {
        Skip.IfNot(host.Started, host.SkipReason ?? "AppHost not started");
        await host.Mailpit.ClearAsync();

        using var client = host.CreateIdpClient();
        var email = $"new-{Guid.NewGuid():N}@e2e.test";

        var registered = await PostFormAsync(client, "e2e-signup/identity/account/register", new()
        {
            ["Input.FirstName"] = "Nora",
            ["Input.LastName"] = "New",
            ["Input.Email"] = email,
            ["Input.Password"] = "Fresh1!Pass",
            ["Input.ConfirmPassword"] = "Fresh1!Pass",
        });
        var registerBody = await Follow(client, registered);
        registerBody.ShouldContain("data-testid=\"register-confirmation\"");

        var confirmUrl = await host.Mailpit.WaitForActionUrlAsync(email);
        var confirmed = await client.GetAsync(Relative(confirmUrl));
        (await Follow(client, confirmed)).ShouldContain("data-testid=\"email-confirmed\"");

        // The account can now sign in with its password.
        var signIn = await PostFormAsync(client, "e2e-signup/identity/account/login", new()
        {
            ["Input.Email"] = email,
            ["Input.Password"] = "Fresh1!Pass",
        });
        signIn.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        signIn.Headers.Location!.ToString().ShouldNotContain("/identity/account/login");
    }

    /// <summary>GETs a Razor page, replays every hidden/antiforgery field plus the given overrides as a POST.</summary>
    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string path, Dictionary<string, string> overrides)
    {
        var pageHtml = await client.GetStringAsync(path);

        var fields = new Dictionary<string, string>();
        foreach (Match input in InputRegex().Matches(pageHtml))
        {
            var name = NameRegex().Match(input.Value);
            if (!name.Success)
            {
                continue;
            }

            var value = ValueRegex().Match(input.Value);
            fields[WebUtility.HtmlDecode(name.Groups[1].Value)] =
                value.Success ? WebUtility.HtmlDecode(value.Groups[1].Value) : string.Empty;
        }

        foreach (var (key, value) in overrides)
        {
            fields[key] = value;
        }

        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    private static async Task<string> Follow(HttpClient client, HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.SeeOther)
        {
            using var followed = await client.GetAsync(response.Headers.Location!.ToString().TrimStart('/'));
            return await followed.Content.ReadAsStringAsync();
        }

        return await response.Content.ReadAsStringAsync();
    }

    private static string Relative(string absoluteUrl)
    {
        var uri = new Uri(absoluteUrl);
        return uri.PathAndQuery.TrimStart('/');
    }

    [GeneratedRegex("<input\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InputRegex();

    [GeneratedRegex("name=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex NameRegex();

    [GeneratedRegex("value=\"([^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex ValueRegex();
}
