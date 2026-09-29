import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { HuiaOidcHelper } from '../src/oidc-client.js'

// Mock the openid-client module at the top level
vi.mock('openid-client', () => {
  const fakeConfig = {
    serverMetadata: () => ({
      pushed_authorization_request_endpoint: 'https://id.test/connect/par',
      end_session_endpoint: 'https://id.test/connect/logout',
    }),
  }

  const fakeConfigNoPar = {
    serverMetadata: () => ({
      end_session_endpoint: 'https://id.test/connect/logout',
    }),
  }

  const fakeConfigNoEndSession = {
    serverMetadata: () => ({}),
  }

  return {
    discovery: vi.fn().mockResolvedValue(fakeConfig),
    randomPKCECodeVerifier: vi.fn().mockReturnValue('verifier_abc'),
    calculatePKCECodeChallenge: vi.fn().mockResolvedValue('challenge_abc'),
    randomState: vi.fn().mockReturnValue('state_abc'),
    randomNonce: vi.fn().mockReturnValue('nonce_abc'),
    buildAuthorizationUrl: vi.fn().mockReturnValue(new URL('https://id.test/connect/authorize?x=1')),
    buildAuthorizationUrlWithPAR: vi.fn().mockResolvedValue(new URL('https://id.test/connect/par?request_uri=urn:uuid:par')),
    buildEndSessionUrl: vi.fn().mockReturnValue(new URL('https://id.test/connect/logout?client_id=c1')),
    authorizationCodeGrant: vi.fn().mockResolvedValue({
      claims: () => ({ sub: 'u1', iss: 'https://id.test/tenant' }),
      access_token: 'at_1',
    }),
    refreshTokenGrant: vi.fn().mockResolvedValue({
      access_token: 'at_refreshed',
    }),
    ClientSecretPost: vi.fn().mockReturnValue('client_auth'),
    allowInsecureRequests: 'allow_insecure',
    _fakeConfig: fakeConfig,
    _fakeConfigNoPar: fakeConfigNoPar,
    _fakeConfigNoEndSession: fakeConfigNoEndSession,
  }
})

import * as oidcMod from 'openid-client'

