import { computed, readonly } from 'vue'
import { useState, useRequestFetch, useRoute, navigateTo, useRuntimeConfig } from '#imports'
import type { UserSession, UserClaims } from '../../types'

export function useUserSession() {
  const session = useState<UserSession>('huia-auth:session', () => ({}))
  const paths = useRuntimeConfig().public.huiaAuth as { sessionPath: string, logoutPath: string }

  return {
    session: readonly(session),
    user: computed<UserClaims | null>(() => session.value.user ?? null),
    loggedIn: computed(() => !!session.value.user),
    expiresAt: computed(() => session.value.expiresAt),

    /** Re-read the server session (after returning from login, on tab focus, …). */
    async fetch(): Promise<void> {
      session.value = await useRequestFetch()(paths.sessionPath) as UserSession
    },

    /** Full logout via the server route (clears storage + cookies, hits the OP end_session). */
    async clear(): Promise<void> {
      await navigateTo(
        { path: paths.logoutPath, query: { returnTo: useRoute().fullPath } },
        { external: true },
      )
    },

    /** Optimistic local reset only (UI); does not touch the server. */
    clearLocal(): void {
      session.value = {}
    },

    /** Whether the signed-in user has `role` (from the `roles` claim — requires the `roles` scope). */
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
