---
layout: home
hero:
  name: Huia
  text: Multi-tenant OIDC / OAuth 2.0 for ASP.NET Core 10
  tagline: Isolated tenants over base-path routing, per-tenant signing keys, passwordless SMS, and an opinionated account UI.
  actions:
    - theme: brand
      text: Getting started
      link: /guide/getting-started
    - theme: alt
      text: Architecture
      link: /architecture/overview
features:
  - title: Tenant isolation
    details: Custom EF Core stores append an explicit TenantId predicate to every query. No ambient tenant, no global query filter.
  - title: Per-tenant keys
    details: Each tenant signs with its own rotated RSA key; Quartz jobs drive the pending -> active -> rotated -> retired lifecycle.
  - title: Flows
    details: Authorization code + PKCE (with optional PAR), refresh, client credentials, device code, and passwordless SMS one-time codes.
