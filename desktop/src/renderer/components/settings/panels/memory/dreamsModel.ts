import type { MessageKey } from '../../../../../shared/locales'
import type { StatusIndicatorTone } from '../../../ui/StatusIndicator'

export type DreamsRunStatus = 'running' | 'succeeded' | 'skipped' | 'failed' | 'canceled'
export type DreamsReviewStatus = 'pending' | 'applied' | 'discarded' | 'archived'

type Translate = (key: MessageKey | string, vars?: Record<string, string | number>) => string

export interface DreamsRunChanges {
  added: string[]
  updated: string[]
  deleted: string[]
  indexChanged: boolean
}

export interface DreamsRunState {
  id: string
  scope: string | null
  status: DreamsRunStatus
  trigger: string | null
  startedAt: string
  endedAt: string | null
  candidateThreadCount: number
  evidenceThreadIds: string[]
  evidenceSearchCount: number
  evidenceReadCount: number
  changes: DreamsRunChanges | null
  reviewStatus: DreamsReviewStatus | null
  autoApplied: boolean
  summary: string | null
  errorType: string | null
  message: string | null
  threadId: string | null
}

export interface DreamsStatus {
  enabled: boolean
  interval: string | null
  autoApply: boolean
  minSessionsSinceLastRun: number
  nextRunAt: string | null
  running: boolean
  lastRun: DreamsRunState | null
}

export const DREAMS_INTERVAL_OPTIONS = ['06:00:00', '12:00:00', '24:00:00', '168:00:00'] as const

export function delay(ms: number): Promise<void> {
  return new Promise((resolve) => window.setTimeout(resolve, ms))
}

export function formatDreamsIntervalOption(value: string, t: Translate): string {
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

export function formatDreamsIntervalForConfig(value: string): string {
  const match = /^(\d+):(\d{2}):(\d{2})$/.exec(value)
  if (!match) return value
  const totalHours = Number(match[1])
  const time = `${String(totalHours % 24).padStart(2, '0')}:${match[2]}:${match[3]}`
  return totalHours >= 24 ? `${Math.floor(totalHours / 24)}.${time}` : time
}

export function dreamsRunStateLabel(run: DreamsRunState): { tone: StatusIndicatorTone; labelKey: MessageKey } {
  switch (run.status) {
    case 'running':
      return { tone: 'pending', labelKey: 'settings.personalization.dreamsStatus.running' }
    case 'failed':
      return { tone: 'error', labelKey: 'settings.personalization.dreamsStatus.failed' }
    case 'skipped':
      return { tone: 'neutral', labelKey: 'settings.personalization.dreamsStatus.skipped' }
    case 'canceled':
      return { tone: 'neutral', labelKey: 'settings.personalization.dreamsStatus.canceled' }
    case 'succeeded':
      switch (run.reviewStatus) {
        case 'pending':
          return { tone: 'warning', labelKey: 'settings.dreams.reviewStatus.pending' }
        case 'applied':
          return { tone: 'success', labelKey: 'settings.dreams.reviewStatus.applied' }
        case 'discarded':
          return { tone: 'neutral', labelKey: 'settings.dreams.reviewStatus.discarded' }
        case 'archived':
          return { tone: 'neutral', labelKey: 'settings.dreams.reviewStatus.archived' }
        default:
          return { tone: 'success', labelKey: 'settings.personalization.dreamsStatus.succeeded' }
      }
  }
}

export function dreamsStoreLabel(run: DreamsRunState, t: Translate): string {
  return run.scope ? t('settings.dreams.store.agent') : t('settings.dreams.store.workspace')
}

export function dreamsChangeSummary(run: DreamsRunState, t: Translate): string[] {
  if (run.status !== 'succeeded') return []
  const changes = run.changes
  const parts = [
    changes?.added.length ? t('settings.dreams.changes.added', { count: changes.added.length }) : null,
    changes?.updated.length ? t('settings.dreams.changes.updated', { count: changes.updated.length }) : null,
    changes?.deleted.length ? t('settings.dreams.changes.deleted', { count: changes.deleted.length }) : null
  ].filter((part): part is string => part != null)
  if (parts.length > 0) return parts
  return [changes?.indexChanged ? t('settings.dreams.changes.index') : t('settings.dreams.changes.none')]
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

function asString(value: unknown): string | null {
  return typeof value === 'string' && value !== '' ? value : null
}

function asCount(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : 0
}

function normalizeDreamsRunChanges(value: unknown): DreamsRunChanges | null {
  if (value == null || typeof value !== 'object') return null
  const source = value as Record<string, unknown>
  return {
    added: asStringArray(source.added),
    updated: asStringArray(source.updated),
    deleted: asStringArray(source.deleted),
    indexChanged: source.indexChanged === true
  }
}

function normalizeDreamsRunState(value: unknown): DreamsRunState | null {
  if (value == null || typeof value !== 'object') return null
  const source = value as Record<string, unknown>
  return {
    id: asString(source.id) ?? '',
    scope: asString(source.scope),
    status: normalizeDreamsRunStatus(source.status),
    trigger: asString(source.trigger),
    startedAt: asString(source.startedAt) ?? '',
    endedAt: asString(source.endedAt),
    candidateThreadCount: asCount(source.candidateThreadCount),
    evidenceThreadIds: asStringArray(source.evidenceThreadIds),
    evidenceSearchCount: asCount(source.evidenceSearchCount),
    evidenceReadCount: asCount(source.evidenceReadCount),
    changes: normalizeDreamsRunChanges(source.changes),
    reviewStatus: normalizeDreamsReviewStatus(source.reviewStatus),
    autoApplied: source.autoApplied === true,
    summary: asString(source.summary),
    errorType: asString(source.errorType),
    message: asString(source.message),
    threadId: asString(source.threadId)
  }
}

export function normalizeDreamsRunList(value: unknown): DreamsRunState[] {
  const source = value != null && typeof value === 'object' ? value as { runs?: unknown } : {}
  return Array.isArray(source.runs)
    ? source.runs.map(normalizeDreamsRunState).filter((run): run is DreamsRunState => run != null)
    : []
}

export function normalizeDreamsStatus(value: unknown): DreamsStatus {
  const source = value != null && typeof value === 'object' ? value as Record<string, unknown> : {}
  return {
    enabled: source.enabled === true,
    interval: normalizeDreamsInterval(source.interval),
    autoApply: source.autoApply === true,
    minSessionsSinceLastRun: asCount(source.minSessionsSinceLastRun),
    nextRunAt: asString(source.nextRunAt),
    running: source.running === true,
    lastRun: normalizeDreamsRunState(source.lastRun)
  }
}
