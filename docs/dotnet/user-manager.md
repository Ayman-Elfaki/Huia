# `HuiaUserManager` & user types

`HuiaUserManager : UserManager<HuiaUser>` is registered by `AddHuia()` via
`.AddUserManager<HuiaUserManager>()`, so `SignInManager<HuiaUser>` and every
`UserManager<HuiaUser>` resolution in your code resolve to it. Inject `HuiaUserManager` directly to
reach the extra members.

## User types

```csharp
public enum HuiaUserType { Unknown, Password, External, Phone }
```

`GetUserTypeAsync(user)` classifies an account with precedence **password → external → phone**: a
record that has a password is a `Password` account even if it also has an external login. The type
governs which contact details an account may change through `/manage/*`:

| Type | Owns | May set | May not set |
|---|---|---|---|
| `Password` | an email + password | email | phone number |
| `External` | an email + external login(s) | email | phone number |
| `Phone` | a confirmed phone number (also the username) | phone number | email address |
| `Unknown` | nothing yet | — | — |

## Members

| Member | What it does |
|---|---|
| `Task<HuiaUserType> GetUserTypeAsync(HuiaUser)` | classification, as above |
| `Task<bool> CanRemoveExternalLoginAsync(HuiaUser)` | `true` unless unlinking would leave the account with no password, no phone number and one or zero logins |
| `Task<HuiaUser?> FindByPhoneNumberAsync(string e164)` | looks up the **`PhoneNumber` column** (not the username); tenant-scoped by `HuiaDbContext`'s global query filter |
| `Task<HuiaUserCreation> CreatePhoneUserAsync(string tenantId, string e164, string firstName, string lastName)` | creates a phone-login account — `UserName == PhoneNumber == e164`, `PhoneNumberConfirmed`, no password, no email |
| `Task<IdentityResult> AddExternalLoginAsync(HuiaUser, string provider, string key, string displayName)` | thin wrapper over `AddLoginAsync(new UserLoginInfo(...))` |
| `Task<HuiaUserCreation> CreateExternalUserAsync(string tenantId, string? email, string firstName, string lastName, string provider, string key, string displayName)` | derives the username (the email, or `slug(displayName)-{key}` when there is none), creates the account, and attaches the provider login |
| `Task<(ExternalEmailLinkOutcome Outcome, HuiaUser? User)> TryLinkExternalByEmailAsync(string email, bool providerVouches, bool accountLinkingEnabled, string provider, string key, string displayName)` | a logged-out external sign-in whose email matches a local account — see below |

`HuiaUserCreation` is a `readonly record struct (IdentityResult Result, HuiaUser User)` with
`Succeeded => Result.Succeeded`; the `User` is persisted only when `Result.Succeeded`.

## `TryLinkExternalByEmailAsync`

```csharp
public enum ExternalEmailLinkOutcome { NoMatch, Linked, Blocked }
```

- **`NoMatch`** — no local account owns the email → the caller should provision a new account.
- **`Linked`** — `accountLinkingEnabled` (the tenant's `ext.EnableAccountsLinking()`) **and** the
  local email is confirmed **and** the provider vouches for it (`email_verified != "false"`) — the
  provider login was attached; sign in `User`.
- **`Blocked`** — an account owns the email but linking is off or the account is ineligible → the
  caller must **refuse** the sign-in (redirect to `?linkError=1`) and never create a duplicate.

This is the account-takeover guard for federated sign-in: linking is opt-in per tenant and, even
when on, only happens for a confirmed, vouched-for address.

## Where the ad-hoc code went

`HuiaUserManager` absorbed several open-coded clusters:

- `HuiaUserTypeExtensions.ResolveTypeAsync` (extension) → `GetUserTypeAsync` **(deleted)**
- `ExternalLoginsModel.CanRemoveLoginAsync` (static) → `CanRemoveExternalLoginAsync` **(deleted)**
- the `Users.FirstOrDefault(u => u.PhoneNumber == e164)` in `Login.cshtml.cs`
- the phone / external `new HuiaUser { … } + CreateAsync (+ AddLoginAsync)` in `CompleteProfile.cshtml.cs`
- the `FindByEmailAsync` + link-gate block in `ExternalEndpoints.ExternalLoginCallbackAsync`

## Tests

`src/dotnet/tests/Huia.IntegrationTests/HuiaUserManagerTests.cs` covers the classification
precedence, phone lookup tenant-scoping, `CreatePhoneUserAsync` shape, `CreateExternalUserAsync`
create-and-link, the three `TryLinkExternalByEmailAsync` outcomes, and `CanRemoveExternalLoginAsync`.
`HuiaTestHost.WithUserManagerAsync(tenantId, um => …)` hands a tenant-scoped `HuiaUserManager` for
your own tests.
