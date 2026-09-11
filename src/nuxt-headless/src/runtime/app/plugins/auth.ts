import { defineNuxtPlugin } from '#app'
import { useUserSession } from '../composables/useUserSession'

export default defineNuxtPlugin(async () => {
  const { fetch } = useUserSession()
  await fetch()
})
