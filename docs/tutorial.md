# Huia tutorial

This tutorial runs the repository's complete sample so you can see Huia issuing tokens, serving its
Identity UI, protecting an API, and supporting a separate web client. It is intended as the quickest path
from a fresh clone to a working sign-in flow.

## Prerequisites

- .NET 10 SDK
- Docker Desktop, running
- Node.js, for the Next.js and Nuxt sample applications

The sample uses .NET Aspire to start PostgreSQL, Mailpit, the Huia API, the Todo web app, the Admin UI, and
a second Huia instance used as an external identity provider.

## Start the sample

From the repository root, run:

```bash
dotnet run --project samples/Huia.AppHost/Huia.AppHost.csproj
```

The terminal prints the Aspire dashboard address. Open it and wait until the resources are running. The
dashboard also exposes the generated resource endpoints; the sample pins the main application ports to
these local URLs:

| Resource | URL | Purpose |
|---|---|---|
| Todo web app | `http://localhost:3000` | User-facing Todo application |
| Todo API | `https://localhost:5041` | Huia authorization server and Todo API |
| Admin UI | `http://localhost:3100` | Admin console |
| Identity server | `https://localhost:5051` | External OIDC provider sample |

Development certificates are used by the two HTTPS resources. Your browser may ask you to accept the local
certificate the first time you open one of them.

## Register and use a Todo

1. Open `http://localhost:3000` and choose **Sign in**.
2. Choose **Create an account**, then register with an email address and password.
3. After Huia completes the authorization-code flow, the browser returns to the Todo app.
4. Add a todo, mark it complete, and delete it.

The browser is using the `todo-web` confidential client. Its redirect URI, allowed scopes, and secret are
registered by `Huia.TodoApi`; the Next.js server exchanges the authorization code and keeps the access token
server-side. The API then validates that token locally before serving the Todo endpoints.

The client registration is in [`Huia.TodoApi/Program.cs`](../samples/Huia.TodoApi/Program.cs), the server-side
OIDC configuration is in [`Huia.TodoApp/src/auth.ts`](../samples/Huia.TodoApp/src/auth.ts), and the protected
CRUD endpoints are in [`Huia.TodoApi/Endpoints`](../samples/Huia.TodoApi/Endpoints).

## Inspect registration email

The sample sends confirmation and password-reset email through Mailpit instead of delivering real mail.
Open the Mailpit resource from the Aspire dashboard to inspect messages. The SMTP adapter is implemented in
[`SmtpEmailSender.cs`](../samples/Huia.TodoApi/Email/SmtpEmailSender.cs).

## Open the Admin UI

Open `http://localhost:3100`. The sample seeds this development administrator:

```text
Email:    admin@example.com
Password: Admin123!Demo
```

The Admin UI authenticates as the confidential `admin-ui` client. Its server proxies requests to Huia's admin
endpoints, so the access token does not need to reach the browser. The API gates those endpoints with both the
`Admin` role and the client presenter. See [`Huia.AdminUI/README.md`](../samples/Huia.AdminUI/README.md) and
the `MapHuiaAdminEndpoints()` registration in [`Huia.TodoApi/Program.cs`](../samples/Huia.TodoApi/Program.cs).

## Try another sign-in method

The Todo API enables email/password, passwordless phone sign-in, and an external OIDC provider when its
configuration is present. The local external provider is the separate `Huia.IdentityServer` resource, so it
can be tested without a Google or Microsoft account. Its complete challenge and callback path is covered by
[`ExternalIdentityServerLoginE2ETests.cs`](../tests/Huia.Tests.E2E/ExternalIdentityServerLoginE2ETests.cs).

For production provider configuration, see [External providers](external-providers.md). For SMS delivery,
rate limiting, and the security model behind phone sign-in, see [Passwordless phone sign-in](passwordless.md).

## What to read next

- [Getting started](getting-started.md) for a minimal ASP.NET Core integration.
- [Architecture](architecture.md) for the request pipeline, stores, claims, and event model.
- [Key management](key-management.md) before deploying outside local development.
- [Custom stores](custom-store.md) when EF Core is not your persistence layer.
