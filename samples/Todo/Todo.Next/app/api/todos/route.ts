import { NextRequest, NextResponse } from 'next/server'
import { getAccessToken } from 'next-huia-oidc/server'
import { huiaConfig } from '@/app/auth.config'

const todoApiUrl = process.env.TODO_API_URL ?? process.env.NUXT_PUBLIC_TODO_API_URL ?? 'http://localhost:5330'

export async function GET() {
  const token = await getAccessToken(huiaConfig)
  if (!token) {
    return NextResponse.json({ error: 'unauthorized' }, { status: 401 })
  }

  try {
    const res = await fetch(`${todoApiUrl}/todos`, {
      headers: { authorization: `Bearer ${token}` },
    })
    if (!res.ok) {
      return NextResponse.json({ error: 'api_error' }, { status: res.status })
    }
    const data = await res.json()
    return NextResponse.json(data, { status: 200 })
  }
  catch (err) {
    return NextResponse.json({ error: 'failed_to_fetch_todos' }, { status: 502 })
  }
}

export async function POST(req: NextRequest) {
  const token = await getAccessToken(huiaConfig)
  if (!token) {
    return NextResponse.json({ error: 'unauthorized' }, { status: 401 })
  }

  try {
    const body = await req.json()
    const res = await fetch(`${todoApiUrl}/todos`, {
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
    return NextResponse.json({ error: 'failed_to_create_todo' }, { status: 502 })
  }
}
