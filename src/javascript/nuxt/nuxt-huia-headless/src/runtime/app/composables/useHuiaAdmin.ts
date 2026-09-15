import { useRequestFetch, useRuntimeConfig } from '#imports'
import type {
  HeadlessAdminUser,
  HeadlessAdminRole,
  HeadlessAdminUsersPage,
  CreateHeadlessAdminUserRequest,
  UpdateHeadlessAdminUserRequest,
} from 'huia-auth-core'

export function useHuiaAdmin() {
  const paths = useRuntimeConfig().public.huiaHeadless as { adminPath?: string }
  const adminPath = paths.adminPath ?? '/auth/admin'
  const fetch = useRequestFetch()

  return {
    async listUsers(params?: { page?: number, pageSize?: number, search?: string }): Promise<HeadlessAdminUsersPage> {
      const q = new URLSearchParams()
      if (params?.page) q.set('page', String(params.page))
      if (params?.pageSize) q.set('pageSize', String(params.pageSize))
      if (params?.search) q.set('search', params.search)
      const qs = q.toString() ? `?${q}` : ''
      return await fetch(`${adminPath}/users${qs}`, { method: 'GET' }) as HeadlessAdminUsersPage
    },

    async getUser(id: string): Promise<HeadlessAdminUser> {
      return await fetch(`${adminPath}/users/${encodeURIComponent(id)}`, { method: 'GET' }) as HeadlessAdminUser
    },

    async createUser(body: CreateHeadlessAdminUserRequest): Promise<HeadlessAdminUser> {
      return await fetch(`${adminPath}/users`, { method: 'POST', body }) as HeadlessAdminUser
    },

    async updateUser(id: string, body: UpdateHeadlessAdminUserRequest): Promise<HeadlessAdminUser> {
      return await fetch(`${adminPath}/users/${encodeURIComponent(id)}`, { method: 'PUT', body }) as HeadlessAdminUser
    },

    async deleteUser(id: string): Promise<void> {
      await fetch(`${adminPath}/users/${encodeURIComponent(id)}`, { method: 'DELETE' })
    },

    async getUserRoles(id: string): Promise<string[]> {
      return await fetch(`${adminPath}/users/${encodeURIComponent(id)}/roles`, { method: 'GET' }) as string[]
    },

    async addUserRole(id: string, role: string): Promise<void> {
      await fetch(`${adminPath}/users/${encodeURIComponent(id)}/roles`, { method: 'POST', body: { role } })
    },

    async removeUserRole(id: string, role: string): Promise<void> {
      await fetch(`${adminPath}/users/${encodeURIComponent(id)}/roles/${encodeURIComponent(role)}`, { method: 'DELETE' })
    },

    async lockUser(id: string): Promise<void> {
      await fetch(`${adminPath}/users/${encodeURIComponent(id)}/lock`, { method: 'POST' })
    },

    async unlockUser(id: string): Promise<void> {
      await fetch(`${adminPath}/users/${encodeURIComponent(id)}/unlock`, { method: 'POST' })
    },

    async listRoles(): Promise<{ data: HeadlessAdminRole[] }> {
      return await fetch(`${adminPath}/roles`, { method: 'GET' }) as { data: HeadlessAdminRole[] }
    },

    async getRole(id: string): Promise<HeadlessAdminRole> {
      return await fetch(`${adminPath}/roles/${encodeURIComponent(id)}`, { method: 'GET' }) as HeadlessAdminRole
    },

    async createRole(name: string): Promise<HeadlessAdminRole> {
      return await fetch(`${adminPath}/roles`, { method: 'POST', body: { name } }) as HeadlessAdminRole
    },

    async updateRole(id: string, name: string): Promise<HeadlessAdminRole> {
      return await fetch(`${adminPath}/roles/${encodeURIComponent(id)}`, { method: 'PUT', body: { name } }) as HeadlessAdminRole
    },

    async deleteRole(id: string): Promise<void> {
      await fetch(`${adminPath}/roles/${encodeURIComponent(id)}`, { method: 'DELETE' })
    },
  }
}
