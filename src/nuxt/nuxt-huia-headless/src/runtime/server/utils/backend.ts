import type { ResolvedAuthConfig, BackendTokenResponse, BackendMeResponse } from './internal-types'

export function isDev(): boolean {
  return process.env.NODE_ENV !== 'production'
}

/** Thrown on a non-2xx response from Huia.Headless. `problem` is the raw JSON body, if any. */
export class BackendError extends Error {
  constructor(public status: number, public problem: unknown) {
    super(`huia_headless_error: ${status}`)
    this.name = 'BackendError'
  }
}

function guardInsecureTls(cfg: ResolvedAuthConfig): void {
  if (!cfg.allowInsecureTls) return
  if (!isDev()) {
    throw new Error('[huia-headless-auth] allowInsecureTls is only honoured outside production')
  }
  // Node's `fetch` (undici) reads this process-wide — matches nuxt-huia-oidc's own dev-only opt-in.
  process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0'
}

async function call<T>(cfg: ResolvedAuthConfig, path: string, init: RequestInit): Promise<T> {
  guardInsecureTls(cfg)

  const res = await fetch(`${cfg.baseUrl}${path}`, {
    ...init,
    headers: { 'content-type': 'application/json', ...init.headers },
  })

  // Read as text first: MapIdentityApi's success responses (e.g. register, confirmEmail) are
  // often `200 OK` / `204 No Content` with an empty body, and an absent `Content-Length` header
  // (rather than `0`) is common enough that trusting the header alone silently breaks on a real
  // backend even though a test mock that sets it explicitly would pass.
  const text = await res.text()

  if (!res.ok) {
    const problem = text ? tryParseJson(text) : null
    throw new BackendError(res.status, problem)
  }

  if (!text) {
    return undefined as T
  }

  return JSON.parse(text) as T
}

function tryParseJson(text: string): unknown {
  try {
    return JSON.parse(text)
  }
  catch {
    return null
  }
}

export function registerAsync(cfg: ResolvedAuthConfig, body: { email: string, password: string }): Promise<void> {
  return call(cfg, '/identity/register', { method: 'POST', body: JSON.stringify(body) })
}

export function loginAsync(
  cfg: ResolvedAuthConfig,
  body: { email: string, password: string, twoFactorCode?: string, twoFactorRecoveryCode?: string },
): Promise<BackendTokenResponse> {
  return call<BackendTokenResponse>(cfg, '/identity/login', { method: 'POST', body: JSON.stringify(body) })
}

export function refreshAsync(cfg: ResolvedAuthConfig, refreshToken: string): Promise<BackendTokenResponse> {
  return call<BackendTokenResponse>(cfg, '/identity/refresh', { method: 'POST', body: JSON.stringify({ refreshToken }) })
}

export function meAsync(cfg: ResolvedAuthConfig, accessToken: string): Promise<BackendMeResponse> {
  return call<BackendMeResponse>(cfg, '/identity/me', { method: 'GET', headers: { authorization: `Bearer ${accessToken}` } })
}

export function confirmEmailAsync(cfg: ResolvedAuthConfig, userId: string, code: string, changedEmail?: string): Promise<void> {
  const query = new URLSearchParams({ userId, code, ...(changedEmail ? { changedEmail } : {}) })
  return call(cfg, `/identity/confirmEmail?${query}`, { method: 'GET' })
}

export function resendConfirmationEmailAsync(cfg: ResolvedAuthConfig, email: string): Promise<void> {
  return call(cfg, '/identity/resendConfirmationEmail', { method: 'POST', body: JSON.stringify({ email }) })
}

export function forgotPasswordAsync(cfg: ResolvedAuthConfig, email: string): Promise<void> {
  return call(cfg, '/identity/forgotPassword', { method: 'POST', body: JSON.stringify({ email }) })
}

export function resetPasswordAsync(cfg: ResolvedAuthConfig, body: { email: string, resetCode: string, newPassword: string }): Promise<void> {
  return call(cfg, '/identity/resetPassword', { method: 'POST', body: JSON.stringify(body) })
}
