// Overrides the huia-auth-nuxt built-in `auth` middleware: bounce unauthenticated visitors to the
// landing page (which shows the sign-in card) rather than straight to the Huia authorize endpoint.
export default defineNuxtRouteMiddleware(() => {
  const { loggedIn } = useUserSession()
  if (!loggedIn.value) {
    return navigateTo('/')
  }
})
