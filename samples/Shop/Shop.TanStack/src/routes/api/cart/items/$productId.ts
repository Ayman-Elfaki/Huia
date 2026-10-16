import { createFileRoute } from '@tanstack/react-router'
import { getAccessToken } from '@huia/tanstack-start-headless/server'
import { huiaHeadlessConfig } from '@/auth.config'

export const Route = createFileRoute('/api/cart/items/$productId')({
  server: {
    handlers: {
      DELETE: async ({ request, params }) => {
        const token = await getAccessToken(huiaHeadlessConfig, request)
        if (!token) {
          return Response.json({ error: 'unauthorized' }, { status: 401 })
        }

        const { productId } = params
        try {
          const res = await fetch(`${huiaHeadlessConfig.baseUrl}/cart/items/${productId}`, {
            method: 'DELETE',
            headers: {
              authorization: `Bearer ${token}`,
            },
          })
          const data = await res.json()
          return Response.json(data, { status: res.status })
        } catch {
          return Response.json({ error: 'cart_delete_failed' }, { status: 502 })
        }
      },
    },
  },
})
