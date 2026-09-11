import {
  defineNuxtModule,
  createResolver,
  addServerHandler,
  addRouteMiddleware,
  addImportsDir,
  addServerImportsDir,
  addPlugin,
} from '@nuxt/kit'
import { defu } from 'defu'

export interface ModuleOptions {
  baseUrl?: string
  session?: {
    name?: string
    password?: string
    maxAge?: number
    cookie?: { sameSite?: 'lax' | 'strict' | 'none', secure?: boolean }
    userClaims?: string[]
  }
  storage?: { base?: string }
  refresh?: {
    enabled?: boolean
    earlyRefreshSeconds?: number
    lock?: { ttlMs?: number, waitMs?: number, pollMs?: number }
  }
  cookie?: { chunkSize?: number, maxChunks?: number }
  middleware?: { global?: boolean, exclude?: string[] }
  hydration?: 'useState' | 'asyncData'
  routes?: {
    register?: string
    login?: string
    logout?: string
    refresh?: string
    session?: string
    confirmEmail?: string
    resendConfirmation?: string
    forgotPassword?: string
    resetPassword?: string
  }
  /** The app's own login page (Huia.Headless has no hosted login UI to redirect to). */
  loginPage?: string
  allowInsecureTls?: boolean
}

const defaults = {
  baseUrl: '',
  session: {
    name: '__Host-huia_headless_sess',
    password: '',
    maxAge: 60 * 60 * 24 * 7,
    cookie: { sameSite: 'lax' as const },
    userClaims: ['sub', 'email', 'firstName', 'lastName', 'roles'],
  },
  storage: { base: 'huia-headless-auth' },
  refresh: {
    enabled: true,
    earlyRefreshSeconds: 60,
    lock: { ttlMs: 10_000, waitMs: 8_000, pollMs: 150 },
  },
  cookie: { chunkSize: 3800, maxChunks: 8 },
  middleware: { global: false, exclude: [] as string[] },
  hydration: 'useState' as const,
  routes: {
    register: '/auth/register',
    login: '/auth/login',
    logout: '/auth/logout',
    refresh: '/auth/refresh',
    session: '/auth/session',
    confirmEmail: '/auth/confirm-email',
    resendConfirmation: '/auth/resend-confirmation',
    forgotPassword: '/auth/forgot-password',
    resetPassword: '/auth/reset-password',
  },
  loginPage: '/login',
  allowInsecureTls: false,
} satisfies ModuleOptions

export default defineNuxtModule<ModuleOptions>({
  meta: {
    name: 'nuxt-huia-headless',
    configKey: 'huiaHeadless',
    compatibility: { nuxt: '>=4.0.0' },
  },
  defaults,

  setup(options, nuxt) {
    const { resolve } = createResolver(import.meta.url)
    const opts = defu(options, defaults)

    if (!opts.session.password && !process.env.NUXT_HUIA_HEADLESS_SESSION_PASSWORD) {
      console.warn('[huia-headless-auth] no session password set — set NUXT_HUIA_HEADLESS_SESSION_PASSWORD (>= 32 chars)')
    }

    // ── runtime config ────────────────────────────────────────────────────────
    nuxt.options.runtimeConfig.huiaHeadless = defu(
      nuxt.options.runtimeConfig.huiaHeadless as Record<string, unknown> | undefined,
      {
        baseUrl: opts.baseUrl ?? '',
        session: { ...opts.session, password: opts.session.password ?? '' },
        storage: opts.storage,
        refresh: opts.refresh,
        cookie: opts.cookie,
        allowInsecureTls: opts.allowInsecureTls,
      },
    ) as typeof nuxt.options.runtimeConfig.huiaHeadless

    nuxt.options.runtimeConfig.public.huiaHeadless = defu(
      nuxt.options.runtimeConfig.public.huiaHeadless as Record<string, unknown> | undefined,
      {
        registerPath: opts.routes.register,
        loginPath: opts.routes.login,
        logoutPath: opts.routes.logout,
        sessionPath: opts.routes.session,
        loginPage: opts.loginPage,
        // The module's own auth routes are never protected by its own middleware, regardless of
        // `middleware.exclude`, plus the app's own login page — same rationale as nuxt-huia-oidc:
        // applying "must be logged in" to the routes/page that exist for the logged-out flow is
        // never correct.
        middlewareExclude: [
          ...opts.middleware.exclude,
          opts.loginPage,
          opts.routes.register,
          opts.routes.login,
          opts.routes.logout,
          opts.routes.refresh,
          opts.routes.session,
          opts.routes.confirmEmail,
          opts.routes.resendConfirmation,
          opts.routes.forgotPassword,
          opts.routes.resetPassword,
        ],
      },
    ) as typeof nuxt.options.runtimeConfig.public.huiaHeadless

    // ── virtual type alias ────────────────────────────────────────────────────
    nuxt.options.alias['#huia-headless-auth'] = resolve('runtime/types')

    // ── auto-imports ──────────────────────────────────────────────────────────
    addServerImportsDir(resolve('runtime/server/utils'))
    addImportsDir(resolve('runtime/app/composables'))

    // ── SSR hydration ─────────────────────────────────────────────────────────
    if (opts.hydration === 'useState') {
      addPlugin({ src: resolve('runtime/app/plugins/session.server'), mode: 'server' })
    }

    // ── route middleware ──────────────────────────────────────────────────────
    addRouteMiddleware({
      name: 'auth',
      path: resolve('runtime/app/middleware/auth'),
      global: opts.middleware.global,
    })

    // ── server handlers ───────────────────────────────────────────────────────
    addServerHandler({ route: opts.routes.register, method: 'post', handler: resolve('runtime/server/routes/auth/register.post') })
    addServerHandler({ route: opts.routes.login, method: 'post', handler: resolve('runtime/server/routes/auth/login.post') })
    addServerHandler({ route: opts.routes.logout, method: 'post', handler: resolve('runtime/server/routes/auth/logout.post') })
    addServerHandler({ route: opts.routes.refresh, method: 'post', handler: resolve('runtime/server/routes/auth/refresh.post') })
    addServerHandler({ route: opts.routes.session, method: 'get', handler: resolve('runtime/server/routes/auth/session.get') })
    addServerHandler({ route: opts.routes.confirmEmail, method: 'get', handler: resolve('runtime/server/routes/auth/confirm-email.get') })
    addServerHandler({ route: opts.routes.resendConfirmation, method: 'post', handler: resolve('runtime/server/routes/auth/resend-confirmation.post') })
    addServerHandler({ route: opts.routes.forgotPassword, method: 'post', handler: resolve('runtime/server/routes/auth/forgot-password.post') })
    addServerHandler({ route: opts.routes.resetPassword, method: 'post', handler: resolve('runtime/server/routes/auth/reset-password.post') })
    addServerHandler({ middleware: true, handler: resolve('runtime/server/middleware/session.context') })

    // ── type augmentation ─────────────────────────────────────────────────────
    nuxt.hook('prepare:types', ({ references }) => {
      references.push({ path: resolve('runtime/types.d.ts') })
    })
  },
})
