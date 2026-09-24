import { describe, it, expect } from 'vitest'
import { resolveOidcConfig } from '../src/server/config.js'
import {
  writeSessionToResponse,
  readSessionFromCookies,
} from '../src/server/session.js'
import { NextResponse } from 'next/server'
import type { CookiePayload, TokenRecord } from 'huia-auth-core'

describe('next-huia-oidc stateless mode', () => {
  const password = 'a-secure-32-byte-password-for-testing-12345'
  const config = resolveOidcConfig({
    issuer: 'https://id.huia.local/todo',
    clientId: 'todo-next',
    session: {
      password,
      stateless: true,
    },
  })

  it('config resolves stateless = true', () => {
    expect(config.session.stateless).toBe(true)
  })

  it('writes and reads stateless session with tokens from cookies without server storage', async () => {
    const res = NextResponse.next()
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

    await writeSessionToResponse(res, config, payload)

    // Collect cookies set on response
    const cookieStore: Record<string, string> = {}
    for (const cookie of res.cookies.getAll()) {
      cookieStore[cookie.name] = cookie.value
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
