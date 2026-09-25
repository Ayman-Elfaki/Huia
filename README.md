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
  dotnet/     The .NET solution (Huia.slnx) — NuGet packages:
                Huia                               domain model, options, eventing, store abstractions (no ASP.NET / EF dep)
                Huia.OpenId                        OpenIddict server + client, Razor account UI, PAR, SMS, passkeys
                Huia.OpenId.EntityFrameworkCore    HuiaDbContext, tenant-scoped stores
                Huia.Headless                      pure JSON authentication endpoints & bearer tokens
                Huia.Headless.EntityFrameworkCore  headless store implementations
              plus tests/ (unit, integration, pen-test).
  javascript/
    shared/
      huia-auth-core/   Platform-agnostic TypeScript core engine: iron-webcrypto session sealing,
                        cookie chunking, token refresh mutex, HuiaOidcHelper (PAR + PKCE), and
                        HuiaHeadlessClient. Shared by all Next.js and Nuxt packages.

    next/
      next-huia-oidc/       Next.js 14/15 library for Huia.OpenId (Route Handlers, Server Components,
                            iron-webcrypto dual-layer session, HuiaOidcProvider, useUserSession).
      next-huia-headless/   Next.js 14/15 library for Huia.Headless (Route Handlers, password auth,
                            SMS OTP, external login, HuiaHeadlessProvider, useHuia).

    nuxt/
      nuxt-huia-oidc/       Nuxt 4 module for Huia.OpenId (built on huia-auth-core).
      nuxt-huia-headless/   Nuxt 4 module for Huia.Headless (built on huia-auth-core).

samples/
  Shared/     Huia.AppHost (Aspire), Huia.Cli, Huia.External
  Todo/       Todo.Api, Todo.Nuxt (Nuxt), Todo.Next (Next.js), Todo.Admin (Nuxt), Todo.IdentityServer
  Shop/       Shop.Api, Shop.Nuxt (Nuxt), Shop.Next (Next.js)
tests/        Huia.E2ETests — full-stack Playwright E2E across .NET hosts, Nuxt, and Next.js apps
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

### TypeScript Packages & Modules

```bash
# Shared Core
cd src/shared/huia-auth-core
npm install && npm test && npm run build

# Next.js Libraries
cd src/next/next-huia-oidc
npm install && npm test && npm run build

cd src/next/next-huia-headless
npm install && npm test && npm run build

# Nuxt Modules
cd src/nuxt/nuxt-huia-oidc
npm install && npm test && npm run build

cd src/nuxt/nuxt-huia-headless
npm install && npm test && npm run build
```

### Docs

```bash
cd docs
npm install
npm run docs:build
```
