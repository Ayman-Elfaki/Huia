// Adds the signed-in user's access token to every nuxt-api-party upstream request. Runs only on the
// Nitro server, so the token never reaches the browser — it lives in Nitro Storage, keyed by the
// opaque session id in the huia-auth cookie. getAccessToken() also refreshes a near-expired token.
export default defineNitroPlugin((nitro) => {
  nitro.hooks.hook('api-party:request', async (ctx, event) => {
    const token = await getAccessToken(event)
    if (token) {
      const headers = new Headers(ctx.options.headers as HeadersInit | undefined)
      headers.set('Authorization', `Bearer ${token}`)
      ctx.options.headers = headers
    }
  })
})
