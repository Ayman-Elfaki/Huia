import React from 'react'
import { Link } from '@tanstack/react-router'
import { useUserSession } from '@huia/tanstack-start-oidc/client'
import { CheckSquare, LogIn, LogOut, User as UserIcon } from 'lucide-react'

export function Header() {
  const { user, loggedIn, login, clear } = useUserSession()

  return (
    <header className="border-b border-gray-800/80 bg-gray-900/60 backdrop-blur sticky top-0 z-50">
      <div className="max-w-5xl mx-auto px-6 h-16 flex items-center justify-between">
        <Link to="/" className="flex items-center gap-2.5 font-bold text-lg tracking-tight text-white hover:opacity-90 transition">
          <div className="w-8 h-8 rounded-lg bg-gradient-to-tr from-cyan-600 to-blue-500 flex items-center justify-center shadow-lg shadow-cyan-500/20">
            <CheckSquare className="w-5 h-5 text-white" />
          </div>
          <span>Huia Todo <span className="text-xs px-2 py-0.5 rounded-full bg-cyan-500/10 text-cyan-400 border border-cyan-500/20 font-medium ml-1">TanStack Start</span></span>
        </Link>

        <div className="flex items-center gap-4">
          {loggedIn ? (
            <div className="flex items-center gap-4">
              <div className="flex items-center gap-2 text-sm text-gray-300">
                <div className="w-7 h-7 rounded-full bg-gray-800 border border-gray-700 flex items-center justify-center text-gray-400">
                  <UserIcon className="w-4 h-4" />
                </div>
                <span data-testid="user-name" className="font-medium">
                  {user?.name || user?.email || user?.sub}
                </span>
                {user?.roles && user.roles.length > 0 && (
                  <span className="text-xs px-2 py-0.5 rounded bg-gray-800 border border-gray-700 text-gray-400">
                    {user.roles.join(', ')}
                  </span>
                )}
              </div>
              <button
                onClick={() => clear()}
                data-testid="sign-out"
                className="flex items-center gap-1.5 text-xs font-medium px-3 py-1.5 rounded-lg border border-gray-700 hover:bg-gray-800 text-gray-300 transition"
              >
                <LogOut className="w-3.5 h-3.5" />
                Sign out
              </button>
            </div>
          ) : (
            <button
              onClick={() => login()}
              data-testid="sign-in"
              className="flex items-center gap-1.5 text-xs font-medium px-4 py-2 rounded-lg bg-cyan-600 hover:bg-cyan-500 text-white shadow-lg shadow-cyan-600/20 transition"
            >
              <LogIn className="w-3.5 h-3.5" />
              Sign in
            </button>
          )}
        </div>
      </div>
    </header>
  )
}
