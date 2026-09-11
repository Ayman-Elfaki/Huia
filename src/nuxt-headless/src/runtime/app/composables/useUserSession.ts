import { computed, readonly } from 'vue'
import { useState, useRequestFetch, useRuntimeConfig } from '#imports'
import type { UserSession, UserClaims } from '../../types'

export function useUserSession() {
  const session = useState<UserSession>('huia-headless:session', () => ({}))
  const paths = (useRuntimeConfig().public.huiaHeadless ?? {}) as { sessionPath?: string }
  const sessionPath = paths.sessionPath || '/api/_auth/session'

  return {
    session: readonly(session),
    user: computed<UserClaims | null>(() => session.value.user ?? null),
    loggedIn: computed(() => !!session.value.loggedIn && !!session.value.user),
    expiresAt: computed(() => session.value.expiresAt),

    async fetch(): Promise<void> {
      try {
        session.value = (await useRequestFetch()(sessionPath)) as UserSession
      }
      catch {
        session.value = { loggedIn: false }
      }
    },

    clearLocal(): void {
      session.value = { loggedIn: false }
    },

    setSession(newSession: UserSession): void {
      session.value = newSession
    },

    hasRole(role: string): boolean {
      return !!session.value.user?.roles?.includes(role)
    },

    hasAnyRole(...roles: string[]): boolean {
      const userRoles = session.value.user?.roles
      return !!userRoles && roles.some((r) => userRoles.includes(r))
    },
  }
}
