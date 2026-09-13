import { computed, readonly } from 'vue'
import { useState, useRequestFetch, useRuntimeConfig } from '#imports'
import type { UserSession, UserClaims } from '../../types'

export function useUserSession() {
  const session = useState<UserSession>('huia-headless-auth:session', () => ({}))
  const paths = useRuntimeConfig().public.huiaHeadless as { sessionPath: string, logoutPath: string }

  return {
    session: readonly(session),
    user: computed<UserClaims | null>(() => session.value.user ?? null),
    loggedIn: computed(() => !!session.value.user),
    expiresAt: computed(() => session.value.expiresAt),

    /** Re-read the server session (after login, on tab focus, …). */
    async fetch(): Promise<void> {
      session.value = await useRequestFetch()(paths.sessionPath) as UserSession
    },

    /** Full logout via the server route (clears storage + cookies). Never redirects. */
    async clear(): Promise<void> {
      await useRequestFetch()(paths.logoutPath, { method: 'POST' })
      session.value = {}
    },

    /** Optimistic local reset only (UI); does not touch the server. */
    clearLocal(): void {
      session.value = {}
    },

    /** Whether the signed-in user has `role` (requires Huia.Headless's `identity/me` to report it). */
    hasRole(role: string): boolean {
      return !!session.value.user?.roles?.includes(role)
    },

    /** Whether the signed-in user has at least one of `roles`. */
    hasAnyRole(...roles: string[]): boolean {
      const userRoles = session.value.user?.roles
      return !!userRoles && roles.some(role => userRoles.includes(role))
    },
  }
}
