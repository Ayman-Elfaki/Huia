import type { ResolvedAuthConfig } from '../../src/runtime/server/utils/internal-types'

export function testConfig(overrides: Partial<ResolvedAuthConfig> = {}): ResolvedAuthConfig {
  return {
    issuer: 'https://id.example.test/acme',
    clientId: 'acme-web',
    clientSecret: 'shhh',
    redirectUri: 'https://app.example.test/auth/oidc/callback',
    scopes: ['openid', 'profile', 'email', 'offline_access'],
    allowedAuthParams: ['ui_locales'],
    par: { enabled: true, required: false },
    secure: true,
    storageBase: 'huia-auth',
    session: {
      name: '__Host-huia_sess',
      password: 'test-password-at-least-32-characters-long!!',
      maxAge: 60 * 60 * 24 * 7,
      userClaims: ['sub', 'name', 'email', 'roles'],
    },
    cookie: { chunkSize: 200, maxChunks: 8 },
    refresh: {
      enabled: true,
      earlyRefreshSeconds: 60,
      lock: { ttlMs: 10_000, waitMs: 800, pollMs: 20 },
    },
    oauthCookieName: '__Host-huia_oauth',
    errorPath: '/',
    logout: { rpInitiated: true },
    allowInsecureTls: false,
    ...overrides,
  }
}
