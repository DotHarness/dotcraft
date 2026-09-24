import type { MessageKey } from '../../../../../shared/locales'

export type DreamsRunStatus = 'running' | 'succeeded' | 'skipped' | 'failed' | 'canceled'
export type DreamsReviewStatus = 'pending' | 'applied' | 'discarded' | 'archived'

export interface DreamsRunState {
  id: string
  status: DreamsRunStatus
  startedAt: string
  endedAt?: string | null
  processedThreadCount: number
  candidateThreadCount: number
  dreamWritten: boolean
  topicFilesWritten: number
  topicFilesDeleted: number
  evidenceSearchCount: number
  evidenceReadCount: number
  outputStoreId?: string | null
  reviewStatus?: DreamsReviewStatus | null
  autoApplied: boolean
  errorType?: string | null
  evidenceThreadIds: string[]
  writtenPaths: string[]
  threadId?: string | null
  turnId?: string | null
  turnIds: string[]
  trigger?: string | null
  message?: string | null
  inputManifestPath?: string | null
}

export interface DreamsStatus {
  enabled: boolean
  interval: string
  threadLookbackCount: number
  autoApply: boolean
  minCompletedTurnsSinceLastRun: number
  nextRunAt?: string | null
  running: boolean
  activeDreamStoreId?: string | null
  lastRun: DreamsRunState | null
}

export const DEFAULT_DREAMS_INTERVAL = '24:00:00'
export const DEFAULT_DREAMS_THREAD_LOOKBACK_COUNT = 20
export const DREAMS_INTERVAL_OPTIONS = ['06:00:00', '12:00:00', '24:00:00', '168:00:00'] as const
export const DREAMS_THREAD_LOOKBACK_OPTIONS = [10, 20, 50, 100] as const

export function delay(ms: number): Promise<void> {
  return new Promise((resolve) => window.setTimeout(resolve, ms))
}

export function formatDreamsIntervalOption(value: string, t: (key: MessageKey | string, vars?: Record<string, string | number>) => string): string {
  switch (value) {
    case '06:00:00':
      return t('settings.personalization.dreamsInterval.6h')
    case '12:00:00':
      return t('settings.personalization.dreamsInterval.12h')
    case '24:00:00':
      return t('settings.personalization.dreamsInterval.24h')
    case '168:00:00':
      return t('settings.personalization.dreamsInterval.7d')
    default:
      return value
  }
}

export function normalizeDreamsInterval(value: unknown): string | null {
  if (typeof value !== 'string') return null
  const trimmed = value.trim()
  if (trimmed === '') return null
  const dayMatch = /^(\d+)\.(\d{1,2}):(\d{2}):(\d{2})$/.exec(trimmed)
  if (dayMatch) {
    const days = Number(dayMatch[1])
    const hours = Number(dayMatch[2])
    const minutes = Number(dayMatch[3])
    const seconds = Number(dayMatch[4])
    if (Number.isFinite(days) && Number.isFinite(hours) && minutes < 60 && seconds < 60) {
      return `${String(days * 24 + hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`
    }
  }
  return trimmed
}

function normalizeDreamsRunStatus(value: unknown): DreamsRunStatus {
  return value === 'running' || value === 'succeeded' || value === 'skipped' || value === 'failed' || value === 'canceled'
    ? value
    : 'skipped'
}

function normalizeDreamsReviewStatus(value: unknown): DreamsReviewStatus | null {
  return value === 'pending' || value === 'applied' || value === 'discarded' || value === 'archived'
    ? value
    : null
}

function asStringArray(value: unknown): string[] {
  return Array.isArray(value)
    ? value.filter((item): item is string => typeof item === 'string')
    : []
}

function normalizeDreamsRunState(value: unknown): DreamsRunState | null {
  if (value == null || typeof value !== 'object') return null
  const source = value as Partial<DreamsRunState>
  return {
    id: typeof source.id === 'string' ? source.id : '',
    status: normalizeDreamsRunStatus(source.status),
    startedAt: typeof source.startedAt === 'string' ? source.startedAt : '',
    endedAt: typeof source.endedAt === 'string' ? source.endedAt : null,
    processedThreadCount:
      typeof source.processedThreadCount === 'number' && Number.isFinite(source.processedThreadCount)
        ? source.processedThreadCount
        : 0,
    candidateThreadCount:
      typeof source.candidateThreadCount === 'number' && Number.isFinite(source.candidateThreadCount)
        ? source.candidateThreadCount
        : 0,
    dreamWritten: source.dreamWritten === true,
    topicFilesWritten:
      typeof source.topicFilesWritten === 'number' && Number.isFinite(source.topicFilesWritten)
        ? source.topicFilesWritten
        : 0,
    topicFilesDeleted:
      typeof source.topicFilesDeleted === 'number' && Number.isFinite(source.topicFilesDeleted)
        ? source.topicFilesDeleted
        : 0,
    evidenceSearchCount:
      typeof source.evidenceSearchCount === 'number' && Number.isFinite(source.evidenceSearchCount)
        ? source.evidenceSearchCount
        : 0,
    evidenceReadCount:
      typeof source.evidenceReadCount === 'number' && Number.isFinite(source.evidenceReadCount)
        ? source.evidenceReadCount
        : 0,
    outputStoreId: typeof source.outputStoreId === 'string' ? source.outputStoreId : null,
    reviewStatus: normalizeDreamsReviewStatus(source.reviewStatus),
    autoApplied: source.autoApplied === true,
    errorType: typeof source.errorType === 'string' ? source.errorType : null,
    evidenceThreadIds: asStringArray(source.evidenceThreadIds),
    writtenPaths: asStringArray(source.writtenPaths),
    threadId: typeof source.threadId === 'string' ? source.threadId : null,
    turnId: typeof source.turnId === 'string' ? source.turnId : null,
    turnIds: asStringArray(source.turnIds),
    trigger: typeof source.trigger === 'string' ? source.trigger : null,
    message: typeof source.message === 'string' ? source.message : null,
    inputManifestPath: typeof source.inputManifestPath === 'string' ? source.inputManifestPath : null
  }
}

export function normalizeDreamsRunList(value: unknown): DreamsRunState[] {
  const source = value != null && typeof value === 'object' ? value as { runs?: unknown } : {}
  return Array.isArray(source.runs)
    ? source.runs.map(normalizeDreamsRunState).filter((run): run is DreamsRunState => run != null)
    : []
}

export function normalizeDreamsStatus(value: unknown): DreamsStatus {
  const source = value != null && typeof value === 'object' ? value as Partial<DreamsStatus> : {}
  const lastRun = normalizeDreamsRunState(source.lastRun)
  return {
    enabled: source.enabled !== false,
    interval: normalizeDreamsInterval(source.interval) ?? DEFAULT_DREAMS_INTERVAL,
    threadLookbackCount:
      typeof source.threadLookbackCount === 'number' && Number.isInteger(source.threadLookbackCount) && source.threadLookbackCount > 0
        ? source.threadLookbackCount
        : DEFAULT_DREAMS_THREAD_LOOKBACK_COUNT,
    autoApply: source.autoApply === true,
    minCompletedTurnsSinceLastRun:
      typeof source.minCompletedTurnsSinceLastRun === 'number' && Number.isFinite(source.minCompletedTurnsSinceLastRun)
        ? source.minCompletedTurnsSinceLastRun
        : 0,
    nextRunAt: typeof source.nextRunAt === 'string' ? source.nextRunAt : null,
    running: source.running === true,
    activeDreamStoreId: typeof source.activeDreamStoreId === 'string' ? source.activeDreamStoreId : null,
    lastRun
  }
}
