import { NextRequest, NextResponse } from 'next/server'
import { getAccessToken } from 'next-huia-oidc/server'
import { huiaConfig } from '@/app/auth.config'

const todoApiUrl = process.env.TODO_API_URL ?? process.env.NUXT_PUBLIC_TODO_API_URL ?? 'http://localhost:5330'

export async function PUT(
  req: NextRequest,
  { params }: { params: Promise<{ id: string }> },
) {
  const token = await getAccessToken(huiaConfig)
  if (!token) {
    return NextResponse.json({ error: 'unauthorized' }, { status: 401 })
  }

  const { id } = await params
  try {
    const body = await req.json()
    const res = await fetch(`${todoApiUrl}/todos/${id}`, {
      method: 'PUT',
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
    return NextResponse.json({ error: 'failed_to_update_todo' }, { status: 502 })
  }
}

export async function DELETE(
  _req: NextRequest,
  { params }: { params: Promise<{ id: string }> },
) {
  const token = await getAccessToken(huiaConfig)
  if (!token) {
    return NextResponse.json({ error: 'unauthorized' }, { status: 401 })
  }

  const { id } = await params
  try {
    const res = await fetch(`${todoApiUrl}/todos/${id}`, {
      method: 'DELETE',
      headers: {
        authorization: `Bearer ${token}`,
      },
    })
    return new NextResponse(null, { status: res.status })
  }
  catch (err) {
    return NextResponse.json({ error: 'failed_to_delete_todo' }, { status: 502 })
  }
}
