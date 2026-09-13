import { describe, it, expect } from 'vitest'
import { resolveOidcConfig } from '../src/server/config.js'

describe('next-huia-oidc config', () => {
  it('builds issuer from baseUrl and tenant', () => {
    const cfg = resolveOidcConfig({
      baseUrl: 'https://id.huia.local',
      tenant: 'todo',
      clientId: 'todo-app',
      session: {
        password: 'a-secure-32-byte-password-for-testing-12345',
      },
    })

    expect(cfg.issuer).toBe('https://id.huia.local/todo')
    expect(cfg.clientId).toBe('todo-app')
    expect(cfg.scopes).toContain('openid')
    expect(cfg.scopes).toContain('offline_access')
    expect(cfg.par.enabled).toBe(true)
  })

  it('uses explicit issuer when provided', () => {
    const cfg = resolveOidcConfig({
      issuer: 'https://custom-id.example.com',
      clientId: 'my-client',
      session: {
        password: 'a-secure-32-byte-password-for-testing-12345',
      },
    })

    expect(cfg.issuer).toBe('https://custom-id.example.com')
  })

  it('throws when neither issuer nor baseUrl/tenant provided', () => {
    expect(() =>
      resolveOidcConfig({
        clientId: 'test',
        session: { password: 'test-pass-32-bytes-long-12345678' },
      }),
    ).toThrow('Either `issuer` or both `baseUrl` and `tenant` must be provided')
  })
})
