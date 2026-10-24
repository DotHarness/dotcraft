export type SidebarDestinationPins = Record<string, boolean>

const MAX_SIDEBAR_DESTINATION_PINS = 100

export function normalizeSidebarDestinationPins(value: unknown): SidebarDestinationPins | undefined {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return undefined
  const pins: SidebarDestinationPins = {}
  let count = 0
  for (const [rawId, pinned] of Object.entries(value)) {
    if (typeof pinned !== 'boolean') continue
    const id = rawId.trim()
    if (!id || /[\u0000-\u001f]/.test(id)) continue
    pins[id] = pinned
    if (++count >= MAX_SIDEBAR_DESTINATION_PINS) break
  }
  return count > 0 ? pins : undefined
}
