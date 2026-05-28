import { useMemo, useState } from 'react'
import { Ellipsis, Eye, Play, Trash2 } from 'lucide-react'
import { translate, type AppLocale } from '../../../shared/locales'
import { useLocale, useT } from '../../contexts/LocaleContext'
import type {
  AutomationSchedule,
  AutomationTask,
  AutomationTaskStatus
} from '../../stores/automationsStore'
import { useAutomationsStore } from '../../stores/automationsStore'
import { useCronStore } from '../../stores/cronStore'
import { useDragDropStore } from '../../stores/dragDropStore'
import { useThreadStore } from '../../stores/threadStore'
import { ConfirmDialog } from '../ui/ConfirmDialog'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { ActionTooltip } from '../ui/ActionTooltip'
import { StatusBadge } from './StatusBadge'
import { addToast } from '../../stores/toastStore'

/** MIME key for drag-and-drop binding a task to a thread entry. */
export const AUTOMATION_TASK_DRAG_MIME = 'application/x-dotcraft-automation-task'

function isTaskDeletable(status: AutomationTaskStatus): boolean {
  return (
    status === 'pending' ||
    status === 'completed' ||
    status === 'failed'
  )
}

function relativeTime(iso: string, locale: AppLocale): string {
  const diff = Date.now() - new Date(iso).getTime()
  const seconds = Math.floor(diff / 1000)
  if (seconds < 60) return translate(locale, 'cron.display.justNow')
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return translate(locale, 'cron.last.minAgo', { n: minutes })
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return translate(locale, 'cron.last.hourAgo', { n: hours })
  const days = Math.floor(hours / 24)
  return translate(locale, 'cron.last.dayAgo', { n: days })
}

function formatSchedule(s: AutomationSchedule | null | undefined, t: ReturnType<typeof useT>): string | null {
  if (!s) return null
  if (s.kind === 'daily') {
    const h = String(s.dailyHour ?? 9).padStart(2, '0')
    const m = String(s.dailyMinute ?? 0).padStart(2, '0')
    return t('auto.taskCard.scheduleDaily', { time: `${h}:${m}` })
  }
  if (s.kind === 'every') {
    const ms = s.everyMs ?? 0
    const HOUR = 60 * 60 * 1000
    if (ms === HOUR) return t('auto.taskCard.scheduleHourly')
    if (ms === 7 * 24 * HOUR) return t('auto.taskCard.scheduleWeekly')
    const minutes = Math.round(ms / 60_000)
    return t('auto.taskCard.scheduleEvery', { interval: `${minutes}m` })
  }
  return null
}

