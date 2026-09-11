import { defineNuxtRouteMiddleware, navigateTo, useRuntimeConfig } from '#imports'
import type { RouteMiddleware } from 'nuxt/app'
import { useUserSession } from '../composables/useUserSession'
import { isExcluded } from '../utils/route-match'

/**
 * Unlike `nuxt-huia-oidc`'s middleware, there is no hosted login page to redirect to — the app owns
 * its own login form (Huia.Headless is a JSON API, not a redirect-based OAuth provider). An
 * unauthenticated visit to a protected route sends the browser to `loginPage` (a normal, internal
 * Nuxt page) instead of an external URL.
 */
const middleware: RouteMiddleware = (to) => {
  const { loginPage, middlewareExclude } = useRuntimeConfig().public.huiaHeadless as {
    loginPage: string
    middlewareExclude: string[]
  }

  if (isExcluded(to.path, middlewareExclude)) return

  const { loggedIn } = useUserSession()
  if (loggedIn.value) return
  if (to.path === loginPage) return

  // Runs during SSR with the correct value → no protected-content flash.
  return navigateTo({ path: loginPage, query: { returnTo: to.fullPath } }, { replace: true })
}

export default defineNuxtRouteMiddleware(middleware)
