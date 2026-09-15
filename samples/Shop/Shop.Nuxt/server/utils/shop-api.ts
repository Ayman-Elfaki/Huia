import { createError, type H3Event } from 'h3'
import { useRuntimeConfig } from '#imports'

/** Proxies a request to Shop.Api, attaching the caller's bearer token when one is required. */
export async function shopApiFetch<T>(
  event: H3Event,
  path: string,
  opts: { method?: string, body?: unknown, requireAuth?: boolean } = {},
): Promise<T> {
  const { shopApiUrl } = useRuntimeConfig()
  const headers: Record<string, string> = { 'content-type': 'application/json' }

  if (opts.requireAuth) {
    const token = await getAccessToken(event)
    if (!token) {
      throw createError({ statusCode: 401, statusMessage: 'Unauthorized' })
    }
    headers.authorization = `Bearer ${token}`
  }

  const res = await fetch(`${shopApiUrl}${path}`, {
    method: opts.method ?? 'GET',
    headers,
    body: opts.body ? JSON.stringify(opts.body) : undefined,
  })

  if (!res.ok) {
    const problem = await res.json().catch(() => null)
    throw createError({ statusCode: res.status, statusMessage: 'shop_api_error', data: problem })
  }

  return res.json() as Promise<T>
}
