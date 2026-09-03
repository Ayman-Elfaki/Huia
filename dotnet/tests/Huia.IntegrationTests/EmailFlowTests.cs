using System.Net;
using System.Text.RegularExpressions;
using Huia.AspNetCore.Emails;
using Huia.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Huia.IntegrationTests;

public sealed partial class EmailFlowTests : IAsyncLifetime
{
    private HuiaTestHost _host = null!;

    public async Task InitializeAsync() => _host = await HuiaTestHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Registration_sends_a_confirmation_link_that_confirms_the_account()
    {
        var client = _host.CreateClient();
        var email = "newbie@signup.test";

        var registerUrl = "/signup/identity/account/register";
        var token = await AntiforgeryAsync(client, registerUrl);
        var register = await client.PostAsync(registerUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.FirstName"] = "New",
            ["Input.LastName"] = "Bie",
            ["Input.Email"] = email,
            ["Input.Password"] = "Password1!",
            ["Input.ConfirmPassword"] = "Password1!",
            ["__RequestVerificationToken"] = token,
        }));
        register.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var confirmUrl = _host.Email.ConfirmationUrl(email);
        confirmUrl.ShouldNotBeNull();

        var confirm = await client.GetAsync(ToLocal(confirmUrl!));
        (await confirm.Content.ReadAsStringAsync()).ShouldContain("email-confirmed");
    }

    [Fact]
    public async Task Forgot_password_emails_a_reset_link_that_sets_a_new_password()
    {
        var email = "reset-me@signup.test";
        var userId = await _host.SeedUserAsync("signup", email, "OldPassword1!", emailConfirmed: true);
        _ = userId;

        var client = _host.CreateClient();
        var forgotUrl = "/signup/identity/account/forgotpassword";
        var token = await AntiforgeryAsync(client, forgotUrl);
        await client.PostAsync(forgotUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email,
            ["__RequestVerificationToken"] = token,
        }));

        var resetUrl = _host.Email.ResetUrl(email);
        resetUrl.ShouldNotBeNull();

        var local = ToLocal(resetUrl!);
        var page = await client.GetStringAsync(local);
        var resetToken = AntiforgeryRegex().Match(page).Groups["v"].Value;
        var userIdValue = Query(resetUrl!, "userId")!;
        var codeValue = Query(resetUrl!, "code")!;

        var reset = await client.PostAsync("/signup/identity/account/resetpassword", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.UserId"] = userIdValue,
            ["Input.Code"] = codeValue,
            ["Input.Password"] = "BrandNew1!",
            ["Input.ConfirmPassword"] = "BrandNew1!",
            ["__RequestVerificationToken"] = resetToken,
        }));

        (await reset.Content.ReadAsStringAsync()).ShouldContain("reset-done");
    }

    [Fact]
    public async Task Forgot_password_carries_the_return_url_back_to_the_sign_in_link()
    {
        var client = _host.CreateClient();

        var form = await client.GetStringAsync("/signup/identity/account/forgotpassword?returnUrl=%2Fconnect%2Fauthorize%3Fx%3D1");
        form.ShouldContain("returnUrl=%2Fconnect%2Fauthorize%3Fx%3D1");

        var token = AntiforgeryRegex().Match(form).Groups["v"].Value;
        var sent = await client.PostAsync(
            "/signup/identity/account/forgotpassword?returnUrl=%2Fconnect%2Fauthorize%3Fx%3D1",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = "nobody@signup.test",
                ["__RequestVerificationToken"] = token,
            }));

        (await sent.Content.ReadAsStringAsync()).ShouldContain("returnUrl=%2Fconnect%2Fauthorize%3Fx%3D1");
    }

    [Fact]
    public async Task The_email_template_renders_localized_html()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var renderer = scope.ServiceProvider.GetRequiredService<RazorEmailRenderer>();

        var model = new EmailModel(
            "Confirm your email", "Confirm your email", "Hi Sam, please confirm.",
            "Confirm email", "https://id.huia.test/master/confirm?x=1",
            "If the button does not work:", "Acme Corp", "#4f46e5", "en", "ltr");

        var html = await renderer.RenderAsync("/Emails/Views/Message.cshtml", model);

        html.ShouldContain("<html");
        html.ShouldContain("Confirm your email");
        html.ShouldContain("https://id.huia.test/master/confirm?x=1");
        html.ShouldContain("Acme Corp");
    }

    private static async Task<string> AntiforgeryAsync(HttpClient client, string url)
    {
        var html = await client.GetStringAsync(url);
        return AntiforgeryRegex().Match(html).Groups["v"].Value;
    }

    private static string ToLocal(string absolute) => new Uri(absolute).PathAndQuery;

    private static string? Query(string url, string key)
    {
        var q = url[(url.IndexOf('?', StringComparison.Ordinal) + 1)..];
        foreach (var pair in q.Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == key)
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }

    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="(?<v>[^"]+)""")]
    private static partial Regex AntiforgeryRegex();
}
