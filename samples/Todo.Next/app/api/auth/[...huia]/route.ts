import { createHuiaOidcHandler } from 'next-huia-oidc/server'
import { huiaConfig } from '@/app/auth.config'

const handler = createHuiaOidcHandler(huiaConfig)

export const GET = handler
export const POST = handler
