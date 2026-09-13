'use client'

import { useContext } from 'react'
import { HuiaHeadlessContext, type HuiaHeadlessContextValue } from './provider.js'

export function useHuia(): HuiaHeadlessContextValue {
  const ctx = useContext(HuiaHeadlessContext)
  if (!ctx) {
    throw new Error('[next-huia-headless] `useHuia` must be used within a `<HuiaHeadlessProvider>` component.')
  }
  return ctx
}

export function useUserSession() {
  const {
    session,
    user,
    loggedIn,
    expiresAt,
    loading,
    fetch,
    clear,
    clearLocal,
    hasRole,
    hasAnyRole,
  } = useHuia()

  return {
    session,
    user,
    loggedIn,
    expiresAt,
    loading,
    fetch,
    clear,
    clearLocal,
    hasRole,
    hasAnyRole,
  }
}

import type {
  HeadlessAdminUser,
  HeadlessAdminRole,
  HeadlessAdminUsersPage,
  CreateHeadlessAdminUserRequest,
  UpdateHeadlessAdminUserRequest,
} from 'huia-auth-core'

export function useHuiaAdmin(apiPrefix: string = '/api/auth') {
  const adminBase = `${apiPrefix.replace(/\/+$/, '')}/admin`

  const call = async <T>(path: string, init?: RequestInit): Promise<T> => {
    const res = await fetch(`${adminBase}${path}`, {
      ...init,
      headers: {
        'content-type': 'application/json',
        ...init?.headers,
      },
    })
    if (res.status === 204) {
      return undefined as unknown as T
    }
    const text = await res.text()
    if (!res.ok) {
      let err: unknown = text
      try { err = JSON.parse(text) } catch {}
      throw err
    }
    return (text ? JSON.parse(text) : undefined) as T
  }

  return {
    listUsers: (params?: { page?: number, pageSize?: number, search?: string }) => {
      const q = new URLSearchParams()
      if (params?.page) q.set('page', String(params.page))
      if (params?.pageSize) q.set('pageSize', String(params.pageSize))
      if (params?.search) q.set('search', params.search)
      const qs = q.toString() ? `?${q}` : ''
      return call<HeadlessAdminUsersPage>(`/users${qs}`)
    },
    getUser: (id: string) => call<HeadlessAdminUser>(`/users/${encodeURIComponent(id)}`),
    createUser: (body: CreateHeadlessAdminUserRequest) => call<HeadlessAdminUser>('/users', { method: 'POST', body: JSON.stringify(body) }),
    updateUser: (id: string, body: UpdateHeadlessAdminUserRequest) => call<HeadlessAdminUser>(`/users/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify(body) }),
    deleteUser: (id: string) => call<void>(`/users/${encodeURIComponent(id)}`, { method: 'DELETE' }),
    getUserRoles: (id: string) => call<string[]>(`/users/${encodeURIComponent(id)}/roles`),
    addUserRole: (id: string, role: string) => call<void>(`/users/${encodeURIComponent(id)}/roles`, { method: 'POST', body: JSON.stringify({ role }) }),
    removeUserRole: (id: string, role: string) => call<void>(`/users/${encodeURIComponent(id)}/roles/${encodeURIComponent(role)}`, { method: 'DELETE' }),
    lockUser: (id: string) => call<void>(`/users/${encodeURIComponent(id)}/lock`, { method: 'POST' }),
    unlockUser: (id: string) => call<void>(`/users/${encodeURIComponent(id)}/unlock`, { method: 'POST' }),
    listRoles: () => call<{ data: HeadlessAdminRole[] }>('/roles'),
    getRole: (id: string) => call<HeadlessAdminRole>(`/roles/${encodeURIComponent(id)}`),
    createRole: (name: string) => call<HeadlessAdminRole>('/roles', { method: 'POST', body: JSON.stringify({ name }) }),
    updateRole: (id: string, name: string) => call<HeadlessAdminRole>(`/roles/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify({ name }) }),
    deleteRole: (id: string) => call<void>(`/roles/${encodeURIComponent(id)}`, { method: 'DELETE' }),
  }
}

