# Security headers

`AddHuiaSecurityHeaders()` inserts a middleware right after tenant resolution that emits a
per-request-nonce Content-Security-Policy plus `X-Content-Type-Options`, `Referrer-Policy`,
`Permissions-Policy` and (over HTTPS) HSTS.

Knobs: `AppendNonceToInlineScripts`, `DisableScripts`, `AdditionalScriptSrc`, `AdditionalStyleSrc`.
