import type { ResolvedAuthConfig } from '../../src/runtime/server/utils/internal-types'

export function testConfig(overrides: Partial<ResolvedAuthConfig> = {}): ResolvedAuthConfig {
  return {
    baseUrl: 'https://api.example.test',
    secure: true,
    storageBase: 'huia-headless-auth',
    session: {
      name: '__Host-huia_headless_sess',
      password: 'test-password-at-least-32-characters-long!!',
      maxAge: 60 * 60 * 24 * 7,
      userClaims: ['sub', 'email', 'firstName', 'lastName', 'roles'],
    },
    cookie: { chunkSize: 200, maxChunks: 8 },
    refresh: {
      enabled: true,
      earlyRefreshSeconds: 60,
      lock: { ttlMs: 10_000, waitMs: 800, pollMs: 20 },
    },
    allowInsecureTls: false,
    ...overrides,
  }
}
