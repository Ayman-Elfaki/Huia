# Email testing with Mailpit

The library sends transactional email (confirm-your-email, reset-your-password) through
`IHuiaEmailSender` — a MailKit SMTP client that no-ops when no host is configured. The Aspire sample
wires [Mailpit](https://github.com/axllent/mailpit) as the SMTP sink so the flows can be exercised
locally and in CI without a real provider.

## AppHost wiring

`samples/Huia.AppHost/AppHost.cs` uses the `CommunityToolkit.Aspire.Hosting.MailPit` integration and
points the identity server at it:

```csharp
var mailpit = builder.AddMailPit("mailpit")
    .WithImageTag("v1.21")             // pinned to the tag CI pre-pulls
    .WithDataVolume("mailpit-data");   // persist captured mail across `aspire run` sessions

builder.AddProject<Projects.Huia_IdentityServer>("huia-identityserver")
    .WithReference(mailpit)
    .WaitFor(mailpit)
    .WithEnvironment(context =>
    {
        // Huia binds discrete Huia:Email:* keys, not an Aspire connection string.
        var smtp = mailpit.GetEndpoint("smtp");
        context.EnvironmentVariables["Huia__Email__Host"] = smtp.Property(EndpointProperty.Host);
        context.EnvironmentVariables["Huia__Email__Port"] = smtp.Property(EndpointProperty.Port);
        context.EnvironmentVariables["Huia__Email__UseSsl"] = "false";
        context.EnvironmentVariables["Huia__Email__FromAddress"] = "no-reply@huia.local";
    });
```

The integration names the endpoints `smtp` and `http` (the web UI + REST API). The E2E suite runs
with `Huia:EnableE2E=true`, which skips the data volume so every run starts with an empty sink.

## Config binding

The library only reads `HuiaOptions.Email` from the `AddHuia(...)` delegate, so the sample binds the
`Huia:Email` section itself:

```csharp
huia.ConfigureEmail(email => builder.Configuration.GetSection("Huia:Email").Bind(email));
```

When `Huia:Email:Host` is set the real MailKit sender is used. When it is **not** set (a bare
`dotnet run` of the sample, or the in-process integration tests) the sample falls back to an
in-memory `CapturingEmailSender` and the `/e2e-mail` debug endpoint.

## Viewing and asserting

Run the AppHost and open the `mailpit` resource's `http` endpoint to read captured messages. Tests
query the REST API:

- `GET /api/v1/search?query=to:<address>` — list matching messages
- `GET /api/v1/message/<id>` — full message (`Text` / `HTML` bodies)
- `DELETE /api/v1/messages` — clear the inbox

`tests/Huia.E2ETests/MailFlowE2ETests.cs` boots the AppHost via `Aspire.Hosting.Testing`
(`AppHostFixture`), posts the forgot-password / register forms on the identity server, then uses
`MailpitClient.WaitForActionUrlAsync` to pull the reset / confirmation link out of the delivered
message and follow it. The suite needs Docker (Mailpit + Postgres images) and is tagged
`Category=E2E`.
