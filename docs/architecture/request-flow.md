# Request & token flow

## Interactive sign-in (Authorization Code + PKCE, with PAR)

```mermaid
sequenceDiagram
  participant RP as Relying party
  participant B as Browser
  participant S as OpenIddict server
  participant UI as Razor account UI
  participant K as HuiaTenantSigningKeyHandler

  RP->>S: POST /{tenant}/connect/par (client-auth, PKCE, form-encoded)
  S-->>RP: { request_uri, expires_in }
  RP->>B: 302 /{tenant}/connect/authorize?client_id&request_uri
  B->>S: GET /{tenant}/connect/authorize
  S-->>B: 302 /{tenant}/identity/account/login   (no session)
  B->>UI: password / SMS OTP / external provider
  UI-->>B: Set-Cookie huia.auth.{tenant}  +  302 back to /connect/authorize
  B->>S: GET /{tenant}/connect/authorize   (now authenticated)
  S-->>B: 302 redirect_uri?code
  B->>RP: code
  RP->>S: POST /{tenant}/connect/token (code, code_verifier, form-encoded)
  Note over S,K: AuthorizeAsync builds the principal (sub, role[], tenant, scopes)<br/>K swaps in the tenant's rotated RSA key for the access + id token<br/>issuer = {Issuer}/{tenant}
  S-->>RP: access_token (JWT) + id_token + refresh_token
```

## Token validation

Tokens are minted with the tenant's rotated key and per-tenant issuer, so three custom OpenIddict
handlers are inserted at fixed orders:

| Handler | Event | Order | Job |
|---|---|---|---|
| `HuiaTenantSigningKeyHandler` | `GenerateTokenContext` | `int.MinValue + 100_500` | override `SigningCredentials` for the **access token / id token only** (overriding the code or refresh token breaks `code` exchange) |
| `HuiaTenantTokenValidationHandler` | `ValidateTokenContext` | `int.MinValue + 101_000` | append the tenant's published keys to `IssuerSigningKeys` and `{Issuer}/{tenant}` to `ValidIssuers` |
| `HuiaTenantJwksHandler` | JWKS request | `int.MaxValue - 100_000` | serve the tenant's published keys at `/{tenant}/.well-known/jwks` |

A resource API on a **separate host** simply points its JWT bearer validation at
`{Issuer}/{tenant}/.well-known/openid-configuration` and gets the right keys over HTTP.

## Passwordless SMS

`amr=sms` is a cookie sign-in the account UI performs after verifying a one-time code; `/connect/authorize`
accepts it with zero OpenIddict changes. The full flow is in [Passwordless SMS](/dotnet/passwordless-sms).

## Relying party (Nuxt)

`huia-auth-nuxt` runs the RP half of the diagram on the Nitro server and keeps every token
server-side — see [the Nuxt module docs](/nuxt/overview).
