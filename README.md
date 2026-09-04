# Huia

Multi-tenant **OpenID Connect / OAuth 2.0 Identity Provider** for ASP.NET Core, plus the
first-party front-end tooling that consumes it.

Huia serves multiple isolated tenants over base-path routing (`/{tenant}/…`): per-tenant signing
keys, discovery documents, and issuers. Authorization Code + PKCE (with RFC 9126 Pushed
Authorization Requests), refresh, client credentials, device code, passwordless SMS one-time codes,
and external login via the OpenIddict client.

## Repository layout

```
src/
  dotnet/     The .NET solution (Huia.slnx) — three NuGet packages:
                Huia                       domain model, options, eventing, constants (no ASP.NET / EF dep)
                Huia.EntityFrameworkCore   HuiaDbContext, renamed entities, tenant-scoped stores
                Huia.AspNetCore            AddHuia()/UseHuia(), OpenIddict server + client, Razor account UI,
                                          passwordless SMS, key-lifecycle jobs, security headers
              plus tests/ (unit, integration, pen-test).
  nuxt/       huia-nuxt — a Nuxt 4 module: OIDC Authorization Code + PKCE + PAR, transparent
              server-side token refresh, dual-layer session (Nitro Storage for tokens; encrypted,
              chunked cookies for the session id + minimal claims). Tokens never reach the browser.
              See src/nuxt/SPEC.md for the full technical specification.

samples/      Huia.AppHost (Aspire), Huia.IdentityServer, Huia.External, Todo.Api,
              Todo.App (Nuxt), Huia.AdminUI (Nuxt)
tests/        Huia.E2ETests — full-stack Playwright E2E across the .NET hosts and the Nuxt apps
docs/         VitePress documentation site
```

`samples/`, `tests/` and `docs/` sit at the repo root because they span both stacks. The .NET
build infrastructure (`Huia.slnx`, `Directory.Build.props`, `Directory.Packages.props`,
`global.json`, `.editorconfig`) lives in `src/dotnet/`; the repo-root `Directory.Build.props` /
`Directory.Packages.props` re-export it so the relocated `samples/` and `tests/` projects still
get central package management and the shared `net10.0` target.

## Build

### .NET

```bash
cd src/dotnet
dotnet build Huia.slnx -c Release
dotnet test  Huia.slnx -c Release --filter "Category!=Container&Category!=E2E&Category!=PenTest"
```

`Container` tests need Docker (Testcontainers PostgreSQL). `PenTest` runs the sample host
out-of-process. `E2E` (repo-root `tests/`) needs the sample builds plus Playwright browsers.

### Nuxt auth module

```bash
cd src/nuxt
npm install
npm run dev:prepare
npm test          # Vitest unit + integration (mocked OP)
npm run dev       # playground on http://localhost:3000
```

### Docs

```bash
cd docs
npm install
npm run docs:build
```
