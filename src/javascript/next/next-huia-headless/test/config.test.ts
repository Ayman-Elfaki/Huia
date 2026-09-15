import { describe, it, expect } from 'vitest'
import { resolveHeadlessConfig } from '../src/server/config.js'

describe('next-huia-headless config', () => {
  it('resolves default configuration properly', () => {
    const cfg = resolveHeadlessConfig({
      baseUrl: 'https://localhost:5341',
      session: {
        password: 'a-secure-32-byte-password-for-testing-12345',
      },
    })

    expect(cfg.baseUrl).toBe('https://localhost:5341')
    expect(cfg.routes.login).toBe('/api/auth/login')
    expect(cfg.routes.register).toBe('/api/auth/register')
    expect(cfg.session.userClaims).toContain('roles')
    expect(cfg.session.userClaims).toContain('firstName')
    expect(cfg.session.userClaims).toContain('lastName')
  })

  it('throws when baseUrl is missing', () => {
    expect(() =>
      resolveHeadlessConfig({
        baseUrl: '',
        session: { password: 'pass' },
      }),
    ).toThrow('`baseUrl` is required')
  })
})
