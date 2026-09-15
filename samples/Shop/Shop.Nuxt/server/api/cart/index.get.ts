export default defineEventHandler(event => shopApiFetch(event, '/cart/', { requireAuth: true }))
