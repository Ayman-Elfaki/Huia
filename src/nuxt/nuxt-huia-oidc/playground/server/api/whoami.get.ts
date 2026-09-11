export default defineEventHandler(async (event) => {
  const { user } = await requireUserSession(event)
  return { id: user.sub, name: user.name ?? null, roles: user.roles ?? [] }
})
