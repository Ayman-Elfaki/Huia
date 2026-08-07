import type { H3Event } from 'h3'

/**
 * Attaches the signed-in user's access token to every proxied nuxt-api-party request server-side, replacing
 * the hand-rolled server/api/admin/[...path].ts and server/api/manage/[...path].ts reverse proxies. The
 * `api-party:request:${endpointId}` Nitro hook receives the real incoming h3 event, so the same
 * session/role checks those proxies did can run here unchanged — the token itself is never exposed to the
 * browser (see nuxt.config.ts's apiParty.endpoints; the client-side composables always call through this
 * server-side hook, never the backend directly).
 */
export default defineNitroPlugin((nitroApp) => {
  nitroApp.hooks.hook('api-party:request:huiaAdmin', async (ctx, event) => {
    const session = await authorizeAdminRequest(event)
    ctx.options.headers.set('Authorization', `Bearer ${session.accessToken}`)
  })

  nitroApp.hooks.hook('api-party:request:huiaManage', async (ctx, event) => {
    const session = await requireUserSessionWithRetry(event)
    ctx.options.headers.set('Authorization', `Bearer ${session.accessToken}`)
  })
})

/**
 * MapHuiaAdminEndpoints itself carries no authorization policy by default (see its own doc comment) —
 * samples/Huia.TodoApi/Program.cs gates the whole group behind the "Admin" role there, so a non-admin
 * token still gets a 403 from Huia itself. The check below exists to fail fast with a clear error before
 * making a round trip, not as the only line of defense.
 */
async function authorizeAdminRequest(event: H3Event) {
  const session = await requireUserSessionWithRetry(event)

  const roles = normalizeRoles(session.claims?.role)
  if (!roles.includes('Admin')) {
    throw createError({ statusCode: 403, statusMessage: 'Admin role required' })
  }

  return session
}
