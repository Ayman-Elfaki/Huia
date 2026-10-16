import { useContext } from 'react'
import { HuiaHeadlessContext, type HuiaHeadlessContextValue } from './provider.js'

export function useHuia(): HuiaHeadlessContextValue {
  const context = useContext(HuiaHeadlessContext)
  if (!context) {
    throw new Error('useHuia must be used within a <HuiaHeadlessProvider>')
  }
  return context
}

export function useUserSession(): HuiaHeadlessContextValue {
  return useHuia()
}
