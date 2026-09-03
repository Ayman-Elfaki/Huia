// Adds the signed-in user's access token to every nuxt-api-party upstream request. Runs only on the
// Nitro server, so the token never reaches the browser (it lives in the nuxt-oidc-auth session).
// `getUserSession` is auto-imported in server routes but not in Nitro plugins, hence the explicit path.
import { getUserSession } from 'nuxt-oidc-auth/runtime/server/utils/session.js'

export default defineNitroPlugin((nitro) => {
  nitro.hooks.hook('api-party:request', async (ctx, event) => {
    const session = await getUserSession(event)
    const token = (session as any)?.accessToken ?? (session as any)?.tokens?.accessToken
    if (typeof token === 'string' && token.length > 0) {
      const headers = new Headers(ctx.options.headers as HeadersInit | undefined)
      headers.set('Authorization', `Bearer ${token}`)
      ctx.options.headers = headers
    }
  })
})
