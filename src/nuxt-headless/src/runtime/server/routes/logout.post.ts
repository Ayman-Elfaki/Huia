import { defineEventHandler } from 'h3'
import { useHeadlessConfig } from '../utils/config'
import { readSessionCookie } from '../utils/cookie'
import { getTokenRecord } from '../utils/storage'
import { clearUserSession } from '../utils/session'

export default defineEventHandler(async (event) => {
  const cfg = useHeadlessConfig(event)
  const cookie = await readSessionCookie(event, cfg)

  if (cookie?.sid) {
    const record = await getTokenRecord(cfg, cookie.sid)
    if (record) {
      try {
        const url = `${cfg.huia.baseUrl}/${cfg.huia.tenant}/identity/logout`
        await fetch(url, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${record.accessToken}`,
          },
          body: JSON.stringify({ refreshToken: record.refreshToken }),
        })
      }
      catch {
        // Continue clearing local session even if remote logout fails
      }
    }
  }

  await clearUserSession(event)
  return { ok: true }
})
