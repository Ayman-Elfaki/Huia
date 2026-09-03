# Security

- Cookie hardening is always on: `huia.auth` (Lax), `huia.flow` / `huia.csrf` (Strict), external
  correlation cookie (None + Secure), all `Secure`-gated on the transport setting.
- OTP secrets are stored as `SHA-256(salt || code)`, single-use, constant-time compared.
- Per-phone OTP throttling uses `System.Threading.RateLimiting`.
- Opt into [security headers](/security/security-headers) with `AddHuiaSecurityHeaders()`.
