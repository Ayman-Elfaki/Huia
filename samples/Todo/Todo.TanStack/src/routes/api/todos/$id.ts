import { createFileRoute } from '@tanstack/react-router'
import { getAccessToken } from '@huia/tanstack-start-oidc/server'
import { huiaConfig } from '../../../auth.config'

const todoApiUrl = process.env.TODO_API_URL ?? process.env.NUXT_PUBLIC_TODO_API_URL ?? 'http://localhost:5330'

export const Route = createFileRoute('/api/todos/$id')({
  server: {
    handlers: {
      PUT: async ({ request, params }) => {
        const token = await getAccessToken(huiaConfig, request)
        if (!token) {
          return Response.json({ error: 'unauthorized' }, { status: 401 })
        }

        const { id } = params
        try {
          const body = await request.json()
          const res = await fetch(`${todoApiUrl}/todos/${id}`, {
            method: 'PUT',
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
          return Response.json({ error: 'failed_to_update_todo' }, { status: 502 })
        }
      },
      DELETE: async ({ request, params }) => {
        const token = await getAccessToken(huiaConfig, request)
        if (!token) {
          return Response.json({ error: 'unauthorized' }, { status: 401 })
        }

        const { id } = params
        try {
          const res = await fetch(`${todoApiUrl}/todos/${id}`, {
            method: 'DELETE',
            headers: {
              authorization: `Bearer ${token}`,
            },
          })
          return new Response(null, { status: res.status })
        }
        catch {
          return Response.json({ error: 'failed_to_delete_todo' }, { status: 502 })
        }
      },
    },
  },
})
