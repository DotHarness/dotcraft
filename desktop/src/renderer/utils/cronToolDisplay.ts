/**
 * Human-readable labels for the agent `Cron` tool — aligned with
 * DotCraft.Core.Cron.CronToolDisplays (server).
 */

import { translate, type AppLocale, type MessageKey } from '../../shared/locales'

export const CRON_TOOL_NAME = 'Cron'

function tr(
  locale: AppLocale,
  key: MessageKey | string,
  vars?: Record<string, string | number>
): string {
  return translate(locale, key, vars)
}

function formatDuration(seconds: number): string {
  const s = Math.floor(seconds)
  if (s < 60) return `${s}s`
  if (s < 3600) return `${Math.floor(s / 60)}m`
  if (s < 86400) return `${Math.floor(s / 3600)}h`
  return `${Math.floor(s / 86400)}d`
}

function getString(args: Record<string, unknown> | undefined, key: string): string | undefined {
  if (!args) return undefined
  const v = args[key]
  if (v === undefined || v === null) return undefined
  return String(v)
}

function tryGetLong(value: unknown): number | null {
  if (value == null) return null
  if (typeof value === 'number' && Number.isFinite(value)) return Math.trunc(value)
  if (typeof value === 'string' && value.trim() !== '') {
    const n = Number(value)
    return Number.isFinite(n) ? Math.trunc(n) : null
  }
  return null
}

function formatAddCollapsed(args: Record<string, unknown> | undefined, locale: AppLocale): string {
  const name = getString(args, 'name')
  const message = getString(args, 'message') ?? tr(locale, 'cron.add.taskDefault')
  const label = name ?? (message.length > 40 ? `${message.slice(0, 40)}…` : message)

  const dailyTime = getString(args, 'dailyTime')
  const dailyHour = tryGetLong(args?.dailyHour)
  if (dailyTime != null && dailyTime !== '') {
    const tz = getString(args, 'timeZone') ?? 'UTC'
    return tr(locale, 'cron.add.scheduleDailyTime', { label, dailyTime, tz })
  }
  if (dailyHour != null) {
    const dm = tryGetLong(args?.dailyMinute) ?? 0
    const tz = getString(args, 'timeZone') ?? 'UTC'
    const hh = String(dailyHour).padStart(2, '0')
    const mm = String(dm).padStart(2, '0')
    return tr(locale, 'cron.add.scheduleDailyHour', { label, hh, mm, tz })
  }

  const everySec = tryGetLong(args?.everySeconds)
  const delaySec = tryGetLong(args?.delaySeconds)
  if (everySec != null && everySec > 0) {
    if (delaySec != null && delaySec > 0) {
      return tr(locale, 'cron.add.scheduleEveryIn', {
        label,
        delay: formatDuration(delaySec),
        every: formatDuration(everySec)
      })
    }
    return tr(locale, 'cron.add.scheduleEvery', { label, every: formatDuration(everySec) })
  }
  if (delaySec != null && delaySec > 0) {
    return tr(locale, 'cron.add.scheduleIn', { label, delay: formatDuration(delaySec) })
  }
  return tr(locale, 'cron.add.scheduleGeneric', { label })
}

/** Collapsed summary for a completed Cron call (matches CronToolDisplays.Cron). */
export function formatCronCollapsedLabel(
  args: Record<string, unknown> | undefined,
  locale: AppLocale
): string {
  const action = (getString(args, 'action') ?? '?').trim().toLowerCase()
  switch (action) {
    case 'add':
      return formatAddCollapsed(args, locale)
    case 'list':
      return tr(locale, 'cron.tool.listJobs')
    case 'remove':
      return tr(locale, 'cron.tool.removeCollapsed', { id: getString(args, 'jobId') ?? '?' })
    default:
      return tr(locale, 'cron.add.cronUnknown', { action })
  }
}

