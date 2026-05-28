/**
 * Helpers for normalizing and de-duplicating provider ids in the Settings and
 * Workspace-setup flows. Kept generic over the provider summary shape so both
 * the wizard and SettingsView can share the same implementation.
 */

export function slugProviderId(value: string): string {
  return value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9_-]+/g, '-')
    .replace(/^-+|-+$/g, '')
}

export function uniqueProviderId(
  baseId: string,
  providers: ReadonlyArray<{ id: string }>
): string {
  const base = slugProviderId(baseId)
  const safeBase = base || 'provider'
  const used = new Set(providers.map((provider) => provider.id.trim().toLowerCase()))
  if (!used.has(safeBase)) return safeBase
  for (let index = 2; index < 1000; index++) {
    const candidate = `${safeBase}-${index}`
    if (!used.has(candidate)) return candidate
  }
  return `${safeBase}-${Date.now()}`
}
