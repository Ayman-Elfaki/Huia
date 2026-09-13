'use client'

import React, { createContext, useContext, useState, useEffect, useCallback } from 'react'
import type { UserSession, UserClaims, HuiaLoginResult, HuiaFlowResult } from '../types.js'

export interface HuiaHeadlessContextValue {
  session: UserSession
  user: UserClaims | null
  loggedIn: boolean
  expiresAt?: number
  loading: boolean
  fetch: () => Promise<void>
  clear: () => Promise<void>
  clearLocal: () => void
  hasRole: (role: string) => boolean
  hasAnyRole: (...roles: string[]) => boolean

  register: (opts: { email: string; password: string; firstName?: string; lastName?: string }) => Promise<{ ok: boolean; error?: unknown }>
  login: (opts: { email: string; password: string; twoFactorCode?: string; twoFactorRecoveryCode?: string }) => Promise<HuiaLoginResult>
  startPhoneLogin: (opts: { phoneNumber: string; country?: string; captchaResponse?: string }) => Promise<{ ok: boolean; flowId?: string; expiresInSeconds?: number; error?: unknown }>
  verifyPhoneLogin: (opts: { flowId: string; code: string }) => Promise<HuiaFlowResult>
  completePhoneProfile: (opts: { flowId: string; firstName: string; lastName: string }) => Promise<HuiaLoginResult>
  externalLoginHref: (provider: string, returnTo?: string) => string
  exchangeExternalCode: (code: string) => Promise<HuiaFlowResult>
  completeExternalProfile: (opts: { code: string; firstName: string; lastName: string }) => Promise<HuiaLoginResult>
}

export const HuiaHeadlessContext = createContext<HuiaHeadlessContextValue | null>(null)

export interface HuiaHeadlessProviderProps {
  children: React.ReactNode
  initialSession?: UserSession
  baseAuthPath?: string // default '/api/auth'
}

