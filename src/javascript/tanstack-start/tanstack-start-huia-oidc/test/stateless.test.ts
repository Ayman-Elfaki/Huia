import { describe, it, expect } from 'vitest'
import { resolveOidcConfig } from '../src/server/config.js'
import {
  writeSessionToHeaders,
  readSessionFromCookies,
  parseCookieHeader,
} from '../src/server/cookies.js'
import type { CookiePayload, TokenRecord } from 'huia-auth-core'

describe('@huia/tanstack-start-oidc stateless mode', () => {
  const password = 'a-secure-32-byte-password-for-testing-12345'
  const config = resolveOidcConfig({
    issuer: 'https://id.huia.local/todo',
    clientId: 'todo-tanstack',
    session: {
      password,
      stateless: true,
    },
  })

  it('config resolves stateless = true', () => {
    expect(config.session.stateless).toBe(true)
  })

  it('writes and reads stateless session with tokens from cookies without server storage', async () => {
    const headers = new Headers()
    const mockTokenRecord: TokenRecord = {
      sid: 'stateless-session-abc',
      accessToken: 'access-token-stateless-test-123',
      refreshToken: 'refresh-token-stateless-test-456',
      claims: {
        sub: 'user_42',
        email: 'bob@example.com',
        roles: ['editor'],
      },
      accessTokenExpiresAt: Date.now() + 3600 * 1000,
      updatedAt: Date.now(),
    }

    const payload: CookiePayload = {
      sid: mockTokenRecord.sid,
      user: mockTokenRecord.claims,
      expiresAt: mockTokenRecord.accessTokenExpiresAt,
      tokens: mockTokenRecord,
      stateless: true,
    }

    await writeSessionToHeaders(headers, config, payload)

    const setCookies = headers.getSetCookie ? headers.getSetCookie() : [headers.get('set-cookie') || '']
    const cookieStore: Record<string, string> = {}
    for (const sc of setCookies) {
      const first = sc.split(';')[0]
      const [k, v] = first.split('=')
      if (v) cookieStore[k.trim()] = v.trim()
    }

    // Read back session
    const readPayload = await readSessionFromCookies(name => cookieStore[name], config)
    expect(readPayload).not.toBeNull()
    expect(readPayload?.stateless).toBe(true)
    expect(readPayload?.user.email).toBe('bob@example.com')
    expect(readPayload?.tokens?.accessToken).toBe('access-token-stateless-test-123')
    expect(readPayload?.tokens?.refreshToken).toBe('refresh-token-stateless-test-456')
  })
})
