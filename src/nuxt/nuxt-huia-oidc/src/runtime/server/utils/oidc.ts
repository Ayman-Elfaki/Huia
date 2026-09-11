import * as oidc from 'openid-client'
import {
  getCookie,
  getRequestURL,
  setCookie,
  deleteCookie,
  type H3Event,
} from 'h3'
import { useRuntimeConfig, useStorage } from 'nitropack/runtime'
import type { IDToken, TokenEndpointResponse } from 'openid-client'
import { resolveAuthConfig, resolveIssuer, discoveryCacheKey } from './config'
import { sealValue, unsealValue } from './cookie'
import {
  getStateRecord,
  setStateRecord,
  deleteStateRecord,
} from './storage'
import { assertHuiaIssuer } from './tokens'
import type { AuthStateRecord } from './internal-types'

export type TokenResponse = TokenEndpointResponse & oidc.TokenEndpointResponseHelpers

/* ── discovery cache ────────────────────────────────────────────────────────── */

const configs = new Map<string, oidc.Configuration>()
const inflight = new Map<string, Promise<oidc.Configuration>>()

export function isDev(): boolean {
  return process.env.NODE_ENV !== 'production'
}

async function discover(issuer: string): Promise<oidc.Configuration> {
  const { huiaAuth } = useRuntimeConfig()
  const raw = huiaAuth as unknown as {
    clientId: string
    clientSecret: string
    storage: { base: string }
    allowInsecureTls: boolean
  }

  const server = new URL(`${issuer}/.well-known/openid-configuration`)

  // `allowInsecureTls` is a dev-only convenience and hard-fails in production. An operator who has
  // already set NODE_TLS_REJECT_UNAUTHORIZED=0 process-wide has opted into insecure transport
  // globally, so discovery honours that in any environment (e.g. the E2E harness on plain http).
  if (raw.allowInsecureTls && !isDev()) {
    throw new Error('[huia-auth] allowInsecureTls is only honoured outside production')
  }
  const insecure = process.env.NODE_TLS_REJECT_UNAUTHORIZED === '0' || (isDev() && raw.allowInsecureTls)

  const config = await oidc.discovery(
    server,
    raw.clientId,
    undefined,
    oidc.ClientSecretPost(raw.clientSecret),
    {
      timeout: 10,
      ...(insecure ? { execute: [oidc.allowInsecureRequests] } : {}),
    },
  )

  await useStorage(raw.storage.base)
    .setItem(discoveryCacheKey(issuer), config.serverMetadata(), { ttl: 3600 })
    .catch(() => {})

  return config
}

export async function getOidcConfig(issuer: string): Promise<oidc.Configuration> {
  const cached = configs.get(issuer)
  if (cached) return cached

  const pending = inflight.get(issuer)
  if (pending) return pending

  const p = discover(issuer)
  inflight.set(issuer, p)
  try {
    const cfg = await p
    configs.set(issuer, cfg)
    return cfg
  }
  finally {
    inflight.delete(issuer)
  }
}

/** Warm discovery at boot without blocking route handling; used by the Nitro plugin. */
export function warmDiscovery(): void {
  const { huiaAuth } = useRuntimeConfig()
  const issuer = resolveIssuer(huiaAuth as never)
  getOidcConfig(issuer).then(
    (cfg) => {
      const par = !!cfg.serverMetadata().pushed_authorization_request_endpoint
      console.info(`[huia-auth] issuer=${issuer} PAR=${par ? 'yes' : 'no'}`)
    },
    (err: unknown) => console.warn(`[huia-auth] discovery deferred: ${(err as Error).message}`),
  )
}

/* ── authorization ─────────────────────────────────────────────────────────── */

