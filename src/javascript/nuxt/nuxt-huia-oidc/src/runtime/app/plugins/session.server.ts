import { defineNuxtPlugin, useState, useRequestEvent } from '#imports'
import type { Plugin } from 'nuxt/app'
import type { UserSession } from '../../types'

/**
 * SSR only. Copies the session resolved by the Nitro middleware (`event.context.huia`) into
 * `useState('huia-auth:session')`, which Nuxt serialises into the payload. The client then reads the
 * identical value on first render — no fetch, no hydration mismatch, no flash.
 */
const plugin: Plugin = defineNuxtPlugin(() => {
  const event = useRequestEvent()
  const state = useState<UserSession>('huia-auth:session', () => ({}))
  const ctx = event?.context as { huia?: UserSession } | undefined
  state.value = ctx?.huia ?? {}
})

export default plugin
