# Security Model

Huia is built from the ground up for high-assurance, multi-tenant and headless identity workloads in ASP.NET Core. This page outlines the core architectural security guarantees, data isolation boundaries, and mitigation strategies implemented across the platform.

## Multi-Tenant Isolation Model

Huia operates on a base-path tenant resolution architecture (`/{tenant}/...`). Isolation is enforced strictly across four layers:

1. **Database Isolation (`HuiaDbContext`)**:
   - Every tenant-scoped entity implements `IHuiaTenantScoped` containing a `TenantId`.
   - Global query filters (`TenantId == TenantInfo.Id`) automatically constrain all reads to the current tenant context.
   - `EnforceMultiTenant()` validates and stamps `TenantId` on write operations, preventing cross-tenant injection.
   - Unique constraints are enforced via composite database indexes (`{TenantId, Normalized*}`).

2. **Cryptographic Key Isolation**:
   - Each tenant receives dedicated RSA signing and encryption keys.
   - Private key material is encrypted at rest using ASP.NET Core Data Protection.
   - In-memory keys are held in `IHuiaKeyRing` keyed strictly by tenant ID and key ID (`kid`).

3. **Token & Issuer Validation**:
   - Tokens issued by tenant $A$ cannot be used against tenant $B$.
   - `HuiaTenantTokenValidationHandler` validates both the issuer URL (`/{tenant}`) and pins the expected signing keys for that specific tenant. Cross-tenant token replay yields `401 Unauthorized (invalid_token)` before request handling or authorization logic executes.

4. **Cookie & Session Scoping**:
   - Cookies are isolated per-tenant.
   - `WithPerTenantAuthentication()` embeds a `__tenant__` claim and rejects any cookie where `__tenant__ != request_tenant`.
   - Per-tenant cookie naming prevents browser session bleeding across tenants on the same domain.

---

## Token Security & OAuth 2.0 Hardening

- **Authorization Code + PKCE**: Proof Key for Code Exchange (RFC 7636) is strictly enforced for all public clients (`RequirePkce()`).
- **Pushed Authorization Requests (PAR)**: Implements RFC 9126 at `/{tenant}/connect/par`. PAR moves complex authorization parameters from public front-channel query strings to back-channel direct HTTP calls, eliminating authorization request tampering, URL length limits, and sensitive data leakage into access logs and `Referer` headers.
- **Client Credentials & Refresh Tokens**: Refresh tokens are cryptographically bound to the client and subject. Revocation of an active grant immediately invalidates associated refresh tokens.

---

## Passwordless SMS & OTP Hardening

Passwordless phone authentication presents unique threat models. Huia applies extensive countermeasures:

- **Salted Hashing**: Verification codes are never stored in plaintext. They are hashed as `SHA-256(salt || code)` using cryptographically secure random salts.
- **Constant-Time Verification**: Verification comparisons use `CryptographicOperations.FixedTimeEquals` to eliminate timing side-channels.
- **Single-Use Enforcement**: Codes are marked consumed immediately upon use, preventing replay.
- **Brute-Force & Attempt Caps**: Failed verification attempts increment an attempt counter; exceeding the cap instantly invalidates the OTP.
- **Dual-Tier Rate Limiting**:
  - Outbound rate limiting prevents SMS bombing before telecommunication credits are spent.
  - Inbound verification throttling blocks high-frequency brute-force attempts.
- **Timing Invariance on Account Enumeration**: In phone sign-in with auto-provisioning disabled, requests for unregistered numbers execute with identical timing and return matching generic responses to prevent user enumeration.

---

## External Provider & Account Linking

- **No Blind Linking**: Account linking by email is disabled by default.
- **Strict Verification Prerequisite**: When `AllowEmailLinking` is explicitly enabled, linking requires that the local account has `EmailConfirmed == true` AND the external identity provider vouches for email verification (`email_verified == true`). If either condition is not met, the sign-in is rejected (`ExternalEmailLinkOutcome.Blocked`).
- **Last Sign-in Guard**: `HuiaUserManager.CanRemoveExternalLoginAsync` prevents users from disassociating an external login if doing so would leave them without any authentication method (no password, no phone, and no remaining logins).

---

## Cookie Policy & Transport Security

Huia enforces conservative cookie defaults across all flows:

| Cookie | SameSite | Scope | Purpose |
|---|---|---|---|
| `huia.auth` | `Lax` | Tenant-scoped | Primary authentication session |
| `huia.flow` | `Strict` | Tenant-scoped | Ephemeral multi-step flow state |
| `huia.csrf` | `Strict` | App-scoped | Anti-forgery validation token |
| `huia.external` | `None` + `Secure` | App-scoped | Cross-site correlation cookie for external IdP redirects |

All cookies automatically receive the `Secure` flag in production environments. Transport security can be customized via `DisableTransportSecurityRequirement` strictly for local testing.

---

## Comprehensive Security Controls Matrix

The following matrix summarizes the mitigations implemented across Huia:

| Security Concern | Implementation / Mitigation |
|---|---|
| **Cross-tenant data access** | `HuiaDbContext` global query filters (`TenantId == TenantInfo.Id`) on read + `EnforceMultiTenant()` on write; composite unique indexes `{TenantId, Normalized*}`. |
| **Cross-tenant token replay** | `HuiaTenantTokenValidationHandler` pins the tenant's keys + issuer; token from tenant A is rejected at tenant B. |
| **Session bleed across tenants** | `WithPerTenantAuthentication()` rejects cookies whose `__tenant__ != request_tenant`; distinct cookie name prefixes. |
| **Authorization code interception** | Enforced PKCE with S256 challenge verification. |
| **Auth-request tampering & log leaks** | RFC 9126 PAR at `/{tenant}/connect/par`; optional client enforcement via `RequirePushedAuthorizationRequests()`. |
| **OTP brute force / reuse** | Salted SHA-256, single-use invalidation, constant-time comparison, attempt caps, outbound & inbound rate limits. |
| **Account enumeration on phone login** | Timing-invariant responses whether number exists or not when auto-provisioning is off. |
| **External sign-in account takeover** | Link-by-email disabled by default; requires confirmed email on both ends when enabled; blocks unverified merges. |
| **Removing last sign-in method** | `HuiaUserManager.CanRemoveExternalLoginAsync` checks against complete account lockouts. |
| **Lockout bypass on token grants** | Resource Owner Password Credentials flow delegates to `CheckPasswordSignInAsync(lockoutOnFailure: true)`; `/connect/token` verifies `IsLockedOutAsync`. |
| **Open redirect attacks** | `IReturnUrlProtector` with Data Protection tokenization and same-origin validation for `returnUrl` and `post_logout_redirect_uri`. |
| **Private key exposure** | Private keys encrypted at rest via ASP.NET Core Data Protection; memory caches store unwrapped keys only by `kid`. |
| **Insecure transport (HTTP)** | OpenIddict transport guard, HTTPS redirection, and mandatory `Secure` cookie flags. |
| **HTTP header hardening** | Opt-in `AddHuiaSecurityHeaders()` providing per-request nonce CSP, HSTS, and referrer policies. |
