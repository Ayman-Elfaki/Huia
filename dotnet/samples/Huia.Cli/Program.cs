using System.CommandLine;
using System.Text.Json;
using Huia.Cli;

var issuerOption = new Option<string>("--issuer") { Description = "The Huia issuer base URL.", DefaultValueFactory = _ => "https://localhost:5310", Recursive = true };
var tenantOption = new Option<string>("--tenant") { Description = "The tenant to authenticate against.", DefaultValueFactory = _ => "master", Recursive = true };
var clientIdOption = new Option<string>("--client-id") { Description = "The device client id.", DefaultValueFactory = _ => "huia-cli", Recursive = true };
var clientSecretOption = new Option<string>("--client-secret") { Description = "The device client secret.", DefaultValueFactory = _ => "huia-cli-secret", Recursive = true };
var scopeOption = new Option<string>("--scope") { Description = "Requested scopes.", DefaultValueFactory = _ => "openid profile email roles offline_access" };

var root = new RootCommand("huia - admin CLI for a Huia identity provider (device-code sign-in).");
foreach (var option in new Option[] { issuerOption, tenantOption, clientIdOption, clientSecretOption })
{
    root.Options.Add(option);
}

var store = new TokenStore();
var clock = TimeProvider.System;

HuiaClient ClientFor(ParseResult parse) => new(
    NewHttp(),
    parse.GetValue(issuerOption)!.TrimEnd('/'),
    parse.GetValue(tenantOption)!,
    parse.GetValue(clientIdOption)!,
    parse.GetValue(clientSecretOption)!,
    clock);

static HttpClient NewHttp()
{
    // The dev issuer uses the ASP.NET Core dev certificate; trust it for the sample CLI.
    var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator };
    return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
}

static async Task<int> Guarded(Func<Task<int>> body)
{
    try
    {
        return await body();
    }
    catch (HuiaCliException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

async Task<string> RequireAccessTokenAsync(ParseResult parse, CancellationToken ct)
{
    var cached = store.Read() ?? throw new HuiaCliException("Not signed in. Run `huia login`.");
    if (!cached.IsExpired(clock))
    {
        return cached.AccessToken;
    }

    if (cached.RefreshToken is null)
    {
        throw new HuiaCliException("The session has expired and cannot be refreshed. Run `huia login`.");
    }

    var refreshed = await ClientFor(parse).RefreshAsync(cached.RefreshToken, ct);
    store.Write(refreshed);
    return refreshed.AccessToken;
}

var login = new Command("login", "Sign in with the OAuth 2.0 device-authorization grant.");
login.Options.Add(scopeOption);
login.SetAction((parse, ct) => Guarded(async () =>
{
    var tokens = await ClientFor(parse).DeviceLoginAsync(parse.GetValue(scopeOption)!, Console.Out, ct);
    store.Write(tokens);
    Console.WriteLine($"Signed in. Token cache: {store.FilePath}");
    return 0;
}));

var logout = new Command("logout", "Clear the local token cache.");
logout.SetAction(_ =>
{
    Console.WriteLine(store.Clear() ? "Signed out." : "Nothing to do (no cached session).");
    return 0;
});

var whoami = new Command("whoami", "Print the claims from the userinfo endpoint.");
whoami.SetAction((parse, ct) => Guarded(async () =>
{
    var accessToken = await RequireAccessTokenAsync(parse, ct);
    var info = await ClientFor(parse).UserInfoAsync(accessToken, ct);
    Console.WriteLine(JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}));

var token = new Command("token", "Print the current access token.");
token.SetAction((parse, ct) => Guarded(async () =>
{
    Console.WriteLine(await RequireAccessTokenAsync(parse, ct));
    return 0;
}));

var scopesList = new Command("list", "List the custom scopes for a tenant (admin).");
var scopesForOption = new Option<string?>("--for") { Description = "The tenant whose scopes to list (defaults to --tenant)." };
scopesList.Options.Add(scopesForOption);
scopesList.SetAction((parse, ct) => Guarded(async () =>
{
    var access = await RequireAccessTokenAsync(parse, ct);
    var issuer = parse.GetValue(issuerOption)!.TrimEnd('/');
    var adminTenant = parse.GetValue(tenantOption)!;
    var target = parse.GetValue(scopesForOption) ?? adminTenant;

    using var http = NewHttp();
    using var request = new HttpRequestMessage(HttpMethod.Get, $"{issuer}/{adminTenant}/admin/scopes?tenant={Uri.EscapeDataString(target)}");
    request.Headers.Authorization = new("Bearer", access);
    using var response = await http.SendAsync(request, ct);
    var body = await response.Content.ReadAsStringAsync(ct);
    if (!response.IsSuccessStatusCode)
    {
        throw new HuiaCliException($"admin/scopes returned {(int)response.StatusCode}: {body}");
    }

    Console.WriteLine(body);
    return 0;
}));

var scopes = new Command("scopes", "Manage per-tenant custom OAuth scopes (admin).");
scopes.Subcommands.Add(scopesList);

root.Subcommands.Add(login);
root.Subcommands.Add(logout);
root.Subcommands.Add(whoami);
root.Subcommands.Add(token);
root.Subcommands.Add(scopes);

return await root.Parse(args).InvokeAsync();

/// <summary>Test entry-point marker.</summary>
public partial class Program;