export function HuiaHeadlessProvider({
  children,
  initialSession,
  baseAuthPath = '/api/auth',
}: HuiaHeadlessProviderProps) {
  const [session, setSession] = useState<UserSession>(initialSession ?? {})
  const [loading, setLoading] = useState<boolean>(!initialSession)

  const fetchSession = useCallback(async () => {
    try {
      setLoading(true)
      const res = await fetch(`${baseAuthPath}/session`)
      if (res.ok) {
        const data = (await res.json()) as UserSession
        setSession(data)
      }
      else {
        setSession({ user: null, loggedIn: false })
      }
    }
    catch {
      setSession({ user: null, loggedIn: false })
    }
    finally {
      setLoading(false)
    }
  }, [baseAuthPath])

  useEffect(() => {
    if (!initialSession) {
      void fetchSession()
    }
  }, [initialSession, fetchSession])

  const clear = useCallback(async () => {
    try {
      await fetch(`${baseAuthPath}/logout`, { method: 'POST' })
    }
    finally {
      setSession({ user: null, loggedIn: false })
    }
  }, [baseAuthPath])

  const clearLocal = useCallback(() => {
    setSession({ user: null, loggedIn: false })
  }, [])

  const register = useCallback(
    async (opts: { email: string; password: string; firstName?: string; lastName?: string }) => {
      try {
        const res = await fetch(`${baseAuthPath}/register`, {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify(opts),
        })
        if (!res.ok) {
          const err = await res.json().catch(() => ({}))
          return { ok: false, error: err }
        }
        return { ok: true }
      }
      catch (error) {
        return { ok: false, error }
      }
    },
    [baseAuthPath],
  )

  const login = useCallback(
    async (opts: { email: string; password: string; twoFactorCode?: string; twoFactorRecoveryCode?: string }) => {
      try {
        const res = await fetch(`${baseAuthPath}/login`, {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify(opts),
        })
        if (!res.ok) {
          const err = await res.json().catch(() => ({}))
          return { ok: false, error: err }
        }
        const data = await res.json()
        await fetchSession()
        return { ok: true, user: data.user }
      }
      catch (error) {
        return { ok: false, error }
      }
    },
    [baseAuthPath, fetchSession],
  )

  const startPhoneLogin = useCallback(
    async (opts: { phoneNumber: string; country?: string; captchaResponse?: string }) => {
      try {
        const res = await fetch(`${baseAuthPath}/phone-start`, {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify(opts),
        })
        const data = await res.json().catch(() => ({}))
        if (!res.ok) return { ok: false, error: data }
        return { ok: true, flowId: data.flowId, expiresInSeconds: data.expiresInSeconds }
      }
      catch (error) {
        return { ok: false, error }
      }
    },
    [baseAuthPath],
  )

  const verifyPhoneLogin = useCallback(
    async (opts: { flowId: string; code: string }): Promise<HuiaFlowResult> => {
      try {
        const res = await fetch(`${baseAuthPath}/phone-verify`, {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify(opts),
        })
        const data = await res.json().catch(() => ({}))
        if (!res.ok) return { ok: false, requiresProfile: false, error: data }

        if (data.requiresProfile) {
          return {
            ok: true,
            requiresProfile: true,
            flowId: data.flowId ?? opts.flowId,
            firstName: data.firstName,
            lastName: data.lastName,
          }
        }

        await fetchSession()
        return { ok: true, requiresProfile: false, user: data.user }
      }
      catch (error) {
        return { ok: false, requiresProfile: false, error }
      }
    },
    [baseAuthPath, fetchSession],
  )

  const completePhoneProfile = useCallback(
    async (opts: { flowId: string; firstName: string; lastName: string }): Promise<HuiaLoginResult> => {
      try {
        const res = await fetch(`${baseAuthPath}/phone-complete-profile`, {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify(opts),
        })
        const data = await res.json().catch(() => ({}))
        if (!res.ok) return { ok: false, error: data }

        await fetchSession()
        return { ok: true, user: data.user }
      }
      catch (error) {
        return { ok: false, error }
      }
    },
    [baseAuthPath, fetchSession],
  )

  const externalLoginHref = useCallback(
    (provider: string, returnTo = '/'): string => {
      return `${baseAuthPath}/external/${encodeURIComponent(provider)}?returnUrl=${encodeURIComponent(returnTo)}`
    },
    [baseAuthPath],
  )

  const exchangeExternalCode = useCallback(
    async (code: string): Promise<HuiaFlowResult> => {
      try {
        const res = await fetch(`${baseAuthPath}/external-exchange`, {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify({ code }),
        })
        const data = await res.json().catch(() => ({}))
        if (!res.ok) return { ok: false, requiresProfile: false, error: data }

        if (data.requiresProfile) {
          return {
            ok: true,
            requiresProfile: true,
            flowId: data.code ?? code,
            email: data.email,
            firstName: data.firstName,
            lastName: data.lastName,
          }
        }

        await fetchSession()
        return { ok: true, requiresProfile: false, user: data.user }
      }
      catch (error) {
        return { ok: false, requiresProfile: false, error }
      }
    },
    [baseAuthPath, fetchSession],
  )

  const completeExternalProfile = useCallback(
    async (opts: { code: string; firstName: string; lastName: string }): Promise<HuiaLoginResult> => {
      try {
        const res = await fetch(`${baseAuthPath}/external-complete-profile`, {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify(opts),
        })
        const data = await res.json().catch(() => ({}))
        if (!res.ok) return { ok: false, error: data }

        await fetchSession()
        return { ok: true, user: data.user }
      }
      catch (error) {
        return { ok: false, error }
      }
    },
    [baseAuthPath, fetchSession],
  )

  const hasRole = useCallback(
    (role: string): boolean => {
      return !!session.user?.roles?.includes(role)
    },
    [session],
  )

  const hasAnyRole = useCallback(
    (...roles: string[]): boolean => {
      const userRoles = session.user?.roles
      return !!userRoles && roles.some(r => userRoles.includes(r))
    },
    [session],
  )

  const user = session.user ?? null
  const loggedIn = !!session.loggedIn && !!user

  return (
    <HuiaHeadlessContext.Provider
      value={{
        session,
        user,
        loggedIn,
        expiresAt: session.expiresAt,
        loading,
        fetch: fetchSession,
        clear,
        clearLocal,
        hasRole,
        hasAnyRole,
        register,
        login,
        startPhoneLogin,
        verifyPhoneLogin,
        completePhoneProfile,
        externalLoginHref,
        exchangeExternalCode,
        completeExternalProfile,
      }}
    >
      {children}
    </HuiaHeadlessContext.Provider>
  )
}
