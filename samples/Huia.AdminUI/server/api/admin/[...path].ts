import { requireUserSession } from 'nuxt-oidc-auth/runtime/server/utils/session.js'

/**
 * Transparent reverse proxy from this app's own origin (/api/admin/**) to Huia's
 * /api/identity/admin/** JSON API (see MapHuiaAdminEndpoints in src/Huia/Endpoints/EndpointRouteBuilderExtensions.cs),
 * attaching the signed-in user's access token server-side — pages/composables call this instead of Huia
 * directly, the same confidential-client pattern samples/Huia.TodoApp's src/lib/todo-api.ts uses. The token
 * itself is exposed on the session object (see nuxt.config.ts's exposeAccessToken), so a client-side script
 * that explicitly reads useOidcAuth().user.accessToken could get at it — same trust model TodoApp's own
 * Auth.js session uses — but nothing in this app's own UI ever reads or displays it.
 *
 * MapHuiaAdminEndpoints itself carries no authorization policy by default (see its own doc comment) —
 * samples/Huia.TodoApi/Program.cs gates the whole group behind the "Admin" role there, so a non-admin
 * token still gets a 403 from Huia itself. The check below exists to fail fast with a clear error before
 * making a round trip, not as the only line of defense.
 */
export default defineEventHandler(async (event) => {
  const session = await requireUserSession(event)

  const roles = normalizeRoles(session.claims?.role)
  if (!roles.includes('Admin')) {
    throw createError({ statusCode: 403, statusMessage: 'Admin role required' })
  }

  const path = getRouterParam(event, 'path') ?? ''
  const { huiaIssuer } = useRuntimeConfig()
  const target = `${huiaIssuer}/api/identity/admin/${path}${getRequestURL(event).search}`

  return proxyRequest(event, target, {
    headers: { Authorization: `Bearer ${session.accessToken}` }
  })
})
