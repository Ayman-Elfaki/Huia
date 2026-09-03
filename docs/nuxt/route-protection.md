# `huia-auth-nuxt` — route protection

## Client — pages

The module registers a named route middleware `auth` (opt-in per page):

```vue
<script setup lang="ts">
definePageMeta({ middleware: 'auth' })
</script>
```

It reads `useUserSession().loggedIn` — which is correct during SSR — so an unauthenticated visitor is
redirected to `/auth/oidc/login?returnTo=<path>` before any protected HTML is sent: no flash, no
client-side bounce.

Set `huiaAuth.middleware.global = true` to protect everything, with
`huiaAuth.middleware.exclude = ['/', '/about', '/auth/**']` for the public routes. To keep the old
"bounce to the landing page" UX instead of going straight to Huia, add an app `middleware/auth.ts`
that re-implements the check with `navigateTo('/')` (the sample apps do this).

## Server — API routes

```ts
// server/api/me.get.ts
export default defineEventHandler(async (event) => {
  const { user } = await requireUserSession(event)   // 401 { code: 'auth_required' } if anonymous
  return { id: user.sub, name: user.name, roles: user.roles ?? [] }
})
```

`requireUserSession` throws `createError({ statusCode: 401, … })` and runs the same
resolve-and-refresh path as `getUserSession`, so a protected API call also refreshes a near-expired
token transparently.

## Forwarding the access token upstream

Never send the token to the browser. On the Nitro server:

```ts
// server/plugins/upstream-auth.ts — with nuxt-api-party
export default defineNitroPlugin((nitro) => {
  nitro.hooks.hook('api-party:request', async (ctx, event) => {
    const token = await getAccessToken(event)   // server-only; refreshes transparently
    if (token) {
      const headers = new Headers(ctx.options.headers as HeadersInit | undefined)
      headers.set('Authorization', `Bearer ${token}`)
      ctx.options.headers = headers
    }
  })
})
```

Or in a plain proxy route:

```ts
// server/api/todos.get.ts
export default defineEventHandler(async (event) => {
  const token = await getAccessToken(event)
  if (!token) throw createError({ statusCode: 401 })
  return $fetch(`${useRuntimeConfig().todoApiUrl}/todos`, {
    headers: { Authorization: `Bearer ${token}` },
  })
})
```

## `useAuth()`

```ts
const { login, logout, user, loggedIn } = useAuth()

login({ returnTo: '/dashboard', locale: 'ar' })   // → /auth/oidc/login?returnTo=…&ui_locales=ar
logout({ returnTo: '/' })                          // → /auth/oidc/logout?returnTo=/
```
