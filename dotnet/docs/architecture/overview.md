# Architecture overview

```mermaid
flowchart LR
  Browser -->|/{tenant}/connect/authorize| Huia
  Huia -->|login cookie| AccountUI[Razor account UI]
  Huia -->|code -> token| Client
  Client -->|Bearer| ResourceApi
  ResourceApi -->|JWKS| Huia
```

`UseHuia()` fixes the pipeline order: exception handler -> localization -> multi-tenant ->
security headers -> routing -> authentication -> authorization.
