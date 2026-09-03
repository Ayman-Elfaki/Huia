import { navigateTo, useRoute, useRuntimeConfig } from '#imports'
import { useUserSession } from './useUserSession'

export function useAuth() {
  const { user, loggedIn, session } = useUserSession()
  const paths = useRuntimeConfig().public.huiaAuth as { loginPath: string, logoutPath: string }

  return {
    user,
    loggedIn,
    session,

    login(opts: { returnTo?: string, locale?: string, prompt?: string } = {}) {
      const query: Record<string, string> = { returnTo: opts.returnTo ?? useRoute().fullPath }
      if (opts.locale) query.ui_locales = opts.locale
      if (opts.prompt) query.prompt = opts.prompt
      return navigateTo({ path: paths.loginPath, query }, { external: true })
    },

    logout(opts: { returnTo?: string } = {}) {
      return navigateTo(
        { path: paths.logoutPath, query: { returnTo: opts.returnTo ?? '/' } },
        { external: true },
      )
    },
  }
}
