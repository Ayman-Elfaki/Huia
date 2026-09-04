---
layout: home
hero:
  name: Huia
  text: Multi-tenant OIDC / OAuth 2.0 for ASP.NET Core
  tagline: Isolated tenants over base-path routing, per-tenant signing keys, passwordless SMS, an opinionated account UI — and a first-party Nuxt 4 client module.
  actions:
    - theme: brand
      text: Getting started
      link: /guide/getting-started
    - theme: alt
      text: .NET reference
      link: /dotnet/options
    - theme: alt
      text: Nuxt module
      link: /nuxt/overview
features:
  - title: Tenant isolation
    details: HuiaDbContext derives from Finbuckle's MultiTenantIdentityDbContext — a global query filter on read, write-time enforcement on save, and named tenant-scoped composite indexes.
  - title: Per-tenant keys
    details: Each tenant signs with its own rotated RSA key; Quartz jobs drive the pending → active → rotated → retired lifecycle, and custom OpenIddict handlers inject the tenant key + issuer.
  - title: Flows
    details: Authorization code + PKCE (with optional PAR), refresh, client credentials, device code, passwordless SMS one-time codes, and external login through the OpenIddict client.
  - title: First-party Nuxt module
    details: huia-nuxt runs the relying-party flow on the Nitro server — PKCE + PAR, transparent refresh, and a dual-layer session that keeps every token off the browser.
---
