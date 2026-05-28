import { useState } from 'react'
import { Ellipsis, Eye, Pause, Play, Trash2 } from 'lucide-react'
import { translate, type AppLocale } from '../../../shared/locales'
import type { CronJobWire } from '../../stores/cronStore'
import { useCronStore } from '../../stores/cronStore'
import { useAutomationsStore } from '../../stores/automationsStore'
import { useReviewPanelStore } from '../../stores/reviewPanelStore'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { ConfirmDialog } from '../ui/ConfirmDialog'
import { formatNextRun } from '../../utils/cronNextRunDisplay'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { ActionTooltip } from '../ui/ActionTooltip'
import { addToast } from '../../stores/toastStore'

function formatSchedule(job: CronJobWire): string {
  const s = job.schedule
  if (s.kind === 'at' && s.atMs != null) {
    return `Once at ${new Date(s.atMs).toLocaleString()}`
  }
  if (s.kind === 'daily' && s.dailyHour != null && s.dailyMinute != null) {
    const hh = String(s.dailyHour).padStart(2, '0')
    const mm = String(s.dailyMinute).padStart(2, '0')
    const tz = s.tz && s.tz.trim() !== '' ? s.tz : 'UTC'
    return `Daily at ${hh}:${mm} (${tz})`
  }
  if (s.kind === 'every' && s.everyMs != null) {
    const ms = s.everyMs
    const sec = Math.floor(ms / 1000)
    let line = ''
    if (sec < 60) line = `Every ${sec}s`
    else {
      const min = Math.floor(sec / 60)
      if (min < 60) line = `Every ${min}m`
      else {
        const h = Math.floor(min / 60)
        if (h < 48) line = `Every ${h}h`
        else {
          const d = Math.floor(h / 24)
          line = `Every ${d}d`
        }
      }
    }
    if (s.initialDelayMs != null && s.initialDelayMs > 0) {
      const dsec = Math.floor(s.initialDelayMs / 1000)
      const dlab =
        dsec < 60 ? `${dsec}s` : dsec < 3600 ? `${Math.floor(dsec / 60)}m` : dsec < 86400 ? `${Math.floor(dsec / 3600)}h` : `${Math.floor(dsec / 86400)}d`
      return `First in ${dlab}, then ${line.toLowerCase()}`
    }
    return line
  }
  return job.schedule.kind
}

function relativeLastRun(lastRunAtMs: number | undefined | null, locale: AppLocale): string {
  if (lastRunAtMs == null) return translate(locale, 'cron.display.never')
  const diff = Date.now() - lastRunAtMs
  const seconds = Math.floor(diff / 1000)
  if (seconds < 60) return translate(locale, 'cron.display.justNow')
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return translate(locale, 'cron.last.minAgo', { n: minutes })
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return translate(locale, 'cron.last.hourAgo', { n: hours })
  const days = Math.floor(hours / 24)
  return translate(locale, 'cron.last.dayAgo', { n: days })
}

