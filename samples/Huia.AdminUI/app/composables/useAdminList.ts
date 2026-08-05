import type { PagedResult } from '~~/shared/types/admin'

/**
 * Fetches a paged admin resource through server/api/admin/[...path].ts. Every list endpoint under
 * MapHuiaAdminEndpoints returns the same PagedResult<T> shape (see src/Huia/Common/PagedResult.cs), so this
 * one composable covers all six resource pages.
 */
export function useAdminList<T>(path: string, query: Ref<Record<string, string | number | undefined>>) {
  const items = ref<T[]>([]) as Ref<T[]>
  const totalCount = ref(0)
  const loading = ref(false)
  const error = ref<string | null>(null)

  async function refresh() {
    loading.value = true
    error.value = null
    try {
      const result = await $fetch<PagedResult<T>>(`/api/admin/${path}`, { query: query.value })
      items.value = result.items
      totalCount.value = result.totalCount
    } catch (err) {
      error.value = adminErrorMessage(err)
    } finally {
      loading.value = false
    }
  }

  return { items, totalCount, loading, error, refresh }
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
