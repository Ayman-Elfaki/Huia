export default defineNuxtRouteMiddleware(() => {
  const { loggedIn } = useOidcAuth()
  if (!loggedIn.value) {
    return navigateTo('/')
  }
})
