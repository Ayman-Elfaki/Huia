import { NextRequest, NextResponse } from 'next/server'
import { getAccessToken } from 'next-huia-headless/server'
import { huiaHeadlessConfig } from '@/app/auth.config'

export async function DELETE(
  _req: NextRequest,
  { params }: { params: Promise<{ productId: string }> },
) {
  const token = await getAccessToken(huiaHeadlessConfig)
  if (!token) {
    return NextResponse.json({ error: 'unauthorized' }, { status: 401 })
  }

  const { productId } = await params
  try {
    const res = await fetch(`${huiaHeadlessConfig.baseUrl}/cart/items/${productId}`, {
      method: 'DELETE',
      headers: {
        authorization: `Bearer ${token}`,
      },
    })
    const data = await res.json()
    return NextResponse.json(data, { status: res.status })
  }
  catch (err) {
    return NextResponse.json({ error: 'cart_delete_failed' }, { status: 502 })
  }
}