/** In-progress line while the Cron tool is running. */
export function formatCronRunningLabel(
  args: Record<string, unknown> | undefined,
  locale: AppLocale
): string {
  const action = (getString(args, 'action') ?? '?').trim().toLowerCase()
  switch (action) {
    case 'add':
      return tr(locale, 'cron.tool.scheduling')
    case 'list':
      return tr(locale, 'cron.tool.listing')
    case 'remove': {
      const jid = getString(args, 'jobId')
      return jid
        ? tr(locale, 'cron.tool.removing', { id: jid })
        : tr(locale, 'cron.tool.removingGeneric')
    }
    default:
      return tr(locale, 'cron.run.cronUnknown', { action })
  }
}

function getJsonStr(obj: Record<string, unknown>, ...keys: string[]): string | undefined {
  for (const k of keys) {
    const v = obj[k]
    if (v === undefined || v === null) continue
    if (typeof v === 'string') return v
    if (typeof v === 'number' || typeof v === 'boolean') return String(v)
  }
  return undefined
}

function getJsonNumber(obj: Record<string, unknown>, ...keys: string[]): number | undefined {
  for (const k of keys) {
    const v = obj[k]
    if (typeof v === 'number' && Number.isFinite(v)) return v
    if (typeof v === 'string' && v.trim() !== '') {
      const n = Number(v)
      if (Number.isFinite(n)) return n
    }
  }
  return undefined
}

/**
 * Parses Cron tool JSON result into display lines (matches CronToolDisplays.CronResult).
 * Returns null when the payload is not a recognized Cron result (caller may fall back to raw text).
 */
export function formatCronResultLines(result: string | undefined, locale: AppLocale): string[] | null {
  if (result == null || result.trim() === '') return null
  let root: unknown
  try {
    root = JSON.parse(result) as unknown
  } catch {
    return null
  }
  if (root === null || typeof root !== 'object' || Array.isArray(root)) return null
  const o = root as Record<string, unknown>

  const err = getJsonStr(o, 'error')
  if (err !== undefined) {
    return [tr(locale, 'cron.result.errorPrefix', { error: err })]
  }

  const status = getJsonStr(o, 'status')
  if (status === 'created') {
    const id = getJsonStr(o, 'id', 'Id')
    const jobName = getJsonStr(o, 'name', 'Name')
    const nextRunMs = getJsonNumber(o, 'nextRun', 'NextRun')
    let timeLabel = '—'
    if (nextRunMs != null) {
      const d = new Date(nextRunMs)
      timeLabel = d.toLocaleTimeString(undefined, {
        hour: '2-digit',
        minute: '2-digit',
        hour12: false
      })
    }
    const nameDisplay = jobName ?? id ?? tr(locale, 'cron.result.jobDefault')
    return [tr(locale, 'cron.result.createdLine', { name: nameDisplay, time: timeLabel })]
  }
  if (status === 'removed') {
    const jobId = getJsonStr(o, 'jobId', 'JobId')
    return [tr(locale, 'cron.result.removed', { jobId: jobId ?? '' })]
  }
  if (status === 'not_found') {
    const jobId = getJsonStr(o, 'jobId', 'JobId')
    return [tr(locale, 'cron.result.notFound', { jobId: jobId ?? '' })]
  }

  const count = getJsonNumber(o, 'count', 'Count')
  if (count != null) {
    const c = Math.floor(count)
    const plural = locale === 'zh-Hans' ? '' : c === 1 ? '' : 's'
    return c === 0
      ? [tr(locale, 'cron.tool.noJobs')]
      : [tr(locale, 'cron.tool.jobCount', { count: c, plural })]
  }

  return null
}

/**
 * Structured view of a successful `Cron(action: "add")` result, as returned by
 * DotCraft.Core.Cron.CronTools (enriched payload). Returns null when the result
 * is absent, malformed, or not a `status: "created"` payload.
 */
export interface CronCreatedDisplay {
  jobId: string | undefined
  jobName: string | undefined
  nextRunAtMs: number | undefined
  schedulePhrase: string
  scheduleKind: string | undefined
  message: string | undefined
  deleteAfterRun: boolean | undefined
  channel: string | undefined
  toUser: string | undefined
}

