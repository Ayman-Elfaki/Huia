'use client'

import React, { useState, useEffect, useCallback } from 'react'
import { useUserSession } from 'next-huia-oidc/client'
import { Plus, Trash2, CheckCircle2, Circle, ShieldCheck, ArrowRight, Loader2 } from 'lucide-react'

interface TodoItem {
  id: number
  title: string
  done: boolean
  createdAt: string
}

export default function HomePage() {
  const { user, loggedIn, login, loading } = useUserSession()
  const [todos, setTodos] = useState<TodoItem[]>([])
  const [newTitle, setNewTitle] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [fetching, setFetching] = useState(false)

  const loadTodos = useCallback(async () => {
    if (!loggedIn) return
    try {
      setFetching(true)
      const res = await fetch('/api/todos')
      if (res.ok) {
        const data = await res.json()
        setTodos(data)
      }
    }
    catch (err) {
      console.error('Failed to load todos', err)
    }
    finally {
      setFetching(false)
    }
  }, [loggedIn])

  useEffect(() => {
    if (loggedIn) {
      void loadTodos()
    }
  }, [loggedIn, loadTodos])

  const addTodo = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!newTitle.trim() || submitting) return

    try {
      setSubmitting(true)
      const res = await fetch('/api/todos', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ title: newTitle.trim() }),
      })
      if (res.ok) {
        const item = await res.json()
        setTodos(prev => [item, ...prev])
        setNewTitle('')
      }
    }
    finally {
      setSubmitting(false)
    }
  }

  const toggleTodo = async (todo: TodoItem) => {
    try {
      const res = await fetch(`/api/todos/${todo.id}`, {
        method: 'PUT',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ title: todo.title, done: !todo.done }),
      })
      if (res.ok) {
        const updated = await res.json()
        setTodos(prev => prev.map(t => (t.id === todo.id ? updated : t)))
      }
    }
    catch (err) {
      console.error('Failed to toggle todo', err)
    }
  }

  const deleteTodo = async (id: number) => {
    try {
      const res = await fetch(`/api/todos/${id}`, { method: 'DELETE' })
      if (res.ok || res.status === 204) {
        setTodos(prev => prev.filter(t => t.id !== id))
      }
    }
    catch (err) {
      console.error('Failed to delete todo', err)
    }
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center py-20 text-gray-400">
        <Loader2 className="w-8 h-8 animate-spin text-blue-500" />
      </div>
    )
  }

  if (!loggedIn) {
    return (
      <div className="py-12 flex flex-col items-center text-center">
        <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-blue-500/10 border border-blue-500/20 text-blue-400 text-xs font-semibold mb-6">
          <ShieldCheck className="w-4 h-4" />
          Enterprise Multi-Tenant Identity Provider
        </div>
        <h1 className="text-4xl md:text-5xl font-extrabold tracking-tight text-white max-w-2xl leading-tight">
          Modern Task Management with <span className="text-transparent bg-clip-text bg-gradient-to-r from-blue-400 to-indigo-400">Huia OpenID</span>
        </h1>
        <p className="mt-4 text-base text-gray-400 max-w-xl">
          Powered by <code className="text-blue-400 font-mono text-sm">next-huia-oidc</code> and ASP.NET Core OpenIddict. Tokens stay secure on the server; the browser never sees raw credentials.
        </p>

        <div className="mt-8 flex gap-4">
          <button
            onClick={() => login()}
            data-testid="landing-sign-in"
            className="flex items-center gap-2 px-6 py-3 rounded-xl bg-blue-600 hover:bg-blue-500 text-white font-medium text-sm shadow-xl shadow-blue-600/30 transition transform hover:-translate-y-0.5"
          >
            Sign in to your account
            <ArrowRight className="w-4 h-4" />
          </button>
        </div>
      </div>
    )
  }

  return (
    <div className="space-y-6 max-w-2xl mx-auto">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-white">Your Tasks</h1>
          <p className="text-xs text-gray-400 mt-1">
            Authenticated as <span className="text-gray-200 font-medium">{user?.email || user?.name}</span>
          </p>
        </div>
      </div>

      {/* New Todo Form */}
      <form onSubmit={addTodo} className="flex gap-2">
        <input
          type="text"
          value={newTitle}
          onChange={e => setNewTitle(e.target.value)}
          placeholder="What needs to be done?"
          className="flex-1 px-4 py-2.5 rounded-xl bg-gray-900 border border-gray-800 focus:border-blue-500 focus:outline-none text-sm text-gray-100 placeholder-gray-500 transition"
        />
        <button
          type="submit"
          disabled={submitting || !newTitle.trim()}
          className="px-4 py-2.5 rounded-xl bg-blue-600 hover:bg-blue-500 disabled:opacity-50 text-white text-sm font-medium flex items-center gap-1.5 transition shadow-lg shadow-blue-600/20"
        >
          {submitting ? <Loader2 className="w-4 h-4 animate-spin" /> : <Plus className="w-4 h-4" />}
          Add
        </button>
      </form>

      {/* Todo List */}
      <div className="bg-gray-900/60 border border-gray-800/80 rounded-2xl overflow-hidden shadow-xl">
        {fetching ? (
          <div className="p-8 flex justify-center text-gray-400">
            <Loader2 className="w-6 h-6 animate-spin text-blue-500" />
          </div>
        ) : todos.length === 0 ? (
          <div className="p-12 text-center text-gray-500 text-sm">
            No tasks yet. Create one above to get started!
          </div>
        ) : (
          <ul className="divide-y divide-gray-800/60">
            {todos.map(todo => (
              <li
                key={todo.id}
                className="p-4 flex items-center justify-between gap-3 hover:bg-gray-800/30 transition group"
              >
                <button
                  onClick={() => toggleTodo(todo)}
                  className="flex items-center gap-3 text-left flex-1"
                >
                  {todo.done ? (
                    <CheckCircle2 className="w-5 h-5 text-emerald-400 shrink-0" />
                  ) : (
                    <Circle className="w-5 h-5 text-gray-500 shrink-0 group-hover:text-gray-400" />
                  )}
                  <span className={`text-sm ${todo.done ? 'line-through text-gray-500' : 'text-gray-200'}`}>
                    {todo.title}
                  </span>
                </button>
                <button
                  onClick={() => deleteTodo(todo.id)}
                  className="p-1.5 text-gray-500 hover:text-red-400 rounded-lg hover:bg-gray-800 transition opacity-0 group-hover:opacity-100"
                  aria-label="Delete todo"
                >
                  <Trash2 className="w-4 h-4" />
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  )
}
