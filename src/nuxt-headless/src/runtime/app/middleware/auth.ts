import { defineNuxtRouteMiddleware, navigateTo } from '#app'
import { useUserSession } from '../composables/useUserSession'

export default defineNuxtRouteMiddleware((to) => {
  const { loggedIn } = useUserSession()
  if (!loggedIn.value) {
    return navigateTo('/login')
  }
})
