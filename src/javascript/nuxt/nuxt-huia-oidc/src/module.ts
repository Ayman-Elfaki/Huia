import {
  defineNuxtModule,
  createResolver,
  addServerHandler,
  addServerPlugin,
  addRouteMiddleware,
  addImportsDir,
  addServerImportsDir,
  addPlugin,
} from '@nuxt/kit'
import { defu } from 'defu'

export interface ModuleOptions {
  baseUrl?: string
  tenant?: string
  issuer?: string
  clientId: string
  clientSecret?: string
  redirectUrl?: string
  scopes?: string[]
  allowedAuthParams?: string[]
  par?: { enabled?: boolean, required?: boolean }
  session?: {
    name?: string
    password?: string
    maxAge?: number
    cookie?: { sameSite?: 'lax' | 'strict' | 'none', secure?: boolean }
    userClaims?: string[]
    stateless?: boolean
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
  routes?: { login?: string, callback?: string, logout?: string, session?: string, error?: string }
  logout?: { rpInitiated?: boolean }
  allowInsecureTls?: boolean
}

const defaults = {
  baseUrl: '',
  tenant: '',
  issuer: '',
  clientId: '',
  redirectUrl: '/auth/oidc/callback',
  scopes: ['openid', 'profile', 'email', 'offline_access'],
  allowedAuthParams: ['ui_locales', 'prompt', 'login_hint'],
  par: { enabled: true, required: false },
  session: {
    name: '__Host-huia_sess',
    password: '',
    maxAge: 60 * 60 * 24 * 7,
    cookie: { sameSite: 'lax' as const },
    userClaims: ['sub', 'name', 'email', 'preferred_username', 'given_name', 'family_name', 'roles'],
    stateless: false,
  },
  storage: { base: 'huia-auth' },
  refresh: {
    enabled: true,
    earlyRefreshSeconds: 60,
    lock: { ttlMs: 10_000, waitMs: 8_000, pollMs: 150 },
  },
  cookie: { chunkSize: 3800, maxChunks: 8 },
  middleware: { global: false, exclude: [] as string[] },
  hydration: 'useState' as const,
  routes: {
    login: '/auth/oidc/login',
    callback: '/auth/oidc/callback',
    logout: '/auth/oidc/logout',
    session: '/auth/session',
    error: '/',
  },
  logout: { rpInitiated: true },
  allowInsecureTls: false,
} satisfies ModuleOptions

export default defineNuxtModule<ModuleOptions>({
  meta: {
    name: 'nuxt-huia-oidc',
    configKey: 'huia',
    compatibility: { nuxt: '>=3.0.0' },
  },
  defaults,

  setup(options, nuxt) {
    const { resolve } = createResolver(import.meta.url)
    const opts = defu(options, defaults)

    if (!opts.session.password && !process.env.NUXT_HUIA_SESSION_PASSWORD) {
      console.warn('[huia-auth] no session password set — set NUXT_HUIA_SESSION_PASSWORD (>= 32 chars)')
    }

    // ── runtime config ────────────────────────────────────────────────────────
    nuxt.options.runtimeConfig.huia = defu(
      nuxt.options.runtimeConfig.huia as Record<string, unknown> | undefined,
      {
        clientId: opts.clientId,
        clientSecret: opts.clientSecret ?? '',
        issuer: opts.issuer ?? '',
        baseUrl: opts.baseUrl ?? '',
        tenant: opts.tenant ?? '',
        redirectUrl: opts.redirectUrl,
        scopes: opts.scopes,
        allowedAuthParams: opts.allowedAuthParams,
        par: opts.par,
        session: { ...opts.session, password: opts.session.password ?? '' },
        storage: opts.storage,
        refresh: opts.refresh,
        cookie: opts.cookie,
        routes: opts.routes,
        logout: opts.logout,
        allowInsecureTls: opts.allowInsecureTls,
      },
    ) as typeof nuxt.options.runtimeConfig.huia

    nuxt.options.runtimeConfig.public.huia = defu(
      nuxt.options.runtimeConfig.public.huia as Record<string, unknown> | undefined,
      {
        loginPath: opts.routes.login,
        logoutPath: opts.routes.logout,
        sessionPath: opts.routes.session,
        // The module's own auth routes are never protected by its own middleware, regardless of
        // `middleware.exclude` — applying "must be logged in" to the routes that exist precisely for
        // the logged-out flow is never correct, and was the mechanism behind the infinite-redirect
        // risk of turning on `middleware.global` with no exclusions configured.
        middlewareExclude: [
          ...opts.middleware.exclude,
          opts.routes.login,
          opts.routes.callback,
          opts.routes.logout,
          opts.routes.session,
        ],
      },
    ) as typeof nuxt.options.runtimeConfig.public.huia

    // ── virtual type alias ────────────────────────────────────────────────────
    nuxt.options.alias['#huia-auth'] = resolve('runtime/types')

    // ── auto-imports ──────────────────────────────────────────────────────────
    nuxt.options.imports = nuxt.options.imports || {}
    nuxt.options.imports.exclude = [...(nuxt.options.imports.exclude || []), /[\\/]huia-auth-core[\\/]/]

    nuxt.hook('nitro:config', (nitroConfig) => {
      nitroConfig.imports = nitroConfig.imports || {}
      nitroConfig.imports.exclude = nitroConfig.imports.exclude || []
      nitroConfig.imports.exclude.push(/[\\/]huia-auth-core[\\/]/)
    })

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
    addServerHandler({ route: opts.routes.login, method: 'get', handler: resolve('runtime/server/routes/auth/oidc/login.get') })
    addServerHandler({ route: opts.routes.callback, method: 'get', handler: resolve('runtime/server/routes/auth/oidc/callback.get') })
    addServerHandler({ route: opts.routes.logout, method: 'get', handler: resolve('runtime/server/routes/auth/oidc/logout.get') })
    addServerHandler({ route: opts.routes.session, method: 'get', handler: resolve('runtime/server/routes/auth/session.get') })
    addServerHandler({ middleware: true, handler: resolve('runtime/server/middleware/session.context') })

    // ── Nitro plugin: OIDC discovery ──────────────────────────────────────────
    addServerPlugin(resolve('runtime/server/plugins/oidc.discovery'))

    // ── type augmentation ─────────────────────────────────────────────────────
    nuxt.hook('prepare:types', ({ references }) => {
      references.push({ path: resolve('runtime/types.d.ts') })
    })
  },
})
