import { defineNitroPlugin } from 'nitropack/runtime'
import { warmDiscovery } from '../utils/oidc'

export default defineNitroPlugin(() => {
  // Kick OIDC discovery off at boot. Never blocks: route handlers await getOidcConfig() which
  // retries on demand if the OP was briefly unreachable here.
  warmDiscovery()
})
