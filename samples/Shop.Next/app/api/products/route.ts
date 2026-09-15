import { NextResponse } from 'next/server'
import { huiaHeadlessConfig } from '@/app/auth.config'

export async function GET() {
  try {
    const res = await fetch(`${huiaHeadlessConfig.baseUrl}/products`)
    if (!res.ok) {
      return NextResponse.json({ error: 'failed_to_fetch_products' }, { status: res.status })
    }
    const data = await res.json()
    return NextResponse.json(data, { status: 200 })
  }
  catch (err) {
    return NextResponse.json({ error: 'products_service_unavailable' }, { status: 502 })
  }
}
