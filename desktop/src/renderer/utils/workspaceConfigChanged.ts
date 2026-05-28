export interface WorkspaceConfigChangedPayload {
  source: string
  regions: string[]
  changedAt: string
}

export const WORKSPACE_CONFIG_CHANGED_DEDUPE_WINDOW_MS = 1000
export const WORKSPACE_DEFAULT_APPROVAL_POLICY_REGION = 'workspace.defaultApprovalPolicy'

export function normalizeWorkspaceConfigChangedPayload(
  payload: { method: string; params: unknown },
  getNow: () => string = () => new Date().toISOString()
): WorkspaceConfigChangedPayload | null {
  if (payload.method !== 'workspace/configChanged') return null

  const raw = (payload.params ?? {}) as Partial<WorkspaceConfigChangedPayload>
  const source = typeof raw.source === 'string' ? raw.source : ''
  const regions = Array.isArray(raw.regions) ? raw.regions.filter((region): region is string => typeof region === 'string') : []

  if (regions.length === 0) return null

  return {
    source,
    regions,
    changedAt: typeof raw.changedAt === 'string' ? raw.changedAt : getNow()
  }
}

export function filterWorkspaceConfigChangedRegions(
  event: WorkspaceConfigChangedPayload,
  dedupeBySourceRegion: Map<string, number>
): WorkspaceConfigChangedPayload | null {
  const changedAtMs = Date.parse(event.changedAt)
  const regions = event.regions.filter((region) => {
    const dedupeKey = `${event.source}:${region}`
    const previous = dedupeBySourceRegion.get(dedupeKey)

    if (
      previous != null &&
      Number.isFinite(changedAtMs) &&
      changedAtMs - previous <= WORKSPACE_CONFIG_CHANGED_DEDUPE_WINDOW_MS
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

export function resolveWorkspaceConfigChangedPayload(
  payload: { method: string; params: unknown },
  dedupeBySourceRegion: Map<string, number>,
  getNow?: () => string
): WorkspaceConfigChangedPayload | null {
  const event = normalizeWorkspaceConfigChangedPayload(payload, getNow)
  if (!event) return null
  return filterWorkspaceConfigChangedRegions(event, dedupeBySourceRegion)
}