describe('HuiaOidcHelper', () => {
  let helper: HuiaOidcHelper

  beforeEach(() => {
    helper = new HuiaOidcHelper()
    vi.clearAllMocks()
  })

  describe('getConfiguration', () => {
    it('calls discovery and caches the result', async () => {
      const options = { issuer: 'https://id.test/tenant', clientId: 'c1' }

      const config1 = await helper.getConfiguration(options)
      const config2 = await helper.getConfiguration(options)

      expect(config1).toBe(config2)
      expect(oidcMod.discovery).toHaveBeenCalledTimes(1)
    })

    it('deduplicates concurrent discovery calls for the same issuer+clientId', async () => {
      const options = { issuer: 'https://id.test/tenant', clientId: 'c1' }

      const [cfg1, cfg2] = await Promise.all([
        helper.getConfiguration(options),
        helper.getConfiguration(options),
      ])

      expect(cfg1).toBe(cfg2)
      expect(oidcMod.discovery).toHaveBeenCalledTimes(1)
    })

    it('uses separate cache entries for different issuer/clientId combinations', async () => {
      await helper.getConfiguration({ issuer: 'https://id.test/a', clientId: 'c1' })
      await helper.getConfiguration({ issuer: 'https://id.test/b', clientId: 'c1' })

      expect(oidcMod.discovery).toHaveBeenCalledTimes(2)
    })

    it('passes ClientSecretPost when clientSecret is provided', async () => {
      await helper.getConfiguration({
        issuer: 'https://id.test/x',
        clientId: 'c1',
        clientSecret: 'secret',
      })

      expect(oidcMod.ClientSecretPost).toHaveBeenCalledWith('secret')
    })
  })

  describe('beginAuthorization', () => {
    it('builds an authorization URL with PKCE state', async () => {
      const config = (oidcMod as any)._fakeConfig
      const result = await helper.beginAuthorization(config, {
        clientId: 'c1',
        redirectUri: 'https://app.test/callback',
        scopes: ['openid', 'profile'],
      })

      expect(result.redirectTo).toContain('https://id.test/connect/authorize')
      expect(result.stateRecord.state).toBe('state_abc')
      expect(result.stateRecord.codeVerifier).toBe('verifier_abc')
      expect(result.stateRecord.nonce).toBe('nonce_abc')
      expect(result.stateRecord.createdAt).toBeLessThanOrEqual(Date.now())
    })

    it('uses default scopes when none provided', async () => {
      const config = (oidcMod as any)._fakeConfig
      await helper.beginAuthorization(config, {
        clientId: 'c1',
        redirectUri: 'https://app.test/callback',
      })

      expect(oidcMod.buildAuthorizationUrl).toHaveBeenCalled()
      const params = vi.mocked(oidcMod.buildAuthorizationUrl).mock.calls[0][1]
      expect(params.scope).toBe('openid profile email offline_access')
    })

    it('uses PAR when parEnabled and server supports it', async () => {
      const config = (oidcMod as any)._fakeConfig
      const result = await helper.beginAuthorization(config, {
        clientId: 'c1',
        redirectUri: 'https://app.test/callback',
        parEnabled: true,
      })

      expect(oidcMod.buildAuthorizationUrlWithPAR).toHaveBeenCalled()
      expect(result.redirectTo).toContain('par')
    })

    it('falls back to plain authorization when PAR fails and parRequired is false', async () => {
      vi.mocked(oidcMod.buildAuthorizationUrlWithPAR).mockRejectedValueOnce(new Error('par_failed'))
      const config = (oidcMod as any)._fakeConfig

      const result = await helper.beginAuthorization(config, {
        clientId: 'c1',
        redirectUri: 'https://app.test/callback',
        parEnabled: true,
        parRequired: false,
      })

      expect(result.redirectTo).toContain('authorize')
    })

    it('throws when PAR fails and parRequired is true', async () => {
      vi.mocked(oidcMod.buildAuthorizationUrlWithPAR).mockRejectedValueOnce(new Error('par_failed'))
      const config = (oidcMod as any)._fakeConfig

      await expect(
        helper.beginAuthorization(config, {
          clientId: 'c1',
          redirectUri: 'https://app.test/callback',
          parEnabled: true,
          parRequired: true,
        }),
      ).rejects.toThrow('par_required')
    })

    it('throws when parRequired but server does not advertise PAR', async () => {
      const config = (oidcMod as any)._fakeConfigNoPar

      await expect(
        helper.beginAuthorization(config, {
          clientId: 'c1',
          redirectUri: 'https://app.test/callback',
          parRequired: true,
        }),
      ).rejects.toThrow('par_required')
    })

    it('preserves returnTo in the stateRecord', async () => {
      const config = (oidcMod as any)._fakeConfig
      const result = await helper.beginAuthorization(config, {
        clientId: 'c1',
        redirectUri: 'https://app.test/callback',
        returnTo: '/dashboard',
      })

      expect(result.stateRecord.returnTo).toBe('/dashboard')
    })
  })

  describe('completeAuthorization', () => {
    it('exchanges the code and validates the issuer', async () => {
      const config = (oidcMod as any)._fakeConfig
      const result = await helper.completeAuthorization(
        config,
        new URL('https://app.test/callback?code=auth_code&state=state_abc'),
        {
          state: 'state_abc',
          codeVerifier: 'verifier_abc',
          nonce: 'nonce_abc',
          createdAt: Date.now(),
          returnTo: '/home',
        },
        'https://id.test/tenant',
      )

      expect(result.claims.sub).toBe('u1')
      expect(result.returnTo).toBe('/home')
      expect(oidcMod.authorizationCodeGrant).toHaveBeenCalledWith(
        config,
        expect.any(URL),
        expect.objectContaining({
          pkceCodeVerifier: 'verifier_abc',
          expectedState: 'state_abc',
          expectedNonce: 'nonce_abc',
        }),
      )
    })

    it('throws when id_token claims are missing', async () => {
      vi.mocked(oidcMod.authorizationCodeGrant).mockResolvedValueOnce({
        claims: () => undefined,
      } as any)

      const config = (oidcMod as any)._fakeConfig
      await expect(
        helper.completeAuthorization(
          config,
          new URL('https://app.test/callback?code=c'),
          { state: 's', codeVerifier: 'v', nonce: 'n', createdAt: Date.now() },
          'https://id.test/tenant',
        ),
      ).rejects.toThrow('missing_id_token')
    })

    it('throws when issuer does not match', async () => {
      const config = (oidcMod as any)._fakeConfig
      await expect(
        helper.completeAuthorization(
          config,
          new URL('https://app.test/callback?code=c'),
          { state: 's', codeVerifier: 'v', nonce: 'n', createdAt: Date.now() },
          'https://other-issuer.test',
        ),
      ).rejects.toThrow('issuer_mismatch')
    })
  })

  describe('refreshTokens', () => {
    it('delegates to refreshTokenGrant', async () => {
      const config = (oidcMod as any)._fakeConfig
      const result = await helper.refreshTokens(config, 'rt_1')

      expect(oidcMod.refreshTokenGrant).toHaveBeenCalledWith(config, 'rt_1', undefined)
      expect(result.access_token).toBe('at_refreshed')
    })

    it('passes scope when provided', async () => {
      const config = (oidcMod as any)._fakeConfig
      await helper.refreshTokens(config, 'rt_1', 'openid profile')

      expect(oidcMod.refreshTokenGrant).toHaveBeenCalledWith(config, 'rt_1', { scope: 'openid profile' })
    })
  })

  describe('buildLogoutUrl', () => {
    it('uses end_session_endpoint when available', () => {
      const config = (oidcMod as any)._fakeConfig
      const url = helper.buildLogoutUrl(config, 'https://id.test/tenant', {
        clientId: 'c1',
        postLogoutRedirectUri: 'https://app.test',
      })

      expect(oidcMod.buildEndSessionUrl).toHaveBeenCalled()
      expect(url).toContain('logout')
    })

    it('includes id_token_hint when provided', () => {
      const config = (oidcMod as any)._fakeConfig
      helper.buildLogoutUrl(config, 'https://id.test/tenant', {
        clientId: 'c1',
        postLogoutRedirectUri: 'https://app.test',
        idTokenHint: 'id_tok_1',
      })

      const params = vi.mocked(oidcMod.buildEndSessionUrl).mock.calls[0][1]
      expect(params.id_token_hint).toBe('id_tok_1')
    })

    it('falls back to /connect/logout when end_session_endpoint is absent', () => {
      const config = (oidcMod as any)._fakeConfigNoEndSession
      const url = helper.buildLogoutUrl(config, 'https://id.test/tenant', {
        clientId: 'c1',
        postLogoutRedirectUri: 'https://app.test',
      })

      expect(url).toContain('https://id.test/tenant/connect/logout')
      expect(url).toContain('client_id=c1')
      expect(url).toContain('post_logout_redirect_uri=')
    })
  })
})
