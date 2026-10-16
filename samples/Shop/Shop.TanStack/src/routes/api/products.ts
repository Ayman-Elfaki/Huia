import { createFileRoute } from '@tanstack/react-router'
import { huiaHeadlessConfig } from '@/auth.config'

export const Route = createFileRoute('/api/products')({
  server: {
    handlers: {
      GET: async () => {
        try {
          const res = await fetch(`${huiaHeadlessConfig.baseUrl}/products`)
          if (!res.ok) {
            return Response.json({ error: 'failed_to_fetch_products' }, { status: res.status })
          }
          const data = await res.json()
          return Response.json(data, { status: 200 })
        } catch {
          return Response.json({ error: 'products_service_unavailable' }, { status: 502 })
        }
      },
    },
  },
})
