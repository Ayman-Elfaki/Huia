# Admin console (`Huia.AdminUI`)

`samples/Huia.AdminUI` is a Nuxt 4 admin console that puts a UI on every `/admin/*` endpoint the
library exposes. It is a **confidential** client built on the first-party
[`huia-auth-nuxt`](/nuxt/overview) module and signs in against the `master` tenant;
[`nuxt-api-party`](https://nuxt-api-party.byjohann.dev/) proxies each call to
`{{issuer}}/master/admin/...` and a Nitro plugin adds the access token via `getAccessToken(event)`,
so the token never reaches the browser.

## What it needs

The admin API is mounted by the host without a policy; the sample attaches one:

```csharp
app.MapHuiaAdminEndpoints()
    .RequireAuthorization(p => p.RequireTenants("master").RequireRole(HuiaConstants.Roles.Administrator));
```

So a signed-in user must be in the `master` tenant, hold the `huia.administrator` role, and have
requested the `roles` scope at sign-in (the console does). The Aspire sample seeds
`admin@huia.local` / `Admin1!Pass` in that role.

## Pages

| Route | Endpoint(s) | Notes |
|-------|-------------|-------|
| `/` | `GET admin/tenants` | Dashboard — tenant count, per-tenant sign-in methods, quick links |
| `/tenants` | `GET admin/tenants` | Read-only |
| `/users` | `GET/POST/PUT/DELETE admin/users` (+ `…/{id}/roles`) | Full CRUD; create a password or phone account; assign / unassign roles inline; keyset paged, tenant filter |
| `/roles` | `GET/POST/PUT/DELETE admin/roles` | Per-tenant roles; a role with members can't be deleted until they're unassigned |
| `/clients` | `GET/POST/PUT/DELETE admin/clients` | Full CRUD for dynamic clients (static ones are read-only); keyset paged |
| `/keys` | `GET/POST admin/keys` + `POST admin/keys/{id}/revoke` | Create a pending or active key, revoke a key; keyset paged, tenant filter |
| `/scopes` | `GET/POST/PUT/DELETE admin/scopes` | Full CRUD, tenant filter |
| `/profile` | `GET/PUT manage/profile` | The signed-in user's own details |

Lists use the admin API's keyset pagination (`after` / `before` / `size`); the `useKeysetList`
composable (`app/composables/useKeysetList.ts`) walks the cursor off the first / last visible row.

## Static vs. dynamic scopes and clients

A client or scope is **static** when it was seeded from the options tree (`tenant.AddScope(...)`,
`tenant.AddServerSideWebApplication(...)`, …) and **dynamic** when it was created at runtime through
`POST /admin/scopes` or `POST /admin/clients`. The distinction is persisted in the OpenIddict entity's
`Properties` under `huia:origin` (`HuiaConstants.Origins.Static` / `Dynamic`); a row with no marker is
treated as static.

The console shows the origin as a badge. For a **static** scope or client the Edit and Delete buttons
are disabled, and the API backs this up — `PUT` / `DELETE` on a code-defined scope or client returns
`409 Conflict`. Create always produces a dynamic entity.

## Signing keys

`POST /admin/keys` mints a new RSA key for a tenant, either **pending** (published in the JWKS but not
signing) or **active** (`activate: true`, which demotes the tenant's current active key to
*rotated*). `POST /admin/keys/{id}/revoke` moves a key to *retired* — it stops signing and validating
at once; the call is refused with `409` if it is the tenant's only active key. `DELETE /admin/keys/{id}`
hard-deletes a key, and only a *retired* one.

## Running it

`Huia.AdminUI` is wired into `samples/Huia.AppHost` as `admin-app` on port 3001:

```bash
dotnet run --project samples/Huia.AppHost
# then open http://localhost:3001 and sign in as admin@huia.local / Admin1!Pass
```

`tests/Huia.E2ETests/AdminUiE2ETests.cs` drives the console through the same AppHost via
`Aspire.Hosting.Testing` + Playwright (`Category=E2E`, needs Docker).
