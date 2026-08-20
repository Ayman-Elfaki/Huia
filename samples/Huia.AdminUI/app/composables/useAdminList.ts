import type { KeysetPage } from '~~/shared/types/admin'

/**
 * Fetches a keyset-paged admin resource through nuxt-api-party's huiaAdmin endpoint (see nuxt.config.ts and
 * server/plugins/apiPartyAuth.ts). Every list endpoint under MapHuiaAdminEndpoints returns the same
 * KeysetPage<T> shape when Huia is configured with an EF Core store (see shared/types/admin.ts) - there's no
 * cursor token in the response; the next/previous page is requested by sending the id of the last/first item
 * already on screen as the `after`/`before` query param (MR.AspNetCore.Pagination's own convention), so this
 * composable requires T to carry an `id`.
 *
 * `filters` carries only non-pagination query params (e.g. `search`, `subject`) — this composable owns
 * `after`/`before`/`pageSize` itself. Callers that change a filter should call `reset()` (not `refresh()`) so
 * pagination starts over from the first page under the new filter.
 */
export function useAdminList<T extends { id: string }>(
  path: string,
  filters: Ref<Record<string, string | undefined>>,
  pageSize = 25
) {
  const items = ref<T[]>([]) as Ref<T[]>
  const hasPrevious = ref(false)
  const hasNext = ref(false)
  const loading = ref(false)
  const error = ref<string | null>(null)

  // The direction used to reach the page currently on screen - replayed as-is by refresh().
  let currentDirection: { after?: string, before?: string } = {}

  async function fetchPage(direction: { after?: string, before?: string }) {
    loading.value = true
    error.value = null
    try {
      const result = await $huiaAdmin<KeysetPage<T>>(path, {
        query: { ...filters.value, ...direction, pageSize }
      })
      items.value = result.data
      hasPrevious.value = result.hasPrevious
      hasNext.value = result.hasNext
      currentDirection = direction
    } catch (err) {
      error.value = adminErrorMessage(err)
    } finally {
      loading.value = false
    }
  }

  /** Re-fetches the current page as-is (e.g. after a create/edit/delete, or the refresh button). */
  function refresh() {
    return fetchPage(currentDirection)
  }

  function goNext() {
    if (!hasNext.value || !items.value.length) return Promise.resolve()
    return fetchPage({ after: items.value[items.value.length - 1]!.id })
  }

  function goPrevious() {
    if (!hasPrevious.value || !items.value.length) return Promise.resolve()
    return fetchPage({ before: items.value[0]!.id })
  }

  /** Starts over from the first page - call after a filter changes. */
  function reset() {
    return fetchPage({})
  }

  return { items, hasPrevious, hasNext, loading, error, refresh, goNext, goPrevious, reset }
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
