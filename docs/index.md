---
layout: home
hero:
  name: Huia
  text: OIDC/OAuth 2.0 and headless identity for ASP.NET Core
  tagline: Huia.OpenId — multi-tenant, base-path routing, per-tenant signing keys, passwordless SMS, an opinionated account UI. Huia.Headless — single-tenant bearer tokens, no redirects, bring your own login form. Two first-party Nuxt 4 client modules to match.
  actions:
    - theme: brand
      text: Getting started
      link: /guide/getting-started
    - theme: alt
      text: .NET reference
      link: /dotnet/options
    - theme: alt
      text: Nuxt modules
      link: /nuxt/overview
features:
  - title: Tenant isolation (Huia.OpenId)
    details: HuiaDbContext derives from Finbuckle's MultiTenantIdentityDbContext — a global query filter on read, write-time enforcement on save, and named tenant-scoped composite indexes.
  - title: Per-tenant keys (Huia.OpenId)
    details: Each tenant signs with its own rotated RSA key; Quartz jobs drive the pending → active → rotated → retired lifecycle, and custom OpenIddict handlers inject the tenant key + issuer.
  - title: Flows (Huia.OpenId)
    details: Authorization code + PKCE (with optional PAR), refresh, client credentials, device code, passwordless SMS one-time codes, and external login through the OpenIddict client.
  - title: Bearer-token API (Huia.Headless)
    details: Single-tenant JSON register/login/passkey endpoints via ASP.NET Core Identity's MapIdentityApi — no OpenIddict, no redirects, for apps that own their own login form.
  - title: Two first-party Nuxt modules
    details: nuxt-huia-oidc runs the OIDC relying-party flow on the Nitro server; nuxt-huia-headless drives the JSON register/login API. Both keep every token off the browser via a dual-layer session.
---
