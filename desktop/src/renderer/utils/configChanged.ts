export interface ConfigChangedPayload {
  source: string
  regions: string[]
  changedAt: string
}

export const CONFIG_CHANGED_DEDUPE_WINDOW_MS = 1000

export function hasConfigKeyPathChange(regions: readonly string[], ...keyPaths: string[]): boolean {
  return regions.some((region) =>
    keyPaths.some((keyPath) => region === keyPath || region.startsWith(`${keyPath}.`)))
}

export function normalizeConfigChangedPayload(
  payload: { method: string; params: unknown },
  getNow: () => string = () => new Date().toISOString()
): ConfigChangedPayload | null {
  if (payload.method !== 'config/changed') return null

  const raw = (payload.params ?? {}) as Partial<ConfigChangedPayload>
  const source = typeof raw.source === 'string' ? raw.source : ''
  const regions = Array.isArray(raw.regions) ? raw.regions.filter((region): region is string => typeof region === 'string') : []

  if (regions.length === 0) return null

  return {
    source,
    regions,
    changedAt: typeof raw.changedAt === 'string' ? raw.changedAt : getNow()
  }
}

export function filterConfigChangedRegions(
  event: ConfigChangedPayload,
  dedupeBySourceRegion: Map<string, number>
): ConfigChangedPayload | null {
  const changedAtMs = Date.parse(event.changedAt)
  const regions = event.regions.filter((region) => {
    const dedupeKey = `${event.source}:${region}`
    const previous = dedupeBySourceRegion.get(dedupeKey)

    if (
      previous != null &&
      Number.isFinite(changedAtMs) &&
      changedAtMs - previous <= CONFIG_CHANGED_DEDUPE_WINDOW_MS
    ) {
      return false
    }

    if (Number.isFinite(changedAtMs)) {
      dedupeBySourceRegion.set(dedupeKey, changedAtMs)
    }

    return true
  })

  if (regions.length === 0) return null
  return { ...event, regions }
}

export function resolveConfigChangedPayload(
  payload: { method: string; params: unknown },
  dedupeBySourceRegion: Map<string, number>,
  getNow?: () => string
): ConfigChangedPayload | null {
  const event = normalizeConfigChangedPayload(payload, getNow)
  if (!event) return null
  return filterConfigChangedRegions(event, dedupeBySourceRegion)
}
