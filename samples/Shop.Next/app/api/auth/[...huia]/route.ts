import { createHuiaHeadlessHandler } from 'next-huia-headless/server'
import { huiaHeadlessConfig } from '@/app/auth.config'

const handler = createHuiaHeadlessHandler(huiaHeadlessConfig)

export const GET = handler
export const POST = handler
