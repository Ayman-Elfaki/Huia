import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { HuiaHeadlessClient, BackendError } from '../src/headless-client.js'
import type {
  BackendTokenResponse,
  BackendMeResponse,
  BackendPhoneStartResponse,
  BackendPhoneVerifyResponse,
  BackendExternalExchangeResponse,
  HeadlessAdminUser,
  HeadlessAdminUsersPage,
  HeadlessAdminRole,
} from '../src/types.js'

function mockFetch(body: unknown, status = 200) {
  return vi.fn().mockResolvedValue({
    ok: status >= 200 && status < 300,
    status,
    text: async () => (body === undefined ? '' : JSON.stringify(body)),
  })
}

describe('HuiaHeadlessClient', () => {
  let originalFetch: typeof globalThis.fetch

  beforeEach(() => {
    originalFetch = globalThis.fetch
  })

  afterEach(() => {
    globalThis.fetch = originalFetch
  })

  describe('constructor', () => {
    it('strips trailing slashes from baseUrl', () => {
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.example.com///' })
      expect(client.baseUrl).toBe('https://id.example.com')
    })

    it('defaults allowInsecureTls to false', () => {
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.example.com' })
      expect(client.allowInsecureTls).toBe(false)
    })
  })

  describe('register', () => {
    it('calls POST /identity/register', async () => {
      const fetch = mockFetch(undefined)
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      await client.register({ email: 'a@b.c', password: 'P@ss1234', firstName: 'A', lastName: 'B' })

      expect(fetch).toHaveBeenCalledOnce()
      const [url, init] = fetch.mock.calls[0]
      expect(url).toBe('https://id.test/identity/register')
      expect(init.method).toBe('POST')
      expect(JSON.parse(init.body)).toEqual({ email: 'a@b.c', password: 'P@ss1234', firstName: 'A', lastName: 'B' })
    })
  })

  describe('login', () => {
    it('calls POST /identity/login and returns token response', async () => {
      const tokenRes: BackendTokenResponse = {
        tokenType: 'Bearer',
        accessToken: 'at_1',
        expiresIn: 3600,
        refreshToken: 'rt_1',
      }
      globalThis.fetch = mockFetch(tokenRes)
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      const result = await client.login({ email: 'a@b.c', password: 'P@ss' })
      expect(result).toEqual(tokenRes)
    })
  })

  describe('refresh', () => {
    it('calls POST /identity/refresh with the refreshToken body', async () => {
      const tokenRes: BackendTokenResponse = {
        tokenType: 'Bearer',
        accessToken: 'at_2',
        expiresIn: 3600,
        refreshToken: 'rt_2',
      }
      const fetch = mockFetch(tokenRes)
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      const result = await client.refresh('rt_1')
      expect(result).toEqual(tokenRes)
      expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ refreshToken: 'rt_1' })
    })
  })

  describe('me', () => {
    it('calls GET /identity/me with Bearer token', async () => {
      const meRes: BackendMeResponse = {
        email: 'a@b.c',
        isEmailConfirmed: true,
        roles: ['admin'],
        firstName: 'Alice',
        lastName: 'Bee',
      }
      const fetch = mockFetch(meRes)
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      const result = await client.me('at_1')
      expect(result).toEqual(meRes)
      expect(fetch.mock.calls[0][1].headers.authorization).toBe('Bearer at_1')
    })
  })

  describe('confirmEmail', () => {
    it('calls GET /identity/confirmEmail with query parameters', async () => {
      const fetch = mockFetch(undefined)
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      await client.confirmEmail('uid_1', 'code_123')
      const url = fetch.mock.calls[0][0] as string
      expect(url).toContain('/identity/confirmEmail?')
      expect(url).toContain('userId=uid_1')
      expect(url).toContain('code=code_123')
    })

    it('includes changedEmail when provided', async () => {
      const fetch = mockFetch(undefined)
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      await client.confirmEmail('uid_1', 'code_123', 'new@email.com')
      const url = fetch.mock.calls[0][0] as string
      expect(url).toContain('changedEmail=new%40email.com')
    })
  })

  describe('forgotPassword / resetPassword', () => {
    it('calls POST /identity/forgotPassword', async () => {
      const fetch = mockFetch(undefined)
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      await client.forgotPassword('a@b.c')
      expect(fetch.mock.calls[0][0]).toBe('https://id.test/identity/forgotPassword')
    })

    it('calls POST /identity/resetPassword', async () => {
      const fetch = mockFetch(undefined)
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      await client.resetPassword({ email: 'a@b.c', resetCode: 'rc', newPassword: 'New1!' })
      expect(fetch.mock.calls[0][0]).toBe('https://id.test/identity/resetPassword')
    })
  })

  describe('phone flow', () => {
    it('phoneStart calls POST /identity/phone/start', async () => {
      const startRes: BackendPhoneStartResponse = { flowId: 'f1', expiresInSeconds: 300 }
      const fetch = mockFetch(startRes)
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      const result = await client.phoneStart({ phoneNumber: '+1234567890' })
      expect(result).toEqual(startRes)
    })

    it('phoneVerify calls POST /identity/phone/verify', async () => {
      const verifyRes: BackendPhoneVerifyResponse = { accessToken: 'at_ph', expiresIn: 3600 }
      const fetch = mockFetch(verifyRes)
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      const result = await client.phoneVerify({ flowId: 'f1', code: '123456' })
      expect(result).toEqual(verifyRes)
    })

    it('phoneCompleteProfile calls POST /identity/phone/complete-profile', async () => {
      const tokenRes: BackendTokenResponse = { tokenType: 'Bearer', accessToken: 'at_3', expiresIn: 3600, refreshToken: 'rt_3' }
      globalThis.fetch = mockFetch(tokenRes)
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      const result = await client.phoneCompleteProfile({ flowId: 'f1', firstName: 'A', lastName: 'B' })
      expect(result).toEqual(tokenRes)
    })
  })

  describe('external login', () => {
    it('externalLoginUrl builds the correct URL', () => {
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })
      const url = client.externalLoginUrl('google', '/dashboard')
      expect(url).toContain('/identity/account/external/google')
      expect(url).toContain('returnUrl=%2Fdashboard')
    })

    it('externalExchange calls POST /identity/account/external/exchange', async () => {
      const exchangeRes: BackendExternalExchangeResponse = { accessToken: 'at_ext', expiresIn: 3600 }
      globalThis.fetch = mockFetch(exchangeRes)
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      const result = await client.externalExchange('ext_code')
      expect(result).toEqual(exchangeRes)
    })

    it('externalCompleteProfile calls POST /identity/account/external/complete-profile', async () => {
      const tokenRes: BackendTokenResponse = { tokenType: 'Bearer', accessToken: 'at_4', expiresIn: 3600, refreshToken: 'rt_4' }
      globalThis.fetch = mockFetch(tokenRes)
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      const result = await client.externalCompleteProfile({ code: 'c1', firstName: 'A', lastName: 'B' })
      expect(result).toEqual(tokenRes)
    })
  })

  describe('admin user endpoints', () => {
    const mockUser: HeadlessAdminUser = {
      id: 'u1', userName: 'a@b.c', email: 'a@b.c', emailConfirmed: true,
      phoneNumber: null, phoneNumberConfirmed: false, firstName: 'A', lastName: 'B',
      lockoutEnabled: true, lockoutEnd: null, roles: ['admin'],
    }

    it('adminListUsers sends authorization header and query params', async () => {
      const page: HeadlessAdminUsersPage = {
        data: [mockUser], totalCount: 1, page: 1, pageSize: 20, hasNext: false, hasPrevious: false,
      }
      const fetch = mockFetch(page)
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      const result = await client.adminListUsers('tok', { page: 2, pageSize: 10, search: 'alice' })
      expect(result).toEqual(page)
      const url = fetch.mock.calls[0][0] as string
      expect(url).toContain('page=2')
      expect(url).toContain('pageSize=10')
      expect(url).toContain('search=alice')
      expect(fetch.mock.calls[0][1].headers.authorization).toBe('Bearer tok')
    })

    it('adminListUsers sends all filter parameters including names, phone, roles, and status', async () => {
      const page: HeadlessAdminUsersPage = {
        data: [mockUser], totalCount: 1, page: 1, pageSize: 20, hasNext: false, hasPrevious: false,
      }
      const fetch = mockFetch(page)
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      await client.adminListUsers('tok', {
        username: 'bob',
        firstName: 'Robert',
        lastName: 'Smith',
        phoneNumber: '+15005550001',
        email: 'bob@example.com',
        role: 'editor',
        roles: ['admin', 'manager'],
        emailConfirmed: true,
        phoneNumberConfirmed: false,
        isLockedOut: false,
      })

      const url = fetch.mock.calls[0][0] as string
      expect(url).toContain('username=bob')
      expect(url).toContain('firstName=Robert')
      expect(url).toContain('lastName=Smith')
      expect(url).toContain('phoneNumber=%2B15005550001')
      expect(url).toContain('email=bob%40example.com')
      expect(url).toContain('role=editor')
      expect(url).toContain('roles=admin%2Cmanager')
      expect(url).toContain('emailConfirmed=true')
      expect(url).toContain('phoneNumberConfirmed=false')
      expect(url).toContain('isLockedOut=false')
    })

    it('adminGetUser calls GET /admin/users/:id', async () => {
      const fetch = mockFetch(mockUser)
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      await client.adminGetUser('tok', 'u1')
      expect(fetch.mock.calls[0][0]).toBe('https://id.test/admin/users/u1')
    })

    it('adminCreateUser calls POST /admin/users', async () => {
      const fetch = mockFetch(mockUser)
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      await client.adminCreateUser('tok', { email: 'a@b.c', password: 'P1!', firstName: 'A', lastName: 'B' })
      expect(fetch.mock.calls[0][1].method).toBe('POST')
    })

    it('adminDeleteUser calls DELETE /admin/users/:id', async () => {
      const fetch = mockFetch(undefined)
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      await client.adminDeleteUser('tok', 'u1')
      expect(fetch.mock.calls[0][1].method).toBe('DELETE')
    })

    it('adminLockUser / adminUnlockUser call the correct endpoints', async () => {
      const fetch = mockFetch(undefined)
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      await client.adminLockUser('tok', 'u1')
      expect(fetch.mock.calls[0][0]).toBe('https://id.test/admin/users/u1/lock')

      await client.adminUnlockUser('tok', 'u1')
      expect(fetch.mock.calls[1][0]).toBe('https://id.test/admin/users/u1/unlock')
    })

    it('adminVerifyEmail calls POST /admin/users/:id/verify-email', async () => {
      const fetch = mockFetch(undefined)
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      await client.adminVerifyEmail('tok', 'u1')
      expect(fetch.mock.calls[0][0]).toBe('https://id.test/admin/users/u1/verify-email')
      expect(fetch.mock.calls[0][1].method).toBe('POST')
      expect(fetch.mock.calls[0][1].headers.authorization).toBe('Bearer tok')
    })
  })

  describe('admin role endpoints', () => {
    const mockRole: HeadlessAdminRole = { id: 'r1', name: 'admin', origin: 'dynamic' }

    it('adminListRoles calls GET /admin/roles', async () => {
      const fetch = mockFetch({ data: [mockRole] })
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      const result = await client.adminListRoles('tok')
      expect(result.data).toEqual([mockRole])
    })

    it('adminCreateRole calls POST /admin/roles', async () => {
      const fetch = mockFetch(mockRole)
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      await client.adminCreateRole('tok', 'editor')
      expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ name: 'editor' })
    })

    it('adminDeleteRole calls DELETE /admin/roles/:id', async () => {
      const fetch = mockFetch(undefined)
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      await client.adminDeleteRole('tok', 'r1')
      expect(fetch.mock.calls[0][0]).toBe('https://id.test/admin/roles/r1')
      expect(fetch.mock.calls[0][1].method).toBe('DELETE')
    })
  })

  describe('user role management', () => {
    it('adminGetUserRoles / adminAddUserRole / adminRemoveUserRole call correct paths', async () => {
      const fetch = mockFetch(['admin'])
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      await client.adminGetUserRoles('tok', 'u1')
      expect(fetch.mock.calls[0][0]).toBe('https://id.test/admin/users/u1/roles')

      globalThis.fetch = mockFetch(undefined)
      await client.adminAddUserRole('tok', 'u1', 'editor')
      await client.adminRemoveUserRole('tok', 'u1', 'editor')
    })
  })

  describe('BackendError handling', () => {
    it('throws BackendError with parsed JSON problem on non-ok response', async () => {
      const problem = { type: 'validation', errors: { email: ['required'] } }
      globalThis.fetch = mockFetch(problem, 400)
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      try {
        await client.login({ email: '', password: '' })
        expect.fail('should have thrown')
      } catch (e) {
        expect(e).toBeInstanceOf(BackendError)
        const err = e as BackendError
        expect(err.status).toBe(400)
        expect(err.problem).toEqual(problem)
      }
    })

    it('throws BackendError with raw text problem when body is not JSON', async () => {
      globalThis.fetch = vi.fn().mockResolvedValue({
        ok: false,
        status: 500,
        text: async () => 'Internal Server Error',
      })
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      try {
        await client.me('tok')
        expect.fail('should have thrown')
      } catch (e) {
        expect(e).toBeInstanceOf(BackendError)
        const err = e as BackendError
        expect(err.status).toBe(500)
        expect(err.problem).toBe('Internal Server Error')
      }
    })

    it('throws BackendError with null problem when body is empty', async () => {
      globalThis.fetch = vi.fn().mockResolvedValue({
        ok: false,
        status: 401,
        text: async () => '',
      })
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      try {
        await client.me('tok')
        expect.fail('should have thrown')
      } catch (e) {
        expect(e).toBeInstanceOf(BackendError)
        expect((e as BackendError).problem).toBeNull()
      }
    })
  })

  describe('content-type header', () => {
    it('always sends content-type: application/json', async () => {
      const fetch = mockFetch(undefined)
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      await client.forgotPassword('a@b.c')
      expect(fetch.mock.calls[0][1].headers['content-type']).toBe('application/json')
    })
  })

  describe('passkey endpoints', () => {
    it('passkeyAssertionOptions calls POST /identity/passkey/assertion-options', async () => {
      const fetch = mockFetch({ challenge: 'xyz' })
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      const res = await client.passkeyAssertionOptions()
      expect(fetch.mock.calls[0][0]).toBe('https://id.test/identity/passkey/assertion-options')
      expect(fetch.mock.calls[0][1].method).toBe('POST')
      expect(res).toEqual({ challenge: 'xyz' })
    })

    it('passkeyAssertion calls POST /identity/passkey/assertion with body', async () => {
      const mockTokens = { accessToken: 'at-1', refreshToken: 'rt-1', expiresIn: 300 }
      const fetch = mockFetch(mockTokens)
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      const res = await client.passkeyAssertion({ id: 'cred-1' })
      expect(fetch.mock.calls[0][0]).toBe('https://id.test/identity/passkey/assertion')
      expect(fetch.mock.calls[0][1].method).toBe('POST')
      expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ credential: { id: 'cred-1' } })
      expect(res).toEqual(mockTokens)
    })

    it('passkeyCreationOptions calls POST /identity/manage/passkeys/creation-options', async () => {
      const fetch = mockFetch({ challenge: 'abc' })
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      await client.passkeyCreationOptions('tok')
      expect(fetch.mock.calls[0][0]).toBe('https://id.test/identity/manage/passkeys/creation-options')
      expect(fetch.mock.calls[0][1].method).toBe('POST')
      expect(fetch.mock.calls[0][1].headers.authorization).toBe('Bearer tok')
    })

    it('passkeyList calls GET /identity/manage/passkeys', async () => {
      const mockList = [{ id: 'k1', name: 'YubiKey', createdAt: '2026-01-01', isBackedUp: false, isUserVerified: true }]
      const fetch = mockFetch(mockList)
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      const res = await client.passkeyList('tok')
      expect(fetch.mock.calls[0][0]).toBe('https://id.test/identity/manage/passkeys')
      expect(fetch.mock.calls[0][1].method).toBe('GET')
      expect(fetch.mock.calls[0][1].headers.authorization).toBe('Bearer tok')
      expect(res).toEqual(mockList)
    })

    it('passkeyRegister calls POST /identity/manage/passkeys', async () => {
      const mockKey = { id: 'k2', name: 'TouchID', createdAt: '2026-01-01', isBackedUp: true, isUserVerified: true }
      const fetch = mockFetch(mockKey)
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      const res = await client.passkeyRegister('tok', { credential: { id: 'c2' }, name: 'TouchID' })
      expect(fetch.mock.calls[0][0]).toBe('https://id.test/identity/manage/passkeys')
      expect(fetch.mock.calls[0][1].method).toBe('POST')
      expect(res).toEqual(mockKey)
    })

    it('passkeyRename calls PATCH /identity/manage/passkeys/:id', async () => {
      const fetch = mockFetch(undefined)
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      await client.passkeyRename('tok', 'k1', 'NewName')
      expect(fetch.mock.calls[0][0]).toBe('https://id.test/identity/manage/passkeys/k1')
      expect(fetch.mock.calls[0][1].method).toBe('PATCH')
      expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ name: 'NewName' })
    })

    it('passkeyRemove calls DELETE /identity/manage/passkeys/:id', async () => {
      const fetch = mockFetch(undefined)
      globalThis.fetch = fetch
      const client = new HuiaHeadlessClient({ baseUrl: 'https://id.test' })

      await client.passkeyRemove('tok', 'k1')
      expect(fetch.mock.calls[0][0]).toBe('https://id.test/identity/manage/passkeys/k1')
      expect(fetch.mock.calls[0][1].method).toBe('DELETE')
    })
  })
})
