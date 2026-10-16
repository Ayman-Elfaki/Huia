import { createFileRoute } from '@tanstack/react-router'
import { createHuiaOidcHandler } from '@huia/tanstack-start-oidc/server'
import { huiaConfig } from '../../../auth.config'

const handler = createHuiaOidcHandler(huiaConfig)

export const Route = createFileRoute('/api/auth/$')({
  server: {
    handlers: {
      GET: async ({ request }) => handler(request),
      POST: async ({ request }) => handler(request),
    },
  },
})
