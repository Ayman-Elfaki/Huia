import { defineEventHandler, setResponseHeader } from 'h3'
import { getUserSession } from '../../utils/session'

export default defineEventHandler(async (event) => {
  setResponseHeader(event, 'Cache-Control', 'no-store')
  return getUserSession(event) // { user?, loggedIn?, expiresAt? } — never a token
})
