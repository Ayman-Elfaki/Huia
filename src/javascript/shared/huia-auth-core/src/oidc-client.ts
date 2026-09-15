import * as oidc from 'openid-client'
import { assertHuiaIssuer } from './tokens.js'
import type { AuthStateRecord } from './types.js'

export interface HuiaOidcClientOptions {
  issuer: string
  clientId: string
  clientSecret?: string
  scopes?: string[]
  par?: {
    enabled?: boolean
    required?: boolean
  }
  allowInsecureTls?: boolean
}

export interface AuthorizationResult {
  redirectTo: string
  stateRecord: AuthStateRecord
}

export interface CompletedAuthResult {
  tokens: oidc.TokenEndpointResponse & oidc.TokenEndpointResponseHelpers
  claims: Record<string, unknown>
  returnTo?: string
}

export class HuiaOidcHelper {
  private configCache = new Map<string, oidc.Configuration>()
  private inflightDiscovery = new Map<string, Promise<oidc.Configuration>>()

  async getConfiguration(options: HuiaOidcClientOptions): Promise<oidc.Configuration> {
    const key = `${options.issuer}:${options.clientId}`
    const cached = this.configCache.get(key)
    if (cached) return cached

    const pending = this.inflightDiscovery.get(key)
    if (pending) return pending

    const p = this.discover(options)
    this.inflightDiscovery.set(key, p)
    try {
      const cfg = await p
      this.configCache.set(key, cfg)
      return cfg
    }
    finally {
      this.inflightDiscovery.delete(key)
    }
  }

  private async discover(options: HuiaOidcClientOptions): Promise<oidc.Configuration> {
    const server = new URL(`${options.issuer.replace(/\/+$/, '')}/.well-known/openid-configuration`)
    const insecure = process.env.NODE_TLS_REJECT_UNAUTHORIZED === '0' || (options.allowInsecureTls && process.env.NODE_ENV !== 'production')

    const clientAuth = options.clientSecret
      ? oidc.ClientSecretPost(options.clientSecret)
      : undefined

    return oidc.discovery(
      server,
      options.clientId,
      undefined,
      clientAuth,
      {
        timeout: 10,
        ...(insecure ? { execute: [oidc.allowInsecureRequests] } : {}),
      },
    )
  }

  async beginAuthorization(
    config: oidc.Configuration,
    options: {
      clientId: string
      redirectUri: string
      scopes?: string[]
      returnTo?: string
      extraParams?: Record<string, string>
      parEnabled?: boolean
      parRequired?: boolean
    },
  ): Promise<AuthorizationResult> {
    const codeVerifier = oidc.randomPKCECodeVerifier()
    const codeChallenge = await oidc.calculatePKCECodeChallenge(codeVerifier)
    const state = oidc.randomState()
    const nonce = oidc.randomNonce()

    const params: Record<string, string> = {
      client_id: options.clientId,
      redirect_uri: options.redirectUri,
      response_type: 'code',
      scope: (options.scopes && options.scopes.length > 0)
        ? options.scopes.join(' ')
        : 'openid profile email offline_access',
      state,
      nonce,
      code_challenge: codeChallenge,
      code_challenge_method: 'S256',
      ...options.extraParams,
    }

    const meta = config.serverMetadata()
    const parSupported = !!meta.pushed_authorization_request_endpoint
    let redirectTo: string

    if (options.parEnabled && parSupported) {
      try {
        redirectTo = (await oidc.buildAuthorizationUrlWithPAR(config, params)).href
      }
      catch (err) {
        if (options.parRequired) {
          throw new Error('par_required: PAR push failed', { cause: err })
        }
        redirectTo = oidc.buildAuthorizationUrl(config, params).href
      }
    }
    else if (options.parRequired) {
      throw new Error('par_required: server does not advertise pushed_authorization_request_endpoint')
    }
    else {
      redirectTo = oidc.buildAuthorizationUrl(config, params).href
    }

    const stateRecord: AuthStateRecord = {
      state,
      codeVerifier,
      nonce,
      returnTo: options.returnTo,
      createdAt: Date.now(),
    }

    return { redirectTo, stateRecord }
  }

  async completeAuthorization(
    config: oidc.Configuration,
    currentUrl: URL,
    stateRecord: AuthStateRecord,
    expectedIssuer: string,
  ): Promise<CompletedAuthResult> {
    const tokens = await oidc.authorizationCodeGrant(config, currentUrl, {
      pkceCodeVerifier: stateRecord.codeVerifier,
      expectedState: stateRecord.state,
      expectedNonce: stateRecord.nonce,
      idTokenExpected: true,
    })

    const claims = tokens.claims()
    if (!claims) throw new Error('missing_id_token')
    assertHuiaIssuer(claims.iss, expectedIssuer)

    return {
      tokens,
      claims,
      returnTo: stateRecord.returnTo,
    }
  }

  async refreshTokens(
    config: oidc.Configuration,
    refreshToken: string,
    scope?: string,
  ): Promise<oidc.TokenEndpointResponse & oidc.TokenEndpointResponseHelpers> {
    return oidc.refreshTokenGrant(config, refreshToken, scope ? { scope } : undefined)
  }

  buildLogoutUrl(
    config: oidc.Configuration,
    issuer: string,
    params: { clientId: string, postLogoutRedirectUri: string, idTokenHint?: string },
  ): string {
    const meta = config.serverMetadata()
    const queryParams: Record<string, string> = {
      client_id: params.clientId,
      post_logout_redirect_uri: params.postLogoutRedirectUri,
    }
    if (params.idTokenHint) queryParams.id_token_hint = params.idTokenHint

    if (meta.end_session_endpoint) {
      return oidc.buildEndSessionUrl(config, queryParams).href
    }

    const url = new URL(`${issuer.replace(/\/+$/, '')}/connect/logout`)
    for (const [k, v] of Object.entries(queryParams)) {
      url.searchParams.set(k, v)
    }
    return url.href
  }
}

export const defaultOidcHelper = new HuiaOidcHelper()
