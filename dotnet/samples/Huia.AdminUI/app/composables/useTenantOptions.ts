// Tenant list for the `<Select>` filters and the scope-create form. Mirrors `TenantDto` from
// `src/Huia.AspNetCore/Endpoints/AdminEndpoints.cs`.

export interface TenantRow {
  tenantId: string
  displayName: string
  passwordEnabled: boolean
  phoneLoginEnabled: boolean
  externalLoginEnabled: boolean
  clientCount: number
}

export function useTenants() {
  return useHuiaData<TenantRow[]>('admin/tenants', { default: () => [] })
}

export function useTenantOptions() {
  const { data } = useTenants()
  return computed(() =>
    (data.value ?? []).map(t => ({ value: t.tenantId, label: `${t.displayName} (${t.tenantId})` })),
  )
}
