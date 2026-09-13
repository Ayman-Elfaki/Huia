export default defineEventHandler(async (event) => {
  const { user } = await requireUserSession(event)
  return { id: user.sub, email: user.email ?? null, roles: user.roles ?? [] }
})
