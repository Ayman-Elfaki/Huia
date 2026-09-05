# Events

Huia raises a small set of immutable, in-process domain events at security- and audit-relevant
moments — an account is registered, a sign-in succeeds, a password changes, an OTP is requested or
verified, a phone number is confirmed or removed. Subscribe by registering a handler in DI; publish
your own events the same way Huia publishes its own, through the same `IHuiaEventPublisher`.

## Subscribing

```csharp
public sealed class LoginAuditHandler : IHuiaEventHandler<UserLoggedInEvent>
{
    public Task HandleAsync(UserLoggedInEvent domainEvent, CancellationToken cancellationToken)
    {
        // domainEvent.TenantId, .UserId, .Method ("password" | "sms" | a provider name), .ClientId, .OccurredAt
        return Task.CompletedTask;
    }
}

builder.Services.AddHuiaEventHandler<UserLoggedInEvent, LoginAuditHandler>();
```

`AddHuiaEventHandler<TEvent, THandler>()` can be called any number of times, for the same or
different event types — every registered handler for an event's type runs. Handlers are resolved
from DI per dispatch (see below), so constructor-inject whatever you need (a logger, an `HttpClient`,
`IServiceScopeFactory` for something that needs its own scope, …).

## Delivery model

- `IHuiaEventPublisher.PublishAsync(IHuiaEvent, CancellationToken)` only **enqueues** — it returns as
  soon as the event is written to an in-memory, unbounded `Channel<IHuiaEvent>`. Publishing from a
  request handler never waits on subscriber work.
- A single background `IHostedService` drains the channel and, per event, resolves every
  `IHuiaEventHandler<TEvent>` registered for that event's concrete type in a **fresh DI scope**.
- Delivery is **sequential**: handlers for one event are awaited one after another, and the next
  event is not read off the channel until the current one has fully dispatched. A slow or blocking
  handler delays every event behind it — keep handlers fast, or hand off to your own background work.
- A handler that throws is logged (`Huia event handler {Handler} threw while handling {Event}.`) and
  **swallowed** — one failing subscriber can never break a sign-in, a registration, or any other
  request that published the event. Nothing is retried.
- Events are **process-local and non-durable**: nothing is persisted, and anything still queued when
  the process stops is lost. This is a lightweight in-process notification mechanism, not a message
  bus or an outbox — put a real queue or outbox in a handler if you need durability.

## Privacy

Every event implements `IHuiaEvent` and carries only `TenantId` and `OccurredAt` (UTC) beyond its own
fields — by design, no event carries a raw phone number, a token, or a password. Phone numbers appear
only masked (`PhoneNumberMask`, last four digits retained — see `IPhoneNumberService.Mask`).

## Reference

| Event | Fields | Raised when |
|---|---|---|
| `UserRegisteredEvent` | `UserId`, `UserName`, `Email?`, `Method` | A new account is persisted — `Method` is `password`, `sms`, or an external provider name. |
| `UserLoggedInEvent` | `UserId`, `Method`, `ClientId?` | A sign-in succeeds, interactively or via `/connect/token`. `Method` is `pwd`, `sms`, `passkey`, or an external provider name. `ClientId` is the OAuth client the sign-in was for, when there is one. |
| `PasswordChangedEvent` | `UserId`, `Reset` | A password is set or changed — `Reset` is `true` for a forgot-password flow, `false` for an authenticated change via `/manage/password`. |
| `OtpRequestedEvent` | `UserId?`, `PhoneNumberMask`, `Delivered` | A one-time code is generated and a delivery attempt made. `UserId` is `null` when the number has no account yet (auto-provisioning); `Delivered` reflects whether the SMS provider accepted the message. |
| `OtpVerifiedEvent` | `UserId?`, `PhoneNumberMask` | A one-time code is verified successfully. |
| `PhoneChangedEvent` | `UserId`, `PhoneNumberMask?`, `Confirmed` | A phone number is confirmed or removed — `Confirmed` distinguishes the two; `PhoneNumberMask` is `null` on removal. |
| `PasskeyRegisteredEvent` | `UserId`, `CredentialId` | A passkey (WebAuthn credential) is registered. `CredentialId` is the base64url id, truncated. |
| `PasskeyRemovedEvent` | `UserId`, `CredentialId` | A passkey is removed. |

All live in the `Huia.Events` namespace (the framework-free `Huia` package) alongside `IHuiaEvent`,
`IHuiaEventPublisher` and `IHuiaEventHandler<TEvent>` — none of it depends on ASP.NET Core.

## Publishing your own events

`IHuiaEventPublisher` is a public, injectable service — you're not limited to reacting to Huia's own
events. Define your own record implementing `IHuiaEvent`, publish it from your own code, and it goes
through the same channel and dispatcher as everything above:

```csharp
public sealed record ReportGeneratedEvent(string TenantId, string ReportId, DateTimeOffset OccurredAt) : IHuiaEvent;

// anywhere with IHuiaEventPublisher injected:
await events.PublishAsync(new ReportGeneratedEvent(tenantId, reportId, timeProvider.GetUtcNow()));
```

## Testing

Huia's own integration tests assert on events with a small capturing `IHuiaEventHandler<TEvent>` per
event type, registered instead of a real handler, that appends to a shared in-memory list — the same
pattern works in your own tests: register a capturing handler with `AddHuiaEventHandler`, publish or
trigger the flow under test, then assert on what was captured. Because dispatch happens on a
background service, allow a short delay (or poll) after the action that publishes the event before
asserting a handler ran.
