export default defineEventHandler(event => shopApiFetch(event, '/checkout', { method: 'POST', requireAuth: true }))
