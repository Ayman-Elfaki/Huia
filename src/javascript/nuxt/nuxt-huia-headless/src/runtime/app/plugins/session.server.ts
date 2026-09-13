import { defineNuxtPlugin, useState, useRequestEvent } from '#imports'
import type { Plugin } from 'nuxt/app'
import type { UserSession } from '../../types'

/**
 * SSR only. Copies the session resolved by the Nitro middleware (`event.context.huiaHeadless`) into
 * `useState('huia-headless-auth:session')`, which Nuxt serialises into the payload. The client then
 * reads the identical value on first render — no fetch, no hydration mismatch, no flash.
 */
const plugin: Plugin = defineNuxtPlugin(() => {
  const event = useRequestEvent()
  const state = useState<UserSession>('huia-headless-auth:session', () => ({}))
  const ctx = event?.context as { huiaHeadless?: UserSession } | undefined
  state.value = ctx?.huiaHeadless ?? {}
})

export default plugin
