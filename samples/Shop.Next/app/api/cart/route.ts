import { NextResponse } from 'next/server'
import { getAccessToken } from 'next-huia-headless/server'
import { huiaHeadlessConfig } from '@/app/auth.config'

export async function GET() {
  const token = await getAccessToken(huiaHeadlessConfig)
  if (!token) {
    return NextResponse.json({ error: 'unauthorized' }, { status: 401 })
  }

  try {
    const res = await fetch(`${huiaHeadlessConfig.baseUrl}/cart`, {
      headers: { authorization: `Bearer ${token}` },
    })
    const data = await res.json()
    return NextResponse.json(data, { status: res.status })
  }
  catch (err) {
    return NextResponse.json({ error: 'cart_unavailable' }, { status: 502 })
  }
}
