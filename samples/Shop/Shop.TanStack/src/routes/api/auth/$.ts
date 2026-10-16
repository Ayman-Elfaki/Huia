import { createFileRoute } from '@tanstack/react-router'
import { createHuiaHeadlessHandler } from '@huia/tanstack-start-headless/server'
import { huiaHeadlessConfig } from '@/auth.config'

const handler = createHuiaHeadlessHandler(huiaHeadlessConfig)

export const Route = createFileRoute('/api/auth/$')({
  server: {
    handlers: {
      GET: ({ request }) => handler(request),
      POST: ({ request }) => handler(request),
      PUT: ({ request }) => handler(request),
      PATCH: ({ request }) => handler(request),
      DELETE: ({ request }) => handler(request),
    },
  },
})
