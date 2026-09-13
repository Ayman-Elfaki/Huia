import type {
  BackendTokenResponse,
  BackendMeResponse,
  BackendPhoneStartResponse,
  BackendPhoneVerifyResponse,
  BackendExternalExchangeResponse,
  HeadlessAdminUser,
  HeadlessAdminRole,
  HeadlessAdminUsersPage,
  CreateHeadlessAdminUserRequest,
  UpdateHeadlessAdminUserRequest,
} from './types.js'

export class BackendError extends Error {
  constructor(public status: number, public problem: unknown) {
    super(`huia_headless_error: ${status}`)
    this.name = 'BackendError'
  }
}

export interface HuiaHeadlessClientOptions {
  baseUrl: string
  allowInsecureTls?: boolean
}

export class HuiaHeadlessClient {
  readonly baseUrl: string
  readonly allowInsecureTls: boolean

  constructor(options: HuiaHeadlessClientOptions) {
    this.baseUrl = options.baseUrl.replace(/\/+$/, '')
    this.allowInsecureTls = options.allowInsecureTls ?? false

    if (this.allowInsecureTls && typeof process !== 'undefined' && process.env.NODE_ENV !== 'production') {
      process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0'
    }
  }

  private async call<T>(path: string, init: RequestInit): Promise<T> {
    const res = await fetch(`${this.baseUrl}${path}`, {
      ...init,
      headers: {
        'content-type': 'application/json',
        ...init.headers,
      },
    })

    const text = await res.text()

    if (!res.ok) {
      let problem: unknown = null
      if (text) {
        try {
          problem = JSON.parse(text)
        }
        catch {
          problem = text
        }
      }
      throw new BackendError(res.status, problem)
    }

    if (!text) {
      return undefined as T
    }

    return JSON.parse(text) as T
  }

  register(body: { email: string, password: string, firstName?: string, lastName?: string }): Promise<void> {
    return this.call('/identity/register', {
      method: 'POST',
      body: JSON.stringify(body),
    })
  }

  login(body: { email: string, password: string, twoFactorCode?: string, twoFactorRecoveryCode?: string }): Promise<BackendTokenResponse> {
    return this.call<BackendTokenResponse>('/identity/login', {
      method: 'POST',
      body: JSON.stringify(body),
    })
  }

  refresh(refreshToken: string): Promise<BackendTokenResponse> {
    return this.call<BackendTokenResponse>('/identity/refresh', {
      method: 'POST',
      body: JSON.stringify({ refreshToken }),
    })
  }

  me(accessToken: string): Promise<BackendMeResponse> {
    return this.call<BackendMeResponse>('/identity/me', {
      method: 'GET',
      headers: { authorization: `Bearer ${accessToken}` },
    })
  }

  confirmEmail(userId: string, code: string, changedEmail?: string): Promise<void> {
    const query = new URLSearchParams({ userId, code, ...(changedEmail ? { changedEmail } : {}) })
    return this.call(`/identity/confirmEmail?${query}`, { method: 'GET' })
  }

  resendConfirmationEmail(email: string): Promise<void> {
    return this.call('/identity/resendConfirmationEmail', {
      method: 'POST',
      body: JSON.stringify({ email }),
    })
  }

  forgotPassword(email: string): Promise<void> {
    return this.call('/identity/forgotPassword', {
      method: 'POST',
      body: JSON.stringify({ email }),
    })
  }

  resetPassword(body: { email: string, resetCode: string, newPassword: string }): Promise<void> {
    return this.call('/identity/resetPassword', {
      method: 'POST',
      body: JSON.stringify(body),
    })
  }

  phoneStart(body: { phoneNumber: string, country?: string, captchaResponse?: string }): Promise<BackendPhoneStartResponse> {
    return this.call<BackendPhoneStartResponse>('/identity/phone/start', {
      method: 'POST',
      body: JSON.stringify(body),
    })
  }

  phoneVerify(body: { flowId: string, code: string }): Promise<BackendPhoneVerifyResponse> {
    return this.call<BackendPhoneVerifyResponse>('/identity/phone/verify', {
      method: 'POST',
      body: JSON.stringify(body),
    })
  }

  phoneCompleteProfile(body: { flowId: string, firstName: string, lastName: string }): Promise<BackendTokenResponse> {
    return this.call<BackendTokenResponse>('/identity/phone/complete-profile', {
      method: 'POST',
      body: JSON.stringify(body),
    })
  }

  externalLoginUrl(provider: string, returnUrl: string): string {
    const q = new URLSearchParams({ returnUrl })
    return `${this.baseUrl}/identity/account/external/${encodeURIComponent(provider)}?${q}`
  }

  externalExchange(code: string): Promise<BackendExternalExchangeResponse> {
    return this.call<BackendExternalExchangeResponse>('/identity/account/external/exchange', {
      method: 'POST',
      body: JSON.stringify({ code }),
    })
  }

