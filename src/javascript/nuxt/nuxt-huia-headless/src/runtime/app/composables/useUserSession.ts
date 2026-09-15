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

    /**
     * Re-read the server session (after login, on tab focus, …). Client-only: called as the second
     * nested `await` inside a login/exchange flow (e.g. `useHuia().login()` → `fetch()` →
     * `useRequestFetch()`), which during SSR is one level deeper than Vue's `withAsyncContext` — set up
     * only for a page's own top-level `await` — keeps the Nuxt app context alive for; `useRequestFetch()`
     * then throws `NUXT_E1001` instead of refreshing anything. Skipping server-side is safe: the session
     * cookie a login/exchange call sets is already on the response by the time `fetch()` runs, so a
     * follow-up navigation (the usual next step) picks up the fresh session on its own next request
     * regardless of whether this reactive `session` ref got updated first.
     */
    async fetch(): Promise<void> {
      if (import.meta.server) return
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
