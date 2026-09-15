'use client'

import React, { useState, useEffect } from 'react'
import Link from 'next/link'
import { useUserSession } from 'next-huia-headless/client'
import { ShoppingCart, Check, Loader2 } from 'lucide-react'

interface Product {
  id: string
  name: string
  price: number
}

export default function ShopHomePage() {
  const { loggedIn } = useUserSession()
  const [products, setProducts] = useState<Product[]>([])
  const [loading, setLoading] = useState(true)
  const [adding, setAdding] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)

  useEffect(() => {
    fetch('/api/products')
      .then(res => res.json())
      .then(data => {
        if (Array.isArray(data)) setProducts(data)
      })
      .catch(err => console.error('Failed to load products', err))
      .finally(() => setLoading(false))
  }, [])

  const addToCart = async (productId: string) => {
    setAdding(productId)
    setMessage(null)
    try {
      const res = await fetch('/api/cart/items', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ productId, quantity: 1 }),
      })
      if (res.ok) {
        setMessage('Added to cart.')
      }
      else {
        setMessage('Could not add to cart.')
      }
    }
    catch {
      setMessage('Could not add to cart.')
    }
    finally {
      setAdding(null)
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold tracking-tight text-white">Products</h1>
        <p className="text-xs text-gray-400 mt-1">Official merchandise and items</p>
      </div>

      {message && (
        <div className="p-3 rounded-xl bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 text-sm flex items-center gap-2">
          <Check className="w-4 h-4" />
          <span>{message}</span>
        </div>
      )}

      {loading ? (
        <div className="flex justify-center py-20 text-gray-400">
          <Loader2 className="w-8 h-8 animate-spin text-emerald-500" />
        </div>
      ) : (
        <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
          {products.map(product => (
            <div
              key={product.id}
              className="bg-gray-900/60 border border-gray-800/80 rounded-2xl p-6 flex flex-col justify-between hover:border-gray-700/80 transition group shadow-lg"
            >
              <div>
                <h3 className="font-semibold text-lg text-white group-hover:text-emerald-400 transition">
                  {product.name}
                </h3>
                <p className="text-3xl font-extrabold text-white mt-4">
                  ${product.price.toFixed(2)}
                </p>
              </div>

              <div className="mt-6">
                {loggedIn ? (
                  <button
                    onClick={() => addToCart(product.id)}
                    disabled={adding === product.id}
                    className="w-full flex items-center justify-center gap-2 py-2.5 px-4 rounded-xl bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 text-white font-medium text-sm transition shadow-lg shadow-emerald-600/20"
                  >
                    {adding === product.id ? (
                      <Loader2 className="w-4 h-4 animate-spin" />
                    ) : (
                      <ShoppingCart className="w-4 h-4" />
                    )}
                    Add to cart
                  </button>
                ) : (
                  <Link
                    href="/login"
                    className="w-full flex items-center justify-center py-2.5 px-4 rounded-xl bg-gray-800 hover:bg-gray-700 text-gray-200 font-medium text-sm transition border border-gray-700"
                  >
                    Sign in to buy
                  </Link>
                )}
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