export function CronJobCard({ job }: { job: CronJobWire }): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const [hovered, setHovered] = useState(false)
  const [showDelete, setShowDelete] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [runningNow, setRunningNow] = useState(false)
  const [menuPosition, setMenuPosition] = useState<ContextMenuPosition | null>(null)
  const selectCronJob = useCronStore((s) => s.selectCronJob)
  const clearTaskSelection = useAutomationsStore((s) => s.selectTask)
  const removeJob = useCronStore((s) => s.removeJob)
  const enableJob = useCronStore((s) => s.enableJob)
  const runJobNow = useCronStore((s) => s.runJobNow)
  const selectedId = useCronStore((s) => s.selectedCronJobId)

  const st = job.state
  const nextRun = formatNextRun(st.nextRunAtMs, job.enabled, locale)
  const ok = st.lastStatus === 'ok'
  const err = st.lastStatus === 'error'
  const dotColor = !job.enabled
    ? 'var(--text-tertiary)'
    : err
      ? 'var(--error)'
      : ok
        ? 'var(--success)'
        : 'var(--text-tertiary)'

  const preview =
    st.lastError && err
      ? st.lastError
      : st.lastResult
        ? st.lastResult
        : '—'

  async function handleDeleteConfirm(): Promise<void> {
    setDeleting(true)
    try {
      await removeJob(job.id)
      setShowDelete(false)
    } finally {
      setDeleting(false)
    }
  }

  async function handleRunNow(): Promise<void> {
    if (runningNow) return
    setRunningNow(true)
    try {
      await runJobNow(job.id)
      addToast(t('cron.runNowQueued'), 'success')
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      addToast(t('cron.runNowFailed', { error: msg }), 'error')
    } finally {
      setRunningNow(false)
    }
  }

  function clearTaskReviewSelection(): void {
    useReviewPanelStore.getState().destroyReviewPanel()
    clearTaskSelection(null)
  }

  return (
    <>
      <div
        role="button"
        tabIndex={0}
        onClick={() => {
          if (st.lastThreadId) {
            clearTaskReviewSelection()
            selectCronJob(job.id)
          }
        }}
        onKeyDown={(e) => {
          if (e.key === 'Enter' || e.key === ' ') {
            if (st.lastThreadId) {
              clearTaskReviewSelection()
              selectCronJob(job.id)
            }
          }
        }}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
        style={{
          display: 'flex',
          alignItems: 'flex-start',
          gap: '12px',
          padding: '10px 14px',
          borderRadius: '8px',
          backgroundColor:
            selectedId === job.id
              ? 'var(--bg-tertiary)'
              : hovered
                ? 'var(--bg-secondary)'
                : 'transparent',
          cursor: st.lastThreadId ? 'pointer' : 'default',
          transition: 'background-color 0.15s'
        }}
      >
        <div
          style={{
            width: '8px',
            height: '8px',
            borderRadius: '50%',
            marginTop: '5px',
            backgroundColor: dotColor,
            flexShrink: 0
          }}
        />
        <div style={{ flex: 1, minWidth: 0 }}>
          <div
            style={{
              fontWeight: 600,
              fontSize: '13px',
              color: 'var(--text-primary)',
              whiteSpace: 'nowrap',
              overflow: 'hidden',
              textOverflow: 'ellipsis'
            }}
          >
            {job.name}
          </div>
          <div
            style={{
              fontSize: '11px',
              color: 'var(--text-tertiary)',
              marginTop: '2px'
            }}
          >
            {formatSchedule(job)}
          </div>
          <div
            style={{
              fontSize: '11px',
              color: 'var(--text-secondary)',
              marginTop: '3px'
            }}
          >
            {t('cron.card.nextRunPrefix')} {nextRun.absolute}
            {nextRun.relative != null ? ` · ${nextRun.relative}` : ''}
            {!job.enabled && st.nextRunAtMs != null ? t('cron.card.pausedSuffix') : ''}
          </div>
          <div
            style={{
              fontSize: '12px',
              color: err ? 'var(--error)' : 'var(--text-secondary)',
              marginTop: '4px',
              whiteSpace: 'nowrap',
              overflow: 'hidden',
              textOverflow: 'ellipsis'
            }}
          >
            {t('cron.card.lastRunPrefix')} {relativeLastRun(st.lastRunAtMs, locale)} · {preview}
          </div>
        </div>
        <div
          style={{
            flexShrink: 0,
            display: 'flex',
            alignItems: 'flex-end',
            gap: '6px'
          }}
        >
          <ActionTooltip label={runningNow ? t('cron.runningNow') : t('cron.runNow')} placement="top">
            <button
              type="button"
              disabled={runningNow}
              onClick={(e) => {
                e.stopPropagation()
                void handleRunNow()
              }}
              style={iconButtonStyle(runningNow)}
              aria-label={runningNow ? t('cron.runningNow') : t('cron.runNow')}
            >
              <Play size={14} aria-hidden fill="currentColor" />
            </button>
          </ActionTooltip>
          <ActionTooltip label={t('cron.moreActions')} placement="top">
            <button
              type="button"
              onClick={(e) => {
                e.stopPropagation()
                const rect = e.currentTarget.getBoundingClientRect()
                setMenuPosition({ x: rect.left, y: rect.bottom + 6 })
              }}
              style={iconButtonStyle(false)}
              aria-label={t('cron.moreActions')}
            >
              <Ellipsis size={16} aria-hidden />
            </button>
          </ActionTooltip>
        </div>
      </div>

      {menuPosition && (
        <ContextMenu
          position={menuPosition}
          onClose={() => setMenuPosition(null)}
          items={[
            {
              label: t('cron.card.view'),
              icon: <Eye size={14} />,
              disabled: !st.lastThreadId,
              onClick: () => {
                clearTaskReviewSelection()
                selectCronJob(job.id)
              }
            },
            {
              label: job.enabled ? t('cron.disable') : t('cron.enable'),
              icon: <Pause size={14} />,
              onClick: () => void enableJob(job.id, !job.enabled)
            },
            {
              label: deleting ? t('cron.deleting') : t('cron.delete'),
              icon: <Trash2 size={14} />,
              danger: true,
              disabled: deleting,
              onClick: () => setShowDelete(true)
            }
          ]}
        />
      )}

      {showDelete && (
        <ConfirmDialog
          title={t('cron.deleteTitle')}
          message={t('cron.deleteConfirmMessage', { name: job.name })}
          confirmLabel={deleting ? t('cron.deleting') : t('cron.deleteConfirm')}
          danger
          onConfirm={() => void handleDeleteConfirm()}
          onCancel={() => setShowDelete(false)}
        />
      )}
    </>
  )
}

function iconButtonStyle(disabled: boolean): React.CSSProperties {
  return {
    width: '30px',
    height: '30px',
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    borderRadius: '8px',
    border: '1px solid var(--border-default)',
    backgroundColor: 'transparent',
    color: disabled ? 'var(--text-dimmed)' : 'var(--text-secondary)',
    cursor: disabled ? 'default' : 'pointer',
    opacity: disabled ? 0.6 : 1,
    padding: 0
  }
}
