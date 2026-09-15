import { NextRequest, NextResponse } from 'next/server'
import { getAccessToken } from 'next-huia-headless/server'
import { huiaHeadlessConfig } from '@/app/auth.config'

export async function POST(req: NextRequest) {
  const token = await getAccessToken(huiaHeadlessConfig)
  if (!token) {
    return NextResponse.json({ error: 'unauthorized' }, { status: 401 })
  }

  try {
    const body = await req.json()
    const res = await fetch(`${huiaHeadlessConfig.baseUrl}/cart/items`, {
      method: 'POST',
      headers: {
        'content-type': 'application/json',
        authorization: `Bearer ${token}`,
      },
      body: JSON.stringify(body),
    })
    const data = await res.json()
    return NextResponse.json(data, { status: res.status })
  }
  catch (err) {
    return NextResponse.json({ error: 'cart_action_failed' }, { status: 502 })
  }
}
