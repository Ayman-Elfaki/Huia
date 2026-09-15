'use client'

import React, { useState, Suspense } from 'react'
import { useRouter, useSearchParams } from 'next/navigation'
import { useHuia } from 'next-huia-headless/client'
import { LogIn, UserPlus, Phone, ShieldCheck, AlertCircle, CheckCircle2, Loader2 } from 'lucide-react'

function LoginContent() {
  const router = useRouter()
  const searchParams = useSearchParams()
  const returnTo = searchParams.get('returnTo') || '/'

  const {
    login,
    register,
    startPhoneLogin,
    verifyPhoneLogin,
    completePhoneProfile,
    externalLoginHref,
  } = useHuia()

  const [tab, setTab] = useState<'password' | 'phone'>('password')
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')

  // Phone state
  const [phoneStep, setPhoneStep] = useState<'number' | 'code' | 'profile'>('number')
  const [phone, setPhone] = useState('')
  const [code, setCode] = useState('')
  const [flowId, setFlowId] = useState<string | null>(null)

  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState<string | null>(null)

  const handleLogin = async (e: React.SubmitEvent) => {
    e.preventDefault()
    setError(null)
    setSubmitting(true)

    try {
      const res = await login({ email, password })
      if (res.ok) {
        router.push(returnTo)
      }
      else {
        setError('Sign-in failed. Check your email and password.')
      }
    }
    catch {
      setError('Sign-in failed.')
    }
    finally {
      setSubmitting(false)
    }
  }

  const handleRegister = async (e: React.SubmitEvent) => {
    e.preventDefault()
    setError(null)
    setSuccess(null)
    setSubmitting(true)

    try {
      const res = await register({ email, password, firstName, lastName })
      if (res.ok) {
        setSuccess('Account created — sign in below.')
        setMode('login')
      }
      else {
        setError('Registration failed. Check the password requirements.')
      }
    }
    catch {
      setError('Registration failed.')
    }
    finally {
      setSubmitting(false)
    }
  }

  const handlePhoneStart = async (e: React.SubmitEvent) => {
    e.preventDefault()
    setError(null)
    setSubmitting(true)

    try {
      const res = await startPhoneLogin({ phoneNumber: phone })
      if (res.ok && res.flowId) {
        setFlowId(res.flowId)
        setSuccess(`We sent a code to ${phone}.`)
        setPhoneStep('code')
      }
      else {
        setError('Could not send a code. Check the number.')
      }
    }
    catch {
      setError('Could not send a code.')
    }
    finally {
      setSubmitting(false)
    }
  }

  const handlePhoneVerify = async (e: React.SubmitEvent) => {
    e.preventDefault()
    if (!flowId) return
    setError(null)
    setSubmitting(true)

    try {
      const res = await verifyPhoneLogin({ flowId, code })
      if (!res.ok) {
        setError('That code did not work.')
        return
      }

      if (res.requiresProfile) {
        setFlowId(res.flowId ?? flowId)
        setPhoneStep('profile')
        return
      }

      router.push(returnTo)
    }
    catch {
      setError('That code did not work.')
    }
    finally {
      setSubmitting(false)
    }
  }

  const handlePhoneComplete = async (e: React.SubmitEvent) => {
    e.preventDefault()
    if (!flowId) return
    setError(null)
    setSubmitting(true)

    try {
      const res = await completePhoneProfile({ flowId, firstName, lastName })
      if (res.ok) {
        router.push(returnTo)
      }
      else {
        setError('Could not finish creating the account.')
      }
    }
    catch {
      setError('Could not finish creating the account.')
    }
    finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="max-w-md mx-auto py-8">
      <div className="bg-gray-900/80 border border-gray-800 rounded-2xl p-6 sm:p-8 shadow-2xl backdrop-blur">
        <div className="text-center mb-6">
          <div className="w-10 h-10 rounded-xl bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 mx-auto flex items-center justify-center mb-3">
            <ShieldCheck className="w-5 h-5" />
          </div>
          <h2 className="text-2xl font-bold text-white">
            {tab === 'phone'
              ? 'Phone Sign In'
              : mode === 'register'
                ? 'Create an account'
                : 'Sign in to Huia Shop'}
          </h2>
          <p className="text-xs text-gray-400 mt-1">
            Pure JSON headless identity with opaque bearer tokens
          </p>
        </div>

        {/* Tab switchers */}
        <div className="flex rounded-xl bg-gray-800/60 p-1 mb-6 border border-gray-800">
          <button
            type="button"
            onClick={() => { setTab('password'); setError(null); }}
            className={`flex-1 py-1.5 text-xs font-medium rounded-lg transition ${tab === 'password' ? 'bg-emerald-600 text-white shadow' : 'text-gray-400 hover:text-gray-200'}`}
          >
            Password
          </button>
          <button
            type="button"
            onClick={() => { setTab('phone'); setError(null); }}
            className={`flex-1 py-1.5 text-xs font-medium rounded-lg transition ${tab === 'phone' ? 'bg-emerald-600 text-white shadow' : 'text-gray-400 hover:text-gray-200'}`}
          >
            Phone
          </button>
        </div>

        {error && (
          <div className="p-3 mb-4 rounded-xl bg-red-500/10 border border-red-500/20 text-red-400 text-xs flex items-center gap-2">
            <AlertCircle className="w-4 h-4 shrink-0" />
            <span>{error}</span>
          </div>
        )}

        {success && (
          <div className="p-3 mb-4 rounded-xl bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 text-xs flex items-center gap-2">
            <CheckCircle2 className="w-4 h-4 shrink-0" />
            <span>{success}</span>
          </div>
        )}

        {/* Email/Password Login Form */}
        {tab === 'password' && mode === 'login' && (
          <form onSubmit={handleLogin} className="space-y-4">
            <div>
              <label className="block text-xs font-medium text-gray-300 mb-1.5">Email</label>
              <input
                type="email"
                placeholder="Email"
                value={email}
                onChange={e => setEmail(e.target.value)}
                required
                className="w-full px-4 py-2.5 rounded-xl bg-gray-950 border border-gray-800 focus:border-emerald-500 focus:outline-none text-sm text-gray-100 placeholder-gray-500 transition"
              />
            </div>

            <div>
              <label className="block text-xs font-medium text-gray-300 mb-1.5">Password</label>
              <input
                type="password"
                placeholder="Password"
                value={password}
                onChange={e => setPassword(e.target.value)}
                required
                className="w-full px-4 py-2.5 rounded-xl bg-gray-950 border border-gray-800 focus:border-emerald-500 focus:outline-none text-sm text-gray-100 placeholder-gray-500 transition"
              />
            </div>

            <button
              type="submit"
              disabled={submitting}
              className="w-full py-2.5 px-4 rounded-xl bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 text-white font-medium text-sm transition shadow-lg shadow-emerald-600/20 flex items-center justify-center gap-2"
            >
              {submitting ? <Loader2 className="w-4 h-4 animate-spin" /> : <LogIn className="w-4 h-4" />}
              Sign in
            </button>

            <div className="pt-2 text-center">
              <button
                type="button"
                onClick={() => { setMode('register'); setError(null); }}
                className="text-xs text-emerald-400 hover:underline"
              >
                Don't have an account? Register
              </button>
            </div>
          </form>
        )}

        {/* Register Form */}
        {tab === 'password' && mode === 'register' && (
          <form onSubmit={handleRegister} className="space-y-4">
            <div className="grid grid-cols-2 gap-3">
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
            </div>

            <div>
              <label className="block text-xs font-medium text-gray-300 mb-1.5">Email</label>
              <input
                type="email"
                placeholder="Email"
                value={email}
                onChange={e => setEmail(e.target.value)}
                required
                className="w-full px-4 py-2.5 rounded-xl bg-gray-950 border border-gray-800 focus:border-emerald-500 focus:outline-none text-sm text-gray-100 placeholder-gray-500 transition"
              />
            </div>

            <div>
              <label className="block text-xs font-medium text-gray-300 mb-1.5">Password</label>
              <input
                type="password"
                placeholder="Password"
                value={password}
                onChange={e => setPassword(e.target.value)}
                required
                className="w-full px-4 py-2.5 rounded-xl bg-gray-950 border border-gray-800 focus:border-emerald-500 focus:outline-none text-sm text-gray-100 placeholder-gray-500 transition"
              />
            </div>

            <button
              type="submit"
              disabled={submitting}
              className="w-full py-2.5 px-4 rounded-xl bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 text-white font-medium text-sm transition shadow-lg shadow-emerald-600/20 flex items-center justify-center gap-2"
            >
              {submitting ? <Loader2 className="w-4 h-4 animate-spin" /> : <UserPlus className="w-4 h-4" />}
              Create account
            </button>

            <div className="pt-2 text-center">
              <button
                type="button"
                onClick={() => { setMode('login'); setError(null); }}
                className="text-xs text-gray-400 hover:underline"
              >
                Already have an account? Sign in
              </button>
            </div>
          </form>
        )}

        {/* Phone SMS OTP Form */}
        {tab === 'phone' && (
          <div>
            {phoneStep === 'number' && (
              <form onSubmit={handlePhoneStart} className="space-y-4">
                <div>
                  <label className="block text-xs font-medium text-gray-300 mb-1.5">Phone number</label>
                  <input
                    type="tel"
                    placeholder="+1 202 555 0123"
                    value={phone}
                    onChange={e => setPhone(e.target.value)}
                    required
                    className="w-full px-4 py-2.5 rounded-xl bg-gray-950 border border-gray-800 focus:border-emerald-500 focus:outline-none text-sm text-gray-100 placeholder-gray-500 transition"
                  />
                </div>

                <button
                  type="submit"
                  disabled={submitting}
                  className="w-full py-2.5 px-4 rounded-xl bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 text-white font-medium text-sm transition shadow-lg shadow-emerald-600/20 flex items-center justify-center gap-2"
                >
                  {submitting ? <Loader2 className="w-4 h-4 animate-spin" /> : <Phone className="w-4 h-4" />}
                  Send code
                </button>
              </form>
            )}

            {phoneStep === 'code' && (
              <form onSubmit={handlePhoneVerify} className="space-y-4">
                <p className="text-xs text-gray-400">
                  We sent a code to {phone}.
                </p>
                <div>
                  <label className="block text-xs font-medium text-gray-300 mb-1.5">Code</label>
                  <input
                    type="text"
                    placeholder="123456"
                    value={code}
                    onChange={e => setCode(e.target.value)}
                    required
                    className="w-full px-4 py-2.5 rounded-xl bg-gray-950 border border-gray-800 focus:border-emerald-500 focus:outline-none text-sm text-gray-100 placeholder-gray-500 transition text-center tracking-widest text-lg font-mono"
                  />
                </div>

                <button
                  type="submit"
                  disabled={submitting}
                  className="w-full py-2.5 px-4 rounded-xl bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 text-white font-medium text-sm transition shadow-lg shadow-emerald-600/20 flex items-center justify-center gap-2"
                >
                  {submitting ? <Loader2 className="w-4 h-4 animate-spin" /> : <LogIn className="w-4 h-4" />}
                  Verify
                </button>
              </form>
            )}

            {phoneStep === 'profile' && (
              <form onSubmit={handlePhoneComplete} className="space-y-4">
                <p className="text-sm text-gray-300 font-medium">
                  Almost done — what's your name?
                </p>
                <div className="grid grid-cols-2 gap-3">
                  <div>
                    <label className="block text-xs font-medium text-gray-300 mb-1.5">First name</label>
                    <input
                      type="text"
                      placeholder="First name"
                      value={firstName}
                      onChange={e => setFirstName(e.target.value)}
                      required
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
                      required
                      className="w-full px-4 py-2.5 rounded-xl bg-gray-950 border border-gray-800 focus:border-emerald-500 focus:outline-none text-sm text-gray-100 placeholder-gray-500 transition"
                    />
                  </div>
                </div>

                <button
                  type="submit"
                  disabled={submitting}
                  className="w-full py-2.5 px-4 rounded-xl bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 text-white font-medium text-sm transition shadow-lg shadow-emerald-600/20 flex items-center justify-center gap-2"
                >
                  {submitting ? <Loader2 className="w-4 h-4 animate-spin" /> : <CheckCircle2 className="w-4 h-4" />}
                  Finish
                </button>
              </form>
            )}
          </div>
        )}

        {/* External Provider Footer */}
        <div className="mt-6 pt-6 border-t border-gray-800/80">
          <a
            href={externalLoginHref('huia', returnTo)}
            className="w-full py-2.5 px-4 rounded-xl border border-gray-800 hover:bg-gray-800/60 text-gray-300 font-medium text-xs transition flex items-center justify-center gap-2"
          >
            Sign in with Partner
          </a>
        </div>
      </div>
    </div>
  )
}

export default function LoginPage() {
  return (
    <Suspense fallback={
      <div className="flex items-center justify-center py-20 text-gray-400">
        <Loader2 className="w-8 h-8 animate-spin text-emerald-500" />
      </div>
    }>
      <LoginContent />
    </Suspense>
  )
}
