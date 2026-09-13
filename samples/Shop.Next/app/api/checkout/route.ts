import { NextResponse } from 'next/server'
import { getAccessToken } from 'next-huia-headless/server'
import { huiaHeadlessConfig } from '@/app/auth.config'

export async function POST() {
  const token = await getAccessToken(huiaHeadlessConfig)
  if (!token) {
    return NextResponse.json({ error: 'unauthorized' }, { status: 401 })
  }

  try {
    const res = await fetch(`${huiaHeadlessConfig.baseUrl}/checkout`, {
      method: 'POST',
      headers: { authorization: `Bearer ${token}` },
    })
    const data = await res.json()
    return NextResponse.json(data, { status: res.status })
  }
  catch (err) {
    return NextResponse.json({ error: 'checkout_failed' }, { status: 502 })
  }
}
