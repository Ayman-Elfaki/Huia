import { useContext } from 'react'
import { HuiaOidcContext, type HuiaOidcContextValue } from './provider.js'

export function useUserSession(): HuiaOidcContextValue {
  const context = useContext(HuiaOidcContext)
  if (!context) {
    throw new Error('useUserSession must be used within a <HuiaOidcProvider>')
  }
  return context
}
