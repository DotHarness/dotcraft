export const FEATURE_GUIDE_IDS = ['creator', 'subagentModels', 'findOutWhy'] as const

export type FeatureGuideId = (typeof FEATURE_GUIDE_IDS)[number]

export type FeatureGuideMoment = 'entry' | 'readiness' | 'friction'

export const FEATURE_GUIDE_MOMENTS: Record<FeatureGuideId, FeatureGuideMoment> = {
  creator: 'entry',
  subagentModels: 'readiness',
  findOutWhy: 'friction'
}

export interface FeatureGuideRecord {
  shown: number
  lastClosedAt?: number
  settled?: boolean
}

export interface FeatureGuidesSettings {
  enabled: boolean
  initializedAt?: number
  lastShownAt?: number
  guides: Partial<Record<FeatureGuideId, FeatureGuideRecord>>
  subagentChats: string[]
}

export const SUBAGENT_MODELS_MIN_CHATS = 2

const MAX_SHOWINGS = 2
const RETURN_AFTER_MS = 7 * 24 * 60 * 60 * 1000

export function normalizeFeatureGuides(value: unknown): FeatureGuidesSettings {
  const raw = value != null && typeof value === 'object' ? value as Record<string, unknown> : {}
  const guides: FeatureGuidesSettings['guides'] = {}
  const rawGuides = raw.guides != null && typeof raw.guides === 'object' ? raw.guides as Record<string, unknown> : {}
  for (const id of FEATURE_GUIDE_IDS) {
    const entry = rawGuides[id]
    if (entry == null || typeof entry !== 'object') continue
    const record = entry as Record<string, unknown>
    guides[id] = {
      shown: typeof record.shown === 'number' && record.shown > 0 ? Math.floor(record.shown) : 0,
      ...(typeof record.lastClosedAt === 'number' ? { lastClosedAt: record.lastClosedAt } : {}),
      ...(record.settled === true ? { settled: true } : {})
    }
  }
  return {
    enabled: raw.enabled !== false,
    ...(typeof raw.initializedAt === 'number' ? { initializedAt: raw.initializedAt } : {}),
    ...(typeof raw.lastShownAt === 'number' ? { lastShownAt: raw.lastShownAt } : {}),
    guides,
    subagentChats: Array.isArray(raw.subagentChats)
      ? [...new Set(raw.subagentChats.filter((id): id is string => typeof id === 'string' && id.length > 0))].slice(0, SUBAGENT_MODELS_MIN_CHATS)
      : []
  }
}

function sameLocalDay(a: number, b: number): boolean {
  const left = new Date(a)
  const right = new Date(b)
  return left.getFullYear() === right.getFullYear()
    && left.getMonth() === right.getMonth()
    && left.getDate() === right.getDate()
}

export function canShowFeatureGuide(
  state: FeatureGuidesSettings,
  id: FeatureGuideId,
  now: number,
  initializedThisSession: boolean
): boolean {
  if (!state.enabled) return false
  const record = state.guides[id]
  if (record?.settled) return false
  const shown = record?.shown ?? 0
  if (shown >= MAX_SHOWINGS) return false
  if (shown > 0 && (record?.lastClosedAt == null || now - record.lastClosedAt < RETURN_AFTER_MS)) return false
  if (FEATURE_GUIDE_MOMENTS[id] === 'entry') return true
  if (initializedThisSession) return false
  return state.lastShownAt == null || !sameLocalDay(state.lastShownAt, now)
}

export function recordFeatureGuideShown(state: FeatureGuidesSettings, id: FeatureGuideId, now: number): FeatureGuidesSettings {
  const record = state.guides[id] ?? { shown: 0 }
  return {
    ...state,
    lastShownAt: FEATURE_GUIDE_MOMENTS[id] === 'entry' ? state.lastShownAt : now,
    guides: { ...state.guides, [id]: { ...record, shown: record.shown + 1 } }
  }
}

export function recordFeatureGuideClosed(state: FeatureGuidesSettings, id: FeatureGuideId, now: number): FeatureGuidesSettings {
  const record = state.guides[id] ?? { shown: 0 }
  return { ...state, guides: { ...state.guides, [id]: { ...record, lastClosedAt: now } } }
}

export function settleFeatureGuide(state: FeatureGuidesSettings, id: FeatureGuideId): FeatureGuidesSettings {
  const record = state.guides[id] ?? { shown: 0 }
  if (record.settled) return state
  return { ...state, guides: { ...state.guides, [id]: { ...record, settled: true } } }
}

export function recordSubagentChat(state: FeatureGuidesSettings, threadId: string): FeatureGuidesSettings {
  if (state.subagentChats.includes(threadId) || state.subagentChats.length >= SUBAGENT_MODELS_MIN_CHATS) return state
  return { ...state, subagentChats: [...state.subagentChats, threadId] }
}