  externalCompleteProfile(body: { code: string, firstName: string, lastName: string }): Promise<BackendTokenResponse> {
    return this.call<BackendTokenResponse>('/identity/account/external/complete-profile', {
      method: 'POST',
      body: JSON.stringify(body),
    })
  }

  adminListUsers(accessToken: string, params?: { page?: number, pageSize?: number, search?: string }): Promise<HeadlessAdminUsersPage> {
    const q = new URLSearchParams()
    if (params?.page) q.set('page', String(params.page))
    if (params?.pageSize) q.set('pageSize', String(params.pageSize))
    if (params?.search) q.set('search', params.search)
    const qs = q.toString() ? `?${q}` : ''
    return this.call<HeadlessAdminUsersPage>(`/admin/users${qs}`, {
      method: 'GET',
      headers: { authorization: `Bearer ${accessToken}` },
    })
  }

  adminGetUser(accessToken: string, id: string): Promise<HeadlessAdminUser> {
    return this.call<HeadlessAdminUser>(`/admin/users/${encodeURIComponent(id)}`, {
      method: 'GET',
      headers: { authorization: `Bearer ${accessToken}` },
    })
  }

  adminCreateUser(accessToken: string, body: CreateHeadlessAdminUserRequest): Promise<HeadlessAdminUser> {
    return this.call<HeadlessAdminUser>('/admin/users', {
      method: 'POST',
      headers: { authorization: `Bearer ${accessToken}` },
      body: JSON.stringify(body),
    })
  }

  adminUpdateUser(accessToken: string, id: string, body: UpdateHeadlessAdminUserRequest): Promise<HeadlessAdminUser> {
    return this.call<HeadlessAdminUser>(`/admin/users/${encodeURIComponent(id)}`, {
      method: 'PUT',
      headers: { authorization: `Bearer ${accessToken}` },
      body: JSON.stringify(body),
    })
  }

  adminDeleteUser(accessToken: string, id: string): Promise<void> {
    return this.call(`/admin/users/${encodeURIComponent(id)}`, {
      method: 'DELETE',
      headers: { authorization: `Bearer ${accessToken}` },
    })
  }

  adminGetUserRoles(accessToken: string, id: string): Promise<string[]> {
    return this.call<string[]>(`/admin/users/${encodeURIComponent(id)}/roles`, {
      method: 'GET',
      headers: { authorization: `Bearer ${accessToken}` },
    })
  }

  adminAddUserRole(accessToken: string, id: string, role: string): Promise<void> {
    return this.call(`/admin/users/${encodeURIComponent(id)}/roles`, {
      method: 'POST',
      headers: { authorization: `Bearer ${accessToken}` },
      body: JSON.stringify({ role }),
    })
  }

  adminRemoveUserRole(accessToken: string, id: string, role: string): Promise<void> {
    return this.call(`/admin/users/${encodeURIComponent(id)}/roles/${encodeURIComponent(role)}`, {
      method: 'DELETE',
      headers: { authorization: `Bearer ${accessToken}` },
    })
  }

  adminLockUser(accessToken: string, id: string): Promise<void> {
    return this.call(`/admin/users/${encodeURIComponent(id)}/lock`, {
      method: 'POST',
      headers: { authorization: `Bearer ${accessToken}` },
    })
  }

  adminUnlockUser(accessToken: string, id: string): Promise<void> {
    return this.call(`/admin/users/${encodeURIComponent(id)}/unlock`, {
      method: 'POST',
      headers: { authorization: `Bearer ${accessToken}` },
    })
  }

  adminListRoles(accessToken: string): Promise<{ data: HeadlessAdminRole[] }> {
    return this.call<{ data: HeadlessAdminRole[] }>('/admin/roles', {
      method: 'GET',
      headers: { authorization: `Bearer ${accessToken}` },
    })
  }

  adminGetRole(accessToken: string, id: string): Promise<HeadlessAdminRole> {
    return this.call<HeadlessAdminRole>(`/admin/roles/${encodeURIComponent(id)}`, {
      method: 'GET',
      headers: { authorization: `Bearer ${accessToken}` },
    })
  }

  adminCreateRole(accessToken: string, name: string): Promise<HeadlessAdminRole> {
    return this.call<HeadlessAdminRole>('/admin/roles', {
      method: 'POST',
      headers: { authorization: `Bearer ${accessToken}` },
      body: JSON.stringify({ name }),
    })
  }

  adminUpdateRole(accessToken: string, id: string, name: string): Promise<HeadlessAdminRole> {
    return this.call<HeadlessAdminRole>(`/admin/roles/${encodeURIComponent(id)}`, {
      method: 'PUT',
      headers: { authorization: `Bearer ${accessToken}` },
      body: JSON.stringify({ name }),
    })
  }

  adminDeleteRole(accessToken: string, id: string): Promise<void> {
    return this.call(`/admin/roles/${encodeURIComponent(id)}`, {
      method: 'DELETE',
      headers: { authorization: `Bearer ${accessToken}` },
    })
  }
}

