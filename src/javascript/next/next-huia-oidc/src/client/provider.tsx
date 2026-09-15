'use client'

import React, { createContext, useContext, useState, useEffect, useCallback } from 'react'
import type { UserSession, UserClaims } from '../types.js'

export interface HuiaOidcContextValue {
  session: UserSession
  user: UserClaims | null
  loggedIn: boolean
  expiresAt?: number
  loading: boolean
  fetch: () => Promise<void>
  clear: () => Promise<void>
  clearLocal: () => void
  login: (options?: { returnTo?: string; extra?: Record<string, string> }) => void
  hasRole: (role: string) => boolean
  hasAnyRole: (...roles: string[]) => boolean
}

export const HuiaOidcContext = createContext<HuiaOidcContextValue | null>(null)

export interface HuiaOidcProviderProps {
  children: React.ReactNode
  initialSession?: UserSession
  sessionEndpoint?: string
  logoutEndpoint?: string
  loginEndpoint?: string
}

export function HuiaOidcProvider({
  children,
  initialSession,
  sessionEndpoint = '/api/auth/session',
  logoutEndpoint = '/api/auth/logout',
  loginEndpoint = '/api/auth/login',
}: HuiaOidcProviderProps) {
  const [session, setSession] = useState<UserSession>(initialSession ?? {})
  const [loading, setLoading] = useState<boolean>(!initialSession)

  const fetchSession = useCallback(async () => {
    try {
      setLoading(true)
      const res = await fetch(sessionEndpoint)
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
  }, [sessionEndpoint])

  useEffect(() => {
    if (!initialSession) {
      void fetchSession()
    }
  }, [initialSession, fetchSession])

  const clear = useCallback(async () => {
    window.location.href = logoutEndpoint
  }, [logoutEndpoint])

  const clearLocal = useCallback(() => {
    setSession({ user: null, loggedIn: false })
  }, [])

  const login = useCallback(
    (options?: { returnTo?: string; extra?: Record<string, string> }) => {
      const q = new URLSearchParams()
      if (options?.returnTo) q.set('returnTo', options.returnTo)
      if (options?.extra) {
        for (const [k, v] of Object.entries(options.extra)) {
          q.set(k, v)
        }
      }
      const qs = q.toString()
      window.location.href = qs ? `${loginEndpoint}?${qs}` : loginEndpoint
    },
    [loginEndpoint],
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
    <HuiaOidcContext.Provider
      value={{
        session,
        user,
        loggedIn,
        expiresAt: session.expiresAt,
        loading,
        fetch: fetchSession,
        clear,
        clearLocal,
        login,
        hasRole,
        hasAnyRole,
      }}
    >
      {children}
    </HuiaOidcContext.Provider>
  )
}
