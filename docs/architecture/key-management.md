# Key management

Each tenant has its own RSA signing keys in `HuiaSigningKeys`. Private material is wrapped with
ASP.NET Core Data Protection.

```mermaid
stateDiagram-v2
  [*] --> Pending: rotation job
  Pending --> Active: activation job (ActivateAt reached)
  Active --> Rotated: newer key activated
  Rotated --> Retired: retention period elapsed
  Retired --> [*]: grace period elapsed
```

`IHuiaKeyRing` serves the published keys through `HybridCache`; the custom OpenIddict handlers
inject the active key for signing and the whole published set for validation.
