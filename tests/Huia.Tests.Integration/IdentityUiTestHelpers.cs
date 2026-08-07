using System.Text.RegularExpressions;

namespace Huia.Tests.Integration;

/// <summary>
/// Drives Huia's server-rendered Identity UI forms (antiforgery token + all) over a real HttpClient.
/// </summary>
internal static partial class IdentityUiTestHelpers
{
    /// <summary>
    /// Scrapes the antiforgery hidden field FormTagHelper renders into every Identity UI form.
    /// </summary>
    public static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string pageUrl)
    {
        using var response = await client.GetAsync(pageUrl);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();
        var match = AntiforgeryTokenPattern().Match(html);
        if (!match.Success)
        {
            throw new InvalidOperationException($"No antiforgery token found on {pageUrl}.");
        }

        return match.Groups["token"].Value;
    }

    /// <summary>
    /// Registers a new account through the real form post (not a direct API call), following exactly what a
    /// browser submits — including the antiforgery token — so a valid, already-authenticated session (the
    /// sample doesn't require confirmed accounts) ends up in <paramref name="client"/>'s cookie container.
    /// </summary>
    public static async Task<HttpResponseMessage> RegisterAsync(
        HttpClient client, string email, string password, string? returnUrl = null)
    {
        var registerUrl = "/identity/account/register" +
                          (returnUrl is null ? "" : $"?returnUrl={Uri.EscapeDataString(returnUrl)}");
        var token = await GetAntiforgeryTokenAsync(client, registerUrl);

        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Input.FirstName"] = "Test",
            ["Input.LastName"] = "User",
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = password,
            ["returnUrl"] = returnUrl ?? string.Empty,
            ["__RequestVerificationToken"] = token,
        };

        return await client.PostAsync(registerUrl, new FormUrlEncodedContent(form));
    }

    /// <summary>
    /// Signs in to an already-registered account through the real form post, the same way
    /// <see cref="RegisterAsync"/> does for a brand-new one. Driving this against a second
    /// <see cref="HttpClient"/> (its own separate cookie container) for the same account is how tests
    /// simulate "the same user, signed in from a second browser" — a genuinely separate session.
    /// </summary>
    public static async Task<HttpResponseMessage> SignInAsync(
        HttpClient client, string email, string password, string? returnUrl = null)
    {
        var loginUrl = "/identity/account/login" +
                       (returnUrl is null ? "" : $"?returnUrl={Uri.EscapeDataString(returnUrl)}");
        var token = await GetAntiforgeryTokenAsync(client, loginUrl);

        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["returnUrl"] = returnUrl ?? string.Empty,
            ["__RequestVerificationToken"] = token,
        };

        return await client.PostAsync(loginUrl, new FormUrlEncodedContent(form));
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"\\s+type=\"hidden\"\\s+value=\"(?<token>[^\"]+)\"",
        RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AntiforgeryTokenPattern();
}