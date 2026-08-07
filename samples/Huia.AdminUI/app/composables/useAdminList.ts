import type { PagedResult } from '~~/shared/types/admin'

/**
 * Fetches a cursor-paged admin resource through nuxt-api-party's huiaAdmin endpoint (see nuxt.config.ts and
 * server/plugins/apiPartyAuth.ts). Every list endpoint under MapHuiaAdminEndpoints returns the same
 * PagedResult<T> shape (see src/Huia/Common/PagedResult.cs — an opaque nextCursor token, not a page number
 * or total count), so this one composable covers every paginated resource page.
 *
 * `filters` carries only non-pagination query params (e.g. `search`, `subject`) — this composable owns
 * `cursor`/`pageSize` itself. Callers that change a filter should call `reset()` (not `refresh()`) so
 * pagination starts over from the first page under the new filter.
 */
export function useAdminList<T>(
  path: string,
  filters: Ref<Record<string, string | undefined>>,
  pageSize = 25
) {
  const items = ref<T[]>([]) as Ref<T[]>
  const nextCursor = ref<string | null>(null)
  const currentCursor = ref<string | null>(null)
  // Cursors used to reach the current page, oldest first — popped to go back a page.
  const cursorHistory = ref<(string | null)[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  const hasPrevious = computed(() => cursorHistory.value.length > 0)

  async function fetchPage(cursor: string | null) {
    loading.value = true
    error.value = null
    try {
      const result = await $huiaAdmin<PagedResult<T>>(path, {
        query: { ...filters.value, cursor: cursor ?? undefined, pageSize }
      })
      items.value = result.items
      nextCursor.value = result.nextCursor
      currentCursor.value = cursor
    } catch (err) {
      error.value = adminErrorMessage(err)
    } finally {
      loading.value = false
    }
  }

  /** Re-fetches the current page as-is (e.g. after a create/edit/delete, or the refresh button). */
  function refresh() {
    return fetchPage(currentCursor.value)
  }

  function goNext() {
    if (!nextCursor.value) return Promise.resolve()
    cursorHistory.value.push(currentCursor.value)
    return fetchPage(nextCursor.value)
  }

  function goPrevious() {
    if (!hasPrevious.value) return Promise.resolve()
    return fetchPage(cursorHistory.value.pop() ?? null)
  }

  /** Starts over from the first page - call after a filter changes. */
  function reset() {
    cursorHistory.value = []
    return fetchPage(null)
  }

  return { items, nextCursor, hasPrevious, loading, error, refresh, goNext, goPrevious, reset }
}

/** Unwraps a ProblemDetails/ValidationProblem body (or a plain string) from a failed $fetch call. */
export function adminErrorMessage(err: unknown): string {
  const data = (err as { data?: unknown })?.data
  if (data && typeof data === 'object') {
    const problem = data as { detail?: string, title?: string, errors?: Record<string, string[]> }
    if (problem.errors) {
      return Object.values(problem.errors).flat().join(' ')
    }
    if (problem.detail || problem.title) {
      return problem.detail || problem.title || 'Request failed.'
    }
  }
  if (typeof data === 'string' && data) return data
  return err instanceof Error ? err.message : 'Request failed.'
}
