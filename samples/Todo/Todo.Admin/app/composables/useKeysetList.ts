// Wraps `useHuiaData` for the admin API's keyset-paginated list endpoints (`admin/users`,
// `admin/clients`, `admin/keys`). The server returns `{ data, hasNext, hasPrevious, ... }` and accepts
// `after` / `before` / `size` (+ an optional `tenant` filter on some routes). `next()` / `prev()` walk
// the cursor off the first / last row currently shown.

interface KeysetPage<T> {
  data: T[]
  hasNext: boolean
  hasPrevious: boolean
}

export interface UseKeysetListOptions {
  /** Rows per page (server clamps to 1..100). */
  pageSize?: number
  /** Row property to use as the cursor. Defaults to `id`. */
  idKey?: string
}

export function useKeysetList<T extends Record<string, unknown>>(
  path: string,
  options: UseKeysetListOptions = {},
) {
  const pageSize = options.pageSize ?? 20
  const idKey = options.idKey ?? 'id'

  const tenant = ref('')
  const after = ref<string | undefined>()
  const before = ref<string | undefined>()

  const query = computed<Record<string, string>>(() => {
    const q: Record<string, string> = { size: String(pageSize) }
    if (tenant.value)
      q.tenant = tenant.value
    if (after.value)
      q.after = after.value
    else if (before.value)
      q.before = before.value
    return q
  })

  const { data, pending, error, refresh } = useHuiaData<KeysetPage<T>>(path, {
    query,
    watch: [query],
    default: () => ({ data: [], hasNext: false, hasPrevious: false }),
  })

  const rows = computed(() => data.value?.data ?? [])
  const hasNext = computed(() => data.value?.hasNext ?? false)
  const hasPrevious = computed(() => data.value?.hasPrevious ?? false)

  function next() {
    const list = rows.value
    if (list.length === 0)
      return
    after.value = String(list[list.length - 1]![idKey])
    before.value = undefined
  }

  function prev() {
    const list = rows.value
    if (list.length === 0)
      return
    before.value = String(list[0]![idKey])
    after.value = undefined
  }

  function setTenant(value: string) {
    tenant.value = value
    after.value = undefined
    before.value = undefined
  }

  return { rows, hasNext, hasPrevious, pending, error, refresh, tenant, setTenant, next, prev }
}