export function parseCronCreatedResult(
  result: string | undefined,
  locale: AppLocale
): CronCreatedDisplay | null {
  if (result == null || result.trim() === '') return null
  let root: unknown
  try {
    root = JSON.parse(result) as unknown
  } catch {
    return null
  }
  if (root === null || typeof root !== 'object' || Array.isArray(root)) return null
  const o = root as Record<string, unknown>

  const status = getJsonStr(o, 'status')
  if (status !== 'created') return null

  const jobId = getJsonStr(o, 'id', 'Id')
  const jobName = getJsonStr(o, 'name', 'Name')
  const nextRunAtMs = getJsonNumber(o, 'nextRun', 'NextRun')
  const deleteAfterRun =
    typeof o['deleteAfterRun'] === 'boolean' ? (o['deleteAfterRun'] as boolean) : undefined
  const message = getJsonStr(o, 'message')
  const channel = getJsonStr(o, 'channel')
  const toUser = getJsonStr(o, 'toUser')

  const scheduleRaw = o['schedule']
  const schedule =
    scheduleRaw !== null && typeof scheduleRaw === 'object' && !Array.isArray(scheduleRaw)
      ? (scheduleRaw as Record<string, unknown>)
      : undefined
  const scheduleKind = schedule ? getJsonStr(schedule, 'kind') : undefined

  const schedulePhrase = formatSchedulePhrase(schedule, locale)

  return {
    jobId,
    jobName,
    nextRunAtMs,
    schedulePhrase,
    scheduleKind,
    message,
    deleteAfterRun,
    channel,
    toUser
  }
}

function formatSchedulePhrase(
  schedule: Record<string, unknown> | undefined,
  locale: AppLocale
): string {
  if (!schedule) return tr(locale, 'cron.schedule.unknown')
  const kind = (getJsonStr(schedule, 'kind') ?? '').toLowerCase()

  if (kind === 'every') {
    const everyMs = getJsonNumber(schedule, 'everyMs')
    const initialDelayMs = getJsonNumber(schedule, 'initialDelayMs')
    const every = everyMs != null ? formatDuration(Math.max(0, Math.floor(everyMs / 1000))) : '?'
    if (initialDelayMs != null && initialDelayMs > 0) {
      const delay = formatDuration(Math.max(0, Math.floor(initialDelayMs / 1000)))
      return tr(locale, 'cron.schedule.everyIn', { delay, every })
    }
    return tr(locale, 'cron.schedule.every', { every })
  }

  if (kind === 'daily') {
    const h = getJsonNumber(schedule, 'dailyHour') ?? 0
    const m = getJsonNumber(schedule, 'dailyMinute') ?? 0
    const tz = getJsonStr(schedule, 'tz') ?? 'UTC'
    const hh = String(Math.max(0, Math.floor(h))).padStart(2, '0')
    const mm = String(Math.max(0, Math.floor(m))).padStart(2, '0')
    return tr(locale, 'cron.schedule.dailyAt', { time: `${hh}:${mm}`, tz })
  }

  if (kind === 'at') {
    const atMs = getJsonNumber(schedule, 'atMs')
    if (atMs != null && Number.isFinite(atMs)) {
      const diffSec = Math.floor((atMs - Date.now()) / 1000)
      if (diffSec > 0) {
        return tr(locale, 'cron.schedule.onceIn', { delay: formatDuration(diffSec) })
      }
      const d = new Date(atMs)
      const time = d.toLocaleString(undefined, {
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
        hour12: false
      })
      return tr(locale, 'cron.schedule.onceAt', { time })
    }
    return tr(locale, 'cron.schedule.unknown')
  }

  return tr(locale, 'cron.schedule.unknown')
}

export function hasCronCreatedDisplayData(
  result: string | undefined,
  locale: AppLocale
): boolean {
  return parseCronCreatedResult(result, locale) != null
}
