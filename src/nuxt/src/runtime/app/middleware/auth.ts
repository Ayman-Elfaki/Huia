import { defineNuxtRouteMiddleware, navigateTo, useRuntimeConfig } from '#imports'
import { useUserSession } from '../composables/useUserSession'

export default defineNuxtRouteMiddleware((to) => {
  const { loggedIn } = useUserSession()
  if (loggedIn.value) return

  const loginPath = (useRuntimeConfig().public.huiaAuth as { loginPath: string }).loginPath
  // Runs during SSR with the correct value → no protected-content flash. `external` because the
  // login route 302s off-origin to the Huia authorize endpoint.
  return navigateTo(
    { path: loginPath, query: { returnTo: to.fullPath } },
    { external: true, replace: true },
  )
})
