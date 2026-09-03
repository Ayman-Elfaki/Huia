# Huia.Cli

A small admin CLI for a Huia identity provider. It signs in with the OAuth 2.0
**device-authorization grant** (`connect/device` → `connect/verify`) against the `huia-cli`
device client seeded on the `master` tenant.

```bash
# start the identity provider first (samples/Huia.IdentityServer), then:
dotnet run --project samples/Huia.Cli -- login
#   open the printed URL, sign in as admin@huia.local / Admin1!Pass, enter the code
dotnet run --project samples/Huia.Cli -- whoami
dotnet run --project samples/Huia.Cli -- scopes list --for todo
dotnet run --project samples/Huia.Cli -- logout
```

| Command | Purpose |
|---|---|
| `login` | Device-code sign-in; caches tokens under `~/.huia/tokens.json`. |
| `logout` | Delete the local token cache. |
| `whoami` | Print the `connect/userinfo` claims (refreshing the token if needed). |
| `token` | Print the current access token. |
| `scopes list [--for <tenant>]` | List a tenant's custom scopes via `/master/admin/scopes` (admin). |

Global options: `--issuer` (default `https://localhost:5310`), `--tenant` (default `master`),
`--client-id`, `--client-secret`. Set `HUIA_CLI_HOME` to relocate the token cache.
