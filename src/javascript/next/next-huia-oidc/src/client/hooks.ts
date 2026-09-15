'use client'

import { useContext } from 'react'
import { HuiaOidcContext, type HuiaOidcContextValue } from './provider.js'

export function useUserSession(): HuiaOidcContextValue {
  const ctx = useContext(HuiaOidcContext)
  if (!ctx) {
    throw new Error('[next-huia-oidc] `useUserSession` must be used within a `<HuiaOidcProvider>` component.')
  }
  return ctx
}
