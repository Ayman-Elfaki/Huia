using Huia;
using Huia.Endpoints;
using Huia.EntityFrameworkCore;
using Huia.IdentityServer.Common;
using Huia.IdentityServer.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("identityserverdb")!;

builder.Services.AddDbContext<IdentityServerDbContext>(options => options.UseNpgsql(connectionString));

var issuer = builder.ResolveIssuer();

builder.Services.AddHuia(issuer, huia =>
    {
        // Distinct branding from Huia.TodoApi's own login page, so the two are visibly different identity
        // providers — this sample plays the role of a separate company's IdP that Huia.TodoApi federates
        // against (see huia-idp in Huia.TodoApi/Program.cs), not a second copy of the same app.
        huia.Branding.Title = "Acme Identity";

        // The only client this identity provider knows about: Huia.TodoApi itself, acting as a confidential
        // server-side relying party (it holds a client secret and exchanges the authorization code for
        // tokens from its own backend, via OpenIddict.Client's authorization code flow — never in the
        // browser).
        huia.Applications.AddServerSideWebApplication(app =>
        {
            app.SetClientId("todoapi");
            app.SetClientSecret(builder.Configuration["Oidc:TodoApiClientSecret"]!);
            app.SetDisplayName("Huia Todo Sample API");

            app.AddRedirectUri(builder.Configuration["Oidc:TodoApiRedirectUri"]!);
            // Same client, a second callback path — Huia.TodoApi's own "huia-idp-partial" registration
            // (see its Program.cs) reuses this one client but requests no "profile" scope, to demonstrate
            // and test the missing-claims path through ExternalLoginConfirmation.
            app.AddRedirectUri(builder.Configuration["Oidc:TodoApiPartialRedirectUri"]!);
            app.AddPostLogoutRedirectUri(builder.Configuration["Oidc:TodoApiPostLogoutRedirectUri"]!);
            app.SetHomeUri(builder.Configuration["Oidc:TodoApiHomeUri"]!);

            app.AllowScopes("profile", "email");
        });

        // Email+password only — this sample only needs to prove out the external-login round trip, not
        // demonstrate every Huia feature a second time. Huia.TodoApi/Program.cs already does that.
        huia.Authentication.UseEmailAndPasswordFlow();

        huia.KeysManagement.UseAutomaticKeyManagement();
    })
    .WithEntityFrameworkStores<IdentityServerDbContext>();

var app = builder.Build();

app.MapDefaultEndpoints();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<IdentityServerDbContext>().Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseHuia();

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapHuiaConnectEndpoints();
app.MapRazorPages();

app.Run();
