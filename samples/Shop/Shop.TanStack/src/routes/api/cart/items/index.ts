import { createFileRoute } from '@tanstack/react-router'
import { getAccessToken } from '@huia/tanstack-start-headless/server'
import { huiaHeadlessConfig } from '@/auth.config'

export const Route = createFileRoute('/api/cart/items/')({
  server: {
    handlers: {
      POST: async ({ request }) => {
        const token = await getAccessToken(huiaHeadlessConfig, request)
        if (!token) {
          return Response.json({ error: 'unauthorized' }, { status: 401 })
        }

        try {
          const body = await request.json()
          const res = await fetch(`${huiaHeadlessConfig.baseUrl}/cart/items`, {
            method: 'POST',
            headers: {
              'content-type': 'application/json',
              authorization: `Bearer ${token}`,
            },
            body: JSON.stringify(body),
          })
          const data = await res.json()
          return Response.json(data, { status: res.status })
        } catch {
          return Response.json({ error: 'cart_action_failed' }, { status: 502 })
        }
      },
    },
  },
})
