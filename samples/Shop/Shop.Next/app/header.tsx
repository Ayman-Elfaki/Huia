'use client'

import React from 'react'
import Link from 'next/link'
import { useUserSession } from 'next-huia-headless/client'
import { ShoppingBag, ShoppingCart, LogIn, LogOut, User as UserIcon } from 'lucide-react'

export function Header() {
  const { user, loggedIn, clear } = useUserSession()

  return (
    <header className="border-b border-gray-800/80 bg-gray-900/60 backdrop-blur sticky top-0 z-50">
      <div className="max-w-5xl mx-auto px-6 h-16 flex items-center justify-between">
        <Link href="/" className="flex items-center gap-2.5 font-bold text-lg tracking-tight text-white hover:opacity-90 transition">
          <div className="w-8 h-8 rounded-lg bg-gradient-to-tr from-emerald-600 to-teal-500 flex items-center justify-center shadow-lg shadow-emerald-500/20">
            <ShoppingBag className="w-5 h-5 text-white" />
          </div>
          <span>Huia Shop <span className="text-xs px-2 py-0.5 rounded-full bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 font-medium ml-1">Next.js</span></span>
        </Link>

        <div className="flex items-center gap-4">
          <Link
            href="/cart"
            className="flex items-center gap-1.5 text-xs font-medium px-3 py-1.5 rounded-lg border border-gray-800 hover:bg-gray-800 text-gray-300 transition"
          >
            <ShoppingCart className="w-4 h-4" />
            Cart
          </Link>

          {loggedIn ? (
            <div className="flex items-center gap-3">
              <div className="flex items-center gap-2 text-sm text-gray-300">
                <div className="w-7 h-7 rounded-full bg-gray-800 border border-gray-700 flex items-center justify-center text-gray-400">
                  <UserIcon className="w-4 h-4" />
                </div>
                <span className="font-medium text-xs">
                  {user?.firstName ? `${user.firstName} ${user.lastName ?? ''}` : (user?.email || user?.sub)}
                </span>
              </div>
              <button
                onClick={() => clear()}
                className="flex items-center gap-1.5 text-xs font-medium px-3 py-1.5 rounded-lg border border-gray-700 hover:bg-gray-800 text-gray-300 transition"
              >
                <LogOut className="w-3.5 h-3.5" />
                Sign out
              </button>
            </div>
          ) : (
            <Link
              href="/login"
              className="flex items-center gap-1.5 text-xs font-medium px-4 py-2 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-white shadow-lg shadow-emerald-600/20 transition"
            >
              <LogIn className="w-3.5 h-3.5" />
              Sign in
            </Link>
          )}
        </div>
      </div>
    </header>
  )
}
