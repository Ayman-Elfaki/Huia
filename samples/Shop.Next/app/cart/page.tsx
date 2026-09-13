'use client'

import React, { useState, useEffect } from 'react'
import Link from 'next/link'
import { useRouter } from 'next/navigation'
import { useUserSession } from 'next-huia-headless/client'
import { ShoppingBag, Trash2, CheckCircle, ArrowLeft, Loader2, CreditCard } from 'lucide-react'

interface CartItem {
  productId: string
  quantity: number
}

interface Product {
  id: string
  name: string
  price: number
}

export default function CartPage() {
  const router = useRouter()
  const { loggedIn, loading: authLoading } = useUserSession()
  const [items, setItems] = useState<CartItem[]>([])
  const [products, setProducts] = useState<Record<string, Product>>({})
  const [loading, setLoading] = useState(true)
  const [checkingOut, setCheckingOut] = useState(false)
  const [orderResult, setOrderResult] = useState<{ orderId: string; total: number } | null>(null)

  useEffect(() => {
    if (!authLoading && !loggedIn) {
      router.push('/login?returnTo=/cart')
      return
    }

    if (loggedIn) {
      Promise.all([
        fetch('/api/cart').then(r => r.json()),
        fetch('/api/products').then(r => r.json()),
      ])
        .then(([cartData, productsData]) => {
          if (Array.isArray(cartData)) setItems(cartData)
          if (Array.isArray(productsData)) {
            const map: Record<string, Product> = {}
            for (const p of productsData) map[p.id] = p
            setProducts(map)
          }
        })
        .catch(err => console.error('Failed to load cart', err))
        .finally(() => setLoading(false))
    }
  }, [loggedIn, authLoading, router])

  const removeItem = async (productId: string) => {
    try {
      const res = await fetch(`/api/cart/items/${productId}`, { method: 'DELETE' })
      if (res.ok) {
        setItems(prev => prev.filter(i => i.productId !== productId))
      }
    }
    catch (err) {
      console.error('Failed to remove item', err)
    }
  }

  const handleCheckout = async () => {
    setCheckingOut(true)
    try {
      const res = await fetch('/api/checkout', { method: 'POST' })
      if (res.ok) {
        const data = await res.json()
        setOrderResult(data)
        setItems([])
      }
    }
    catch (err) {
      console.error('Checkout failed', err)
    }
    finally {
      setCheckingOut(false)
    }
  }

  const total = items.reduce((sum, item) => {
    const p = products[item.productId]
    return sum + (p ? p.price * item.quantity : 0)
  }, 0)

  if (authLoading || loading) {
    return (
      <div className="flex justify-center py-20 text-gray-400">
        <Loader2 className="w-8 h-8 animate-spin text-emerald-500" />
      </div>
    )
  }

  if (orderResult) {
    return (
      <div className="max-w-md mx-auto py-12 text-center">
        <div className="w-16 h-16 rounded-full bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 mx-auto flex items-center justify-center mb-4">
          <CheckCircle className="w-8 h-8" />
        </div>
        <h2 className="text-2xl font-bold text-white">Order Confirmed!</h2>
        <p className="text-xs text-gray-400 mt-1">Order {orderResult.orderId} placed — total ${orderResult.total.toFixed(2)}</p>
        <div className="mt-6 p-4 rounded-xl bg-gray-900 border border-gray-800 text-left text-xs space-y-2">
          <div className="flex justify-between">
            <span className="text-gray-400">Order ID:</span>
            <span className="font-mono text-gray-200">{orderResult.orderId}</span>
          </div>
          <div className="flex justify-between">
            <span className="text-gray-400">Total Charged:</span>
            <span className="font-bold text-white">${orderResult.total.toFixed(2)}</span>
          </div>
        </div>
        <Link
          href="/"
          className="mt-6 inline-flex items-center gap-2 px-5 py-2.5 rounded-xl bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-medium transition"
        >
          <ArrowLeft className="w-4 h-4" />
          Continue Shopping
        </Link>
      </div>
    )
  }

  return (
    <div className="max-w-2xl mx-auto space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold tracking-tight text-white">Your Cart</h1>
        <Link href="/" className="text-xs text-emerald-400 hover:underline flex items-center gap-1">
          <ArrowLeft className="w-3.5 h-3.5" />
          Back to Products
        </Link>
      </div>

      {items.length === 0 ? (
        <div className="p-12 text-center bg-gray-900/60 border border-gray-800/80 rounded-2xl">
          <ShoppingBag className="w-10 h-10 text-gray-600 mx-auto mb-3" />
          <p className="text-sm text-gray-400">Your cart is empty.</p>
          <Link
            href="/"
            className="mt-4 inline-block px-4 py-2 rounded-xl bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-medium transition shadow-lg shadow-emerald-600/20"
          >
            Browse Products
          </Link>
        </div>
      ) : (
        <div className="space-y-4">
          <div className="bg-gray-900/60 border border-gray-800/80 rounded-2xl overflow-hidden divide-y divide-gray-800/60 shadow-xl">
            {items.map(item => {
              const product = products[item.productId]
              return (
                <div key={item.productId} className="p-4 flex items-center justify-between gap-4">
                  <div>
                    <h4 className="font-semibold text-sm text-white">
                      {item.productId} × {item.quantity}
                    </h4>
                    <p className="text-xs text-gray-400 mt-0.5">
                      {product?.name ?? item.productId} &bull; ${product?.price.toFixed(2) ?? '0.00'} each
                    </p>
                  </div>
                  <div className="flex items-center gap-4">
                    <span className="font-bold text-sm text-white">
                      ${((product?.price ?? 0) * item.quantity).toFixed(2)}
                    </span>
                    <button
                      onClick={() => removeItem(item.productId)}
                      className="p-1.5 text-gray-500 hover:text-red-400 rounded-lg hover:bg-gray-800 transition"
                      aria-label="Remove item"
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>
                  </div>
                </div>
              )
            })}
          </div>

          <div className="bg-gray-900/80 border border-gray-800 rounded-2xl p-6 shadow-xl space-y-4">
            <div className="flex justify-between text-base font-bold text-white">
              <span>Total:</span>
              <span className="text-xl text-emerald-400">${total.toFixed(2)}</span>
            </div>

            <button
              onClick={handleCheckout}
              disabled={checkingOut}
              className="w-full py-3 px-4 rounded-xl bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 text-white font-medium text-sm transition shadow-lg shadow-emerald-600/20 flex items-center justify-center gap-2"
            >
              {checkingOut ? <Loader2 className="w-4 h-4 animate-spin" /> : <CreditCard className="w-4 h-4" />}
              Checkout
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
