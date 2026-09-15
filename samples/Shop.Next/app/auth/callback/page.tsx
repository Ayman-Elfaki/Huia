'use client'

import React, { useState, useEffect, useCallback, Suspense } from 'react'
import { useRouter, useSearchParams } from 'next/navigation'
import { useHuia } from 'next-huia-headless/client'
import { Loader2, AlertCircle, CheckCircle2 } from 'lucide-react'

function AuthCallbackContent() {
  const router = useRouter()
  const searchParams = useSearchParams()
  const { exchangeExternalCode, completeExternalProfile } = useHuia()

  const code = searchParams.get('code')
  const returnTo = searchParams.get('returnTo') || '/'

  const [step, setStep] = useState<'working' | 'profile' | 'error'>('working')
  const [flowCode, setFlowCode] = useState('')
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const run = useCallback(async () => {
    if (!code) {
      setStep('error')
      setError('No sign-in code was returned.')
      return
    }

    try {
      const result = await exchangeExternalCode(code)
      if (!result.ok) {
        setStep('error')
        setError('Sign-in did not complete. Please try again.')
        return
      }

      if (result.requiresProfile) {
        setFlowCode(result.flowId || code)
        setFirstName(result.firstName ?? '')
        setLastName(result.lastName ?? '')
        setStep('profile')
        return
      }

      router.push(returnTo)
    }
    catch {
      setStep('error')
      setError('Sign-in failed. Please try again.')
    }
  }, [code, exchangeExternalCode, router, returnTo])

  useEffect(() => {
    void run()
  }, [run])

  const onCompleteProfile = async (e: React.FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setError(null)

    try {
      const result = await completeExternalProfile({
        code: flowCode,
        firstName,
        lastName,
      })
      if (!result.ok) {
        setError('Could not finish creating the account.')
        return
      }
      router.push(returnTo)
    }
    catch {
      setError('Could not finish creating the account.')
    }
    finally {
      setBusy(false)
    }
  }

  return (
    <div className="max-w-md mx-auto py-12">
      <div className="bg-gray-900/80 border border-gray-800 rounded-2xl p-6 sm:p-8 shadow-2xl backdrop-blur">
        {step === 'working' && (
          <div className="flex items-center justify-center gap-3 text-gray-400 py-6">
            <Loader2 className="w-5 h-5 animate-spin text-emerald-500" />
            <span className="text-sm">Completing sign-in…</span>
          </div>
        )}

        {step === 'profile' && (
          <div>
            <h2 className="text-xl font-bold text-white mb-2">
              Almost done — what's your name?
            </h2>
            <p className="text-xs text-gray-400 mb-6">
              Please complete your profile to finish setting up your account.
            </p>

            {error && (
              <div className="p-3 mb-4 rounded-xl bg-red-500/10 border border-red-500/20 text-red-400 text-xs flex items-center gap-2">
                <AlertCircle className="w-4 h-4 shrink-0" />
                <span>{error}</span>
              </div>
            )}

            <form onSubmit={onCompleteProfile} className="space-y-4">
              <div>
                <label className="block text-xs font-medium text-gray-300 mb-1.5">First name</label>
                <input
                  type="text"
                  placeholder="First name"
                  value={firstName}
                  onChange={e => setFirstName(e.target.value)}
                  className="w-full px-4 py-2.5 rounded-xl bg-gray-950 border border-gray-800 focus:border-emerald-500 focus:outline-none text-sm text-gray-100 placeholder-gray-500 transition"
                />
              </div>

              <div>
                <label className="block text-xs font-medium text-gray-300 mb-1.5">Last name</label>
                <input
                  type="text"
                  placeholder="Last name"
                  value={lastName}
                  onChange={e => setLastName(e.target.value)}
                  className="w-full px-4 py-2.5 rounded-xl bg-gray-950 border border-gray-800 focus:border-emerald-500 focus:outline-none text-sm text-gray-100 placeholder-gray-500 transition"
                />
              </div>

              <button
                type="submit"
                disabled={busy}
                className="w-full py-2.5 px-4 rounded-xl bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 text-white font-medium text-sm transition shadow-lg shadow-emerald-600/20 flex items-center justify-center gap-2"
              >
                {busy ? <Loader2 className="w-4 h-4 animate-spin" /> : <CheckCircle2 className="w-4 h-4" />}
                Finish
              </button>
            </form>
          </div>
        )}

        {step === 'error' && (
          <div className="text-center py-4">
            <div className="p-3 mb-4 rounded-xl bg-red-500/10 border border-red-500/20 text-red-400 text-xs flex items-center gap-2">
              <AlertCircle className="w-4 h-4 shrink-0" />
              <span>{error}</span>
            </div>
            <button
              onClick={() => router.push('/login')}
              className="text-xs text-emerald-400 hover:underline"
            >
              Back to sign in
            </button>
          </div>
        )}
      </div>
    </div>
  )
}

export default function AuthCallbackPage() {
  return (
    <Suspense fallback={
      <div className="flex items-center justify-center py-20 text-gray-400">
        <Loader2 className="w-8 h-8 animate-spin text-emerald-500" />
      </div>
    }>
      <AuthCallbackContent />
    </Suspense>
  )
}
