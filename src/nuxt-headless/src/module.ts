import {
  defineNuxtModule,
  createResolver,
  addServerHandler,
  addImportsDir,
  addServerImportsDir,
  addPlugin,
  addRouteMiddleware,
} from '@nuxt/kit'
import { defu } from 'defu'

export interface ModuleOptions {
  huia: {
    baseUrl?: string
    tenant?: string
  }
  session?: {
    name?: string
    password?: string
    maxAge?: number
    cookie?: {
      sameSite?: 'lax' | 'strict' | 'none'
      secure?: boolean
    }
    userClaims?: string[]
  }
  storageBase?: string
  refresh?: {
    enabled?: boolean
    earlyRefreshSeconds?: number
  }
  cookie?: {
    chunkSize?: number
    maxChunks?: number
  }
  routes?: {
    register?: string
    login?: string
    logout?: string
    refresh?: string
    phoneStart?: string
    phoneVerify?: string
    phoneCompleteProfile?: string
    session?: string
  }
}

const defaults = {
  huia: {},
  session: {
    name: '__Host-huia_headless_sess',
    password: '',
    maxAge: 60 * 60 * 24 * 7,
    cookie: { sameSite: 'lax' as const },
    userClaims: ['sub', 'name', 'email', 'preferred_username', 'given_name', 'family_name', 'roles'],
  },
  storageBase: 'huia-headless-auth',
  refresh: {
    enabled: true,
    earlyRefreshSeconds: 60,
  },
  cookie: {
    chunkSize: 3800,
    maxChunks: 8,
  },
  routes: {
    register: '/auth/headless/register',
    login: '/auth/headless/login',
    logout: '/auth/headless/logout',
    refresh: '/auth/headless/refresh',
    phoneStart: '/auth/headless/phone/login/start',
    phoneVerify: '/auth/headless/phone/login/verify',
    phoneCompleteProfile: '/auth/headless/phone/complete-profile',
    session: '/api/_auth/session',
  },
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
      console.warn('[huia-headless] no session password set — set NUXT_HUIA_HEADLESS_SESSION_PASSWORD (>= 32 chars)')
    }

    // Runtime config
    nuxt.options.runtimeConfig.huiaHeadless = defu(
      nuxt.options.runtimeConfig.huiaHeadless as Record<string, unknown> | undefined,
      {
        huia: {
          baseUrl: opts.huia.baseUrl ?? '',
          tenant: opts.huia.tenant ?? '',
        },
        session: opts.session,
        storageBase: opts.storageBase,
        refresh: opts.refresh,
        cookie: opts.cookie,
        routes: opts.routes,
      }
    )

    nuxt.options.runtimeConfig.public.huiaHeadless = defu(
      nuxt.options.runtimeConfig.public.huiaHeadless as Record<string, unknown> | undefined,
      {
        sessionPath: opts.routes.session,
        loginPath: opts.routes.login,
        registerPath: opts.routes.register,
        logoutPath: opts.routes.logout,
        refreshPath: opts.routes.refresh,
        phoneStartPath: opts.routes.phoneStart,
        phoneVerifyPath: opts.routes.phoneVerify,
        phoneCompleteProfilePath: opts.routes.phoneCompleteProfile,
      }
    )

    // Server handlers
    addServerHandler({
      route: opts.routes.session,
      handler: resolve('./runtime/server/api/session.get'),
    })

    addServerHandler({
      route: opts.routes.login,
      handler: resolve('./runtime/server/routes/login.post'),
    })

    addServerHandler({
      route: opts.routes.register,
      handler: resolve('./runtime/server/routes/register.post'),
    })

    addServerHandler({
      route: opts.routes.logout,
      handler: resolve('./runtime/server/routes/logout.post'),
    })

    addServerHandler({
      route: opts.routes.refresh,
      handler: resolve('./runtime/server/routes/refresh.post'),
    })

    addServerHandler({
      route: opts.routes.phoneStart,
      handler: resolve('./runtime/server/routes/phone-start.post'),
    })

    addServerHandler({
      route: opts.routes.phoneVerify,
      handler: resolve('./runtime/server/routes/phone-verify.post'),
    })

    addServerHandler({
      route: opts.routes.phoneCompleteProfile,
      handler: resolve('./runtime/server/routes/complete-profile.post'),
    })

    // Composables & Server utils
    addImportsDir(resolve('./runtime/app/composables'))
    addServerImportsDir(resolve('./runtime/server/utils'))

    // App plugins & middleware
    addPlugin(resolve('./runtime/app/plugins/auth'))
    addRouteMiddleware({
      name: 'auth',
      path: resolve('./runtime/app/middleware/auth'),
    })
  },
})
