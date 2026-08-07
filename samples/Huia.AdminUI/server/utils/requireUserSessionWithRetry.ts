import { requireUserSession } from 'nuxt-oidc-auth/runtime/server/utils/session.js'
import type { H3Event } from 'h3'

/**
 * Right after the OIDC callback redirect, nuxt-oidc-auth's session cookie can be unrecognized by
 * requireUserSession for a short window (confirmed by tests/Huia.Tests.E2E/AdminUiE2ETests.cs's own
 * GotoWithRetryAsync workaround, which rides out the exact same race at the browser level) — a handful of
 * short retries here avoids surfacing that transient 401 to the client as a real "unauthorized" error.
 */
export async function requireUserSessionWithRetry(event: H3Event, attempts = 3, delayMs = 150) {
  for (let attempt = 1; attempt <= attempts; attempt++) {
    try {
      return await requireUserSession(event)
    } catch (error) {
      if (attempt === attempts) {
        throw error
      }
      await new Promise(resolve => setTimeout(resolve, delayMs))
    }
  }

  // Unreachable — the loop above always either returns or throws on its last attempt.
  throw new Error('requireUserSessionWithRetry: exhausted retries without a result.')
}
