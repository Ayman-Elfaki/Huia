import { defineEventHandler } from 'h3'
import { getUserSession } from '../utils/session'

export default defineEventHandler(async (event) => {
  return await getUserSession(event)
})