export function TaskCard({ task }: { task: AutomationTask }): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const [hovered, setHovered] = useState(false)
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [runningNow, setRunningNow] = useState(false)
  const [menuPosition, setMenuPosition] = useState<ContextMenuPosition | null>(null)
  const selectTask = useAutomationsStore((s) => s.selectTask)
  const selectCronJob = useCronStore((s) => s.selectCronJob)
  const deleteTask = useAutomationsStore((s) => s.deleteTask)
  const runTaskNow = useAutomationsStore((s) => s.runTaskNow)
  const threadList = useThreadStore((s) => s.threadList)
  const deletable = isTaskDeletable(task.status)
  const draggable = true
  const runnable = task.status !== 'running'

  const boundThreadName = useMemo(() => {
    const id = task.threadBinding?.threadId
    if (!id) return null
    const m = threadList.find((th) => th.id === id)
    return m?.displayName ?? id
  }, [task.threadBinding, threadList])

  const scheduleText = useMemo(() => formatSchedule(task.schedule, t), [task.schedule, t])

  const isDragging = useDragDropStore(
    (s) => s.active?.kind === 'automation-task' && s.active.taskId === task.id
  )

  function handleDragStart(e: React.DragEvent): void {
    e.dataTransfer.setData(AUTOMATION_TASK_DRAG_MIME, task.id)
    // Provide a human-readable label for targets that surface it via effectAllowed.
    e.dataTransfer.setData('text/plain', task.title)
    e.dataTransfer.effectAllowed = 'link'
    useDragDropStore.getState().start({
      kind: 'automation-task',
      taskId: task.id,
      title: task.title,
      alreadyBoundThreadId: task.threadBinding?.threadId ?? null
    })
  }

  function handleDragEnd(): void {
    useDragDropStore.getState().end()
  }

  function focusThisTask(): void {
    selectCronJob(null)
    selectTask(task.id)
  }

  async function handleDeleteConfirm(): Promise<void> {
    setDeleting(true)
    try {
      await deleteTask(task)
      setShowDeleteConfirm(false)
    } finally {
      setDeleting(false)
    }
  }

  async function handleRunNow(): Promise<void> {
    if (!runnable || runningNow) return
    setRunningNow(true)
    try {
      await runTaskNow(task)
      addToast(t('auto.runNowQueued'), 'success')
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      addToast(t('auto.runNowFailed', { error: msg }), 'error')
    } finally {
      setRunningNow(false)
    }
  }

  return (
    <>
    <div
      role="button"
      tabIndex={0}
      draggable={draggable}
      onDragStart={draggable ? handleDragStart : undefined}
      onDragEnd={draggable ? handleDragEnd : undefined}
      onClick={() => focusThisTask()}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') focusThisTask()
      }}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: '12px',
        padding: '10px 14px',
        borderRadius: '8px',
        backgroundColor: hovered ? 'var(--bg-secondary)' : 'transparent',
        cursor: draggable ? (isDragging ? 'grabbing' : 'grab') : 'pointer',
        opacity: isDragging ? 0.55 : 1,
        transform: isDragging ? 'scale(0.98) rotate(-0.8deg)' : 'none',
        transition: 'background-color 0.15s, opacity 120ms ease, transform 120ms ease'
      }}
    >
      <div style={{ flexShrink: 0 }}>
        <StatusBadge status={task.status} />
      </div>

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
          {task.title}
        </div>
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
            marginTop: '2px',
            fontSize: '12px',
            color: 'var(--text-tertiary)',
            flexWrap: 'wrap'
          }}
        >
          <span>{relativeTime(task.updatedAt, locale)}</span>
          {boundThreadName && (
            <span
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: '3px',
                padding: '1px 6px',
                borderRadius: '8px',
                backgroundColor: 'color-mix(in srgb, var(--accent) 12%, transparent)',
                color: 'var(--accent)',
                fontSize: '11px',
                fontWeight: 500
              }}
              title={t('auto.taskCard.bound', { name: boundThreadName })}
            >
              💬 {boundThreadName}
            </span>
          )}
          {scheduleText && (
            <span
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                padding: '1px 6px',
                borderRadius: '8px',
                backgroundColor: 'var(--bg-tertiary)',
                color: 'var(--text-secondary)',
                fontSize: '11px',
                fontWeight: 500
              }}
            >
              ⏰ {scheduleText}
            </span>
          )}
        </div>
      </div>

      <div
        style={{
          flexShrink: 0,
          display: 'flex',
          alignItems: 'center',
          gap: '6px'
        }}
      >
        <ActionTooltip
          label={runningNow ? t('auto.runningNow') : t('auto.runNow')}
          disabledReason={
            task.status === 'running'
              ? t('auto.runNowAlreadyRunning')
              : undefined
          }
          placement="top"
        >
          <button
            type="button"
            disabled={!runnable || runningNow}
            onClick={(e) => {
              e.stopPropagation()
              void handleRunNow()
            }}
            style={iconButtonStyle(!runnable || runningNow)}
            aria-label={runningNow ? t('auto.runningNow') : t('auto.runNow')}
          >
            <Play size={14} aria-hidden fill="currentColor" />
          </button>
        </ActionTooltip>
        <ActionTooltip label={t('auto.moreActions')} placement="top">
          <button
            type="button"
            onClick={(e) => {
              e.stopPropagation()
              const rect = e.currentTarget.getBoundingClientRect()
              setMenuPosition({ x: rect.left, y: rect.bottom + 6 })
            }}
            style={iconButtonStyle(false)}
            aria-label={t('auto.moreActions')}
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
            label: t('auto.task.view'),
            icon: <Eye size={14} />,
            onClick: focusThisTask
          },
          {
            label: deleting ? t('auto.deleting') : t('auto.delete'),
            icon: <Trash2 size={14} />,
            danger: true,
            disabled: !deletable || deleting,
            onClick: () => setShowDeleteConfirm(true)
          }
        ]}
      />
    )}

    {showDeleteConfirm && (
      <ConfirmDialog
        title={t('auto.task.deleteTitle')}
        message={
          task.threadId ? t('auto.task.deleteWithThread') : t('auto.task.deleteOnly')
        }
        confirmLabel={deleting ? t('auto.deleting') : t('auto.delete')}
        danger
        onConfirm={() => void handleDeleteConfirm()}
        onCancel={() => setShowDeleteConfirm(false)}
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