export async function beginAuthorization(
  event: H3Event,
  opts: { returnTo: string, extra: Record<string, string> },
): Promise<{ redirectTo: string }> {
  const cfg = resolveAuthConfig(event)
  const config = await getOidcConfig(cfg.issuer)
  const meta = config.serverMetadata()

  const codeVerifier = oidc.randomPKCECodeVerifier()
  const codeChallenge = await oidc.calculatePKCECodeChallenge(codeVerifier)
  const state = oidc.randomState()
  const nonce = oidc.randomNonce()

  const params: Record<string, string> = {
    client_id: cfg.clientId,
    redirect_uri: cfg.redirectUri,
    response_type: 'code',
    scope: cfg.scopes.join(' '),
    state,
    nonce,
    code_challenge: codeChallenge,
    code_challenge_method: 'S256',
    ...opts.extra,
  }

  const parSupported = !!meta.pushed_authorization_request_endpoint
  let redirectTo: string

  if (cfg.par.enabled && parSupported) {
    try {
      redirectTo = (await oidc.buildAuthorizationUrlWithPAR(config, params)).href
    }
    catch (err) {
      if (cfg.par.required) throw new Error('par_required: PAR push failed', { cause: err })
      redirectTo = oidc.buildAuthorizationUrl(config, params).href
    }
  }
  else if (cfg.par.required) {
    throw new Error('par_required: the OP does not advertise a PAR endpoint')
  }
  else {
    redirectTo = oidc.buildAuthorizationUrl(config, params).href
  }

  const record: AuthStateRecord = {
    state,
    nonce,
    codeVerifier,
    redirectUri: cfg.redirectUri,
    returnTo: opts.returnTo,
    createdAt: Date.now(),
  }
  const ttl = 600
  await setStateRecord(cfg, record, ttl)

  setCookie(event, cfg.oauthCookieName, await sealValue(cfg, { state }), {
    httpOnly: true,
    secure: cfg.secure,
    sameSite: 'lax',
    path: '/',
    maxAge: ttl,
  })

  return { redirectTo }
}

export async function completeAuthorization(event: H3Event): Promise<{
  tokens: TokenResponse
  claims: IDToken
  returnTo: string
}> {
  const cfg = resolveAuthConfig(event)
  const config = await getOidcConfig(cfg.issuer)
  const url = getRequestURL(event)

  const errParam = url.searchParams.get('error')
  if (errParam) {
    throw new Error(/request_uri|expired/i.test(errParam) ? `par_expired: ${errParam}` : `authorize_error: ${errParam}`)
  }

  const state = url.searchParams.get('state') ?? ''
  const record = await getStateRecord(cfg, state)
  const cookieState = (await unsealValue<{ state: string }>(cfg, getCookie(event, cfg.oauthCookieName)))?.state

  if (!record || !state || record.state !== state || cookieState !== state) {
    throw new Error('oauth_state_mismatch')
  }

  const tokens = await oidc.authorizationCodeGrant(config, url, {
    pkceCodeVerifier: record.codeVerifier,
    expectedState: record.state,
    expectedNonce: record.nonce,
    idTokenExpected: true,
  })

  const claims = tokens.claims()
  if (!claims) throw new Error('missing_id_token')
  assertHuiaIssuer(claims.iss, cfg.issuer)

  await deleteStateRecord(cfg, state).catch(() => {})
  deleteCookie(event, cfg.oauthCookieName, { path: '/' })

  return { tokens, claims, returnTo: record.returnTo }
}

/* ── logout ────────────────────────────────────────────────────────────────── */

export async function buildLogoutUrl(
  event: H3Event,
  opts: { idTokenHint?: string, postLogoutRedirectUri: string },
): Promise<string> {
  const cfg = resolveAuthConfig(event)
  const config = await getOidcConfig(cfg.issuer)
  const params: Record<string, string> = {
    post_logout_redirect_uri: opts.postLogoutRedirectUri,
    client_id: cfg.clientId,
  }
  if (opts.idTokenHint) params.id_token_hint = opts.idTokenHint

  const meta = config.serverMetadata()
  if (meta.end_session_endpoint) {
    return oidc.buildEndSessionUrl(config, params).href
  }
  const url = new URL(`${cfg.issuer}/connect/logout`)
  for (const [k, v] of Object.entries(params)) url.searchParams.set(k, v)
  return url.href
}

export function isInvalidGrant(err: unknown): boolean {
  return err instanceof oidc.ResponseBodyError && err.error === 'invalid_grant'
}
