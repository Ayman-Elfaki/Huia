import { readBody } from 'h3'

export default defineEventHandler(async (event) => {
  const body = await readBody<{ productId: string, quantity: number }>(event)
  return shopApiFetch(event, '/cart/items', { method: 'POST', body, requireAuth: true })
})
