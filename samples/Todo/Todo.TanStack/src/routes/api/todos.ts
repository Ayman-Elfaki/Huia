import { createFileRoute } from '@tanstack/react-router'
import { getAccessToken } from '@huia/tanstack-start-oidc/server'
import { huiaConfig } from '../../auth.config'

const todoApiUrl = process.env.TODO_API_URL ?? process.env.NUXT_PUBLIC_TODO_API_URL ?? 'http://localhost:5330'

export const Route = createFileRoute('/api/todos')({
  server: {
    handlers: {
      GET: async ({ request }) => {
        const token = await getAccessToken(huiaConfig, request)
        if (!token) {
          return Response.json({ error: 'unauthorized' }, { status: 401 })
        }

        try {
          const res = await fetch(`${todoApiUrl}/todos`, {
            headers: { authorization: `Bearer ${token}` },
          })
          if (!res.ok) {
            return Response.json({ error: 'api_error' }, { status: res.status })
          }
          const data = await res.json()
          return Response.json(data, { status: 200 })
        }
        catch {
          return Response.json({ error: 'failed_to_fetch_todos' }, { status: 502 })
        }
      },
      POST: async ({ request }) => {
        const token = await getAccessToken(huiaConfig, request)
        if (!token) {
          return Response.json({ error: 'unauthorized' }, { status: 401 })
        }

        try {
          const body = await request.json()
          const res = await fetch(`${todoApiUrl}/todos`, {
            method: 'POST',
            headers: {
              'content-type': 'application/json',
              authorization: `Bearer ${token}`,
            },
            body: JSON.stringify(body),
          })
          const data = await res.json()
          return Response.json(data, { status: res.status })
        }
        catch {
          return Response.json({ error: 'failed_to_create_todo' }, { status: 502 })
        }
      },
    },
  },
})
