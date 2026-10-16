import { createFileRoute } from '@tanstack/react-router'
import { getAccessToken } from '@huia/tanstack-start-headless/server'
import { huiaHeadlessConfig } from '@/auth.config'

export const Route = createFileRoute('/api/cart/')({
  server: {
    handlers: {
      GET: async ({ request }) => {
        const token = await getAccessToken(huiaHeadlessConfig, request)
        if (!token) {
          return Response.json({ error: 'unauthorized' }, { status: 401 })
        }

        try {
          const res = await fetch(`${huiaHeadlessConfig.baseUrl}/cart`, {
            headers: { authorization: `Bearer ${token}` },
          })
          const data = await res.json()
          return Response.json(data, { status: res.status })
        } catch {
          return Response.json({ error: 'cart_unavailable' }, { status: 502 })
        }
      },
    },
  },
})
