import { requireUserSession } from 'nuxt-oidc-auth/runtime/server/utils/session.js'

/**
 * Transparent reverse proxy from this app's own origin (/api/manage/**) to Huia's self-service
 * /api/identity/manage/** JSON API (see MapHuiaManageEndpoints in
 * src/Huia/Endpoints/EndpointRouteBuilderExtensions.cs), attaching the signed-in user's own access token —
 * same pattern as server/api/admin/[...path].ts, minus that one's "Admin" role gate: managing your own
 * account isn't an admin-only operation (every route in this app already requires being signed in and
 * having the "Admin" role to reach the dashboard at all — see app.vue — but that's a separate, broader gate
 * than what this specific API needs).
 */
export default defineEventHandler(async (event) => {
  const session = await requireUserSession(event)

  const path = getRouterParam(event, 'path') ?? ''
  const { huiaIssuer } = useRuntimeConfig()
  const target = `${huiaIssuer}/api/identity/manage/${path}${getRequestURL(event).search}`

  return proxyRequest(event, target, {
    headers: { Authorization: `Bearer ${session.accessToken}` }
  })
})
