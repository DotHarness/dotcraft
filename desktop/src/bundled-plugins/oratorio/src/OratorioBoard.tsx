import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import {
  Archive,
  Plus,
  RefreshCw,
  RotateCcw,
  Search,
  Settings,
  Tag,
  TriangleAlert,
} from 'lucide-react'
import {
  Button,
  IconButton,
  Input,
  Select,
  Skeleton,
} from './ui'
import { OratorioTaskDetail } from './OratorioTaskDetail'
import {
  ORATORIO_COLUMNS,
  repositoryName,
  repositoryPath,
  taskMatches,
  type BoardMode,
  type OratorioTask,
  type TaskColumn,
} from './oratorio-model'
import { OratorioBrandMark } from './OratorioBrandMark'
import { oratorioHost, showOratorioToast } from './runtime'
import { NewLocalTaskDialog, type NewLocalTaskDraft } from './NewLocalTaskDialog'
import { OratorioBoardEmptyState } from './OratorioBoardEmptyState'
import { useOratorioI18n, type OratorioMessageKey, type OratorioTranslate } from './oratorio-i18n'
import { KindIcon, ProviderIcon, sourceLabel, taskStateKey, taskStateTone, type TaskTone } from './OratorioTaskParts'
import type { SourceProvider } from './settings/oratorio-settings-model'
import type { LatestResult } from './oratorio-contracts'

type BoardPresentation = 'ready' | 'loading' | 'empty' | 'unconfigured' | 'error'

export interface OratorioBoardState {
  mode: BoardMode
  query: string
  repository: string
  assignee: string
  selectedTaskId: string | null
  scrollTop: number
}

const MODES: Array<{ id: BoardMode; label: OratorioMessageKey }> = [
  { id: 'active', label: 'modeActive' },
  { id: 'all', label: 'modeAll' },
  { id: 'cancelled', label: 'modeCancelled' },
  { id: 'archived', label: 'modeArchived' },
]

const COLUMN_LABELS: Record<TaskColumn, OratorioMessageKey> = {
  todo: 'laneTodo',
  'in-progress': 'laneInProgress',
  'in-review': 'laneInReview',
  done: 'laneDone',
}

export type BoardTaskAction = 'dispatch' | 'request-changes' | 'approve' | 'cancel-run'

export function OratorioBoard({
  presentation = 'ready',
  initialMode = 'active',
  tasks: serverTasks = [],
  onSync,
  onRetry,
  onCreateTask,
  onLoadMore,
  hasMore = false,
  onReorder,
  onTaskAction,
  onLoadTaskDetail,
  onOpenDetail,
  onOpenSettings,
  onConnectSource,
  onOpenThread,
  initialState,
  onStateChange,
}: {
  presentation?: BoardPresentation
  initialMode?: BoardMode
  tasks?: OratorioTask[]
  onSync?: () => Promise<void>
  onRetry?: () => Promise<void>
  onCreateTask?: (task: NewLocalTaskDraft) => Promise<OratorioTask>
  onLoadMore?: () => Promise<void>
  hasMore?: boolean
  onReorder?: (task: OratorioTask, column: TaskColumn) => Promise<void>
  onTaskAction?: (task: OratorioTask, action: BoardTaskAction) => Promise<OratorioTask>
  onLoadTaskDetail?: (task: OratorioTask) => Promise<OratorioTask>
  onOpenDetail: (task: OratorioTask) => void
  onOpenSettings: () => void
  onConnectSource?: (provider: SourceProvider) => void
  onOpenThread: (task: OratorioTask) => void
  initialState?: OratorioBoardState
  onStateChange?: (state: OratorioBoardState) => void
}) {
  const { t, relativeTime } = useOratorioI18n()
  const [mode, setMode] = useState<BoardMode>(initialState?.mode ?? initialMode)
  const [query, setQuery] = useState(initialState?.query ?? '')
  const [repository, setRepository] = useState(initialState?.repository ?? 'all')
  const [assignee, setAssignee] = useState(initialState?.assignee ?? 'all')
  const [selected, setSelected] = useState<OratorioTask | null>(() => serverTasks.find((task) => task.id === initialState?.selectedTaskId) ?? null)
  const [syncing, setSyncing] = useState(false)
  const [taskItems, setTaskItems] = useState(serverTasks)
  const [draggingId, setDraggingId] = useState<string | null>(null)
  const [newTaskOpen, setNewTaskOpen] = useState(false)
  const [detailRefresh, setDetailRefresh] = useState(0)
  const selectedRef = useRef(selected)
  selectedRef.current = selected
  const scrollTop = useRef(initialState?.scrollTop ?? 0)

  function reportState(): void {
    onStateChange?.({ mode, query, repository, assignee, selectedTaskId: selected?.id ?? null, scrollTop: scrollTop.current })
  }

  useEffect(() => {
    if (!initialState) setMode(initialMode)
  }, [initialMode])

  useEffect(reportState, [assignee, mode, query, repository, selected?.id])

  useEffect(() => {
    setTaskItems(serverTasks)
    const current = selectedRef.current
    const next = current ? serverTasks.find((task) => task.id === current.id) : undefined
    if (!current || !next) return
    if (next.detail) { setSelected(next); return }
    if (next.updatedAt !== current.updatedAt || next.state !== current.state) setDetailRefresh((count) => count + 1)
    setSelected({ ...next, body: next.body ?? current.body, detail: current.detail })
  }, [serverTasks])

  useEffect(() => {
    const selectedId = initialState?.selectedTaskId
    if (!selectedId) return
    const initialTask = taskItems.find((task) => task.id === selectedId)
    if (initialTask) setSelected(initialTask)
  }, [initialState?.selectedTaskId, taskItems])

  useEffect(() => {
    const task = selectedRef.current
    if (!task || !onLoadTaskDetail) return
    let active = true
    void onLoadTaskDetail(task).then((detailed) => {
      if (!active) return
      setTaskItems((items) => items.map((item) => item.id === detailed.id ? detailed : item))
      setSelected((current) => current?.id === detailed.id ? detailed : current)
    }).catch((error) => {
      if (active) showOratorioToast({ message: error instanceof Error ? error.message : t('detailsNotLoaded'), tone: 'error' })
    })
    return () => { active = false }
  }, [onLoadTaskDetail, selected?.id, detailRefresh])

  const source = useMemo(() => {
    if (presentation === 'empty' || presentation === 'unconfigured') return []
    if (mode === 'active') return taskItems.filter((task) => task.lifecycle === 'open' && !task.archived && !task.cancelled)
    if (mode === 'all') return taskItems
    if (mode === 'cancelled') return taskItems.filter((task) => task.cancelled)
    return taskItems.filter((task) => task.archived)
  }, [presentation, mode, taskItems])
  const tasks = useMemo(
    () => source.filter((task) => taskMatches(task, query, repository, assignee)),
    [assignee, query, repository, source],
  )
  const repositories = ['all', ...Array.from(new Set(taskItems.filter((task) => task.provider !== 'local' && task.repository).map((task) => task.repository)))]
  const assignees = ['all', 'unassigned', ...Array.from(new Set(taskItems.map((task) => task.assignee).filter((value): value is string => Boolean(value))))]

  function sync(): void {
    setSyncing(true)
    void (onSync?.() ?? Promise.resolve()).then(() => {
      showOratorioToast({ message: t('sourcesUpdated'), tone: 'success' })
    }).catch(() => showOratorioToast({ message: t('sourceSyncFailed'), tone: 'error' })).finally(() => setSyncing(false))
  }

  function drop(column: TaskColumn): void {
    if (!draggingId) return
    const moving = taskItems.find((task) => task.id === draggingId)
    if (!moving) return
    const columnLabel = t(COLUMN_LABELS[column])
    const action = dragAction(moving, column)
    if (!action) {
      if (moving.column === column) void onReorder?.(moving, column).catch(() => showOratorioToast({ message: t('orderNotSaved'), tone: 'error' }))
      else showOratorioToast({ message: t('cannotMove', { task: moving.shortId, lane: columnLabel }), tone: 'warning' })
      setDraggingId(null)
      return
    }
    void (async () => {
      if (action === 'cancel-run') {
        const confirmed = await oratorioHost().ui.confirm({
          title: t('actionCancelRun'),
          message: t('cancelRunMessage', { task: moving.shortId }),
          confirmLabel: t('actionCancelRun'),
          danger: true
        })
        if (!confirmed) return
      }
      const updated = await onTaskAction?.(moving, action)
      if (updated) setTaskItems((items) => items.map((task) => task.id === updated.id ? updated : task))
      showOratorioToast({ message: action === 'cancel-run' ? t('toastRunCancelled') : t('taskMovedTo', { lane: columnLabel }), tone: 'success' })
    })().catch(() => showOratorioToast({ message: t('actionNotCompleted'), tone: 'error' }))
    setDraggingId(null)
  }

  return (
    <main className="ora-board" aria-label={t('boardLabel')}>
      <header className="ora-board__brand">
        <OratorioBrandMark />
        <h1>Oratorio</h1>
      </header>

      <div className="ora-board__toolbar" aria-label={t('boardControls')}>
        <div className="ora-board__modes" role="group" aria-label={t('taskView')}>
          {MODES.map((value) => (
            <button type="button" key={value.id} data-mode={value.id} aria-pressed={mode === value.id} onClick={() => setMode(value.id)}>
              {t(value.label)}
            </button>
          ))}
        </div>
        <label className="ora-board__search">
          <Search size={15} aria-hidden="true" />
          <Input bare value={query} onChange={(event) => setQuery(event.target.value)} placeholder={t('searchPlaceholder')} aria-label={t('searchTasks')} />
        </label>
        <Select
          ariaLabel={t('repository')}
          value={repository}
          onValueChange={setRepository}
          options={repositories.map((value) => ({ value, label: value === 'all' ? t('allRepositories') : repositoryName(value) }))}
          appearance="frameless"
        />
        <Select
          ariaLabel={t('assignee')}
          value={assignee}
          onValueChange={setAssignee}
          options={assignees.map((value) => ({ value, label: value === 'all' ? t('allAssignees') : value === 'unassigned' ? t('unassigned') : value }))}
          appearance="frameless"
        />
        <span className="ora-board__actions">
          <IconButton className="ora-board__new-task" icon={<Plus size={15} />} label={t('newLocalTask')} tooltipLabel={t('newLocalTask')} onClick={() => setNewTaskOpen(true)} />
          <IconButton icon={<RefreshCw size={15} className={syncing ? 'ora-spin' : undefined} />} label={t('syncSources')} tooltipLabel={t('syncSources')} onClick={sync} disabled={syncing || presentation === 'unconfigured'} />
          <IconButton icon={<Settings size={15} />} label={t('oratorioSettings')} tooltipLabel={t('oratorioSettings')} onClick={onOpenSettings} />
        </span>
      </div>

      {presentation === 'loading' ? <BoardSkeleton label={t('loadingBoard')} /> : presentation === 'unconfigured' ? (
        <OratorioBoardEmptyState onConnect={(provider) => onConnectSource?.(provider)} onCreateTask={() => setNewTaskOpen(true)} />
      ) : presentation === 'error' ? (
        <InlineBoardError t={t} onRetry={onRetry} />
      ) : mode === 'active' ? (
        <div className="ora-board__columns">
          {ORATORIO_COLUMNS.map((column) => {
            const columnTasks = tasks.filter((task) => task.column === column)
            const label = t(COLUMN_LABELS[column])
            return (
              <section
                className="ora-column"
                key={column}
                aria-label={label}
                data-drag-target={draggingId ? 'true' : undefined}
                onDragOver={(event) => event.preventDefault()}
                onDrop={() => drop(column)}
              >
                <header className="ora-column__header">
                  <h2>{label}</h2>
                  <b>{columnTasks.length}</b>
                </header>
                <div className="ora-column__stack dc-scrollbar-stable">
                  {columnTasks.length ? columnTasks.map((task) => (
                    <TaskCard key={task.id} task={task} t={t} relativeTime={relativeTime} selected={selected?.id === task.id} onOpen={() => setSelected(task)} onDragStart={() => setDraggingId(task.id)} onDragEnd={() => setDraggingId(null)} />
                  )) : <p className="ora-board__empty">{t('noTasks')}</p>}
                </div>
              </section>
            )
          })}
        </div>
      ) : (
        <ClosedTaskList
          title={t(MODES.find((value) => value.id === mode)!.label)}
          tasks={tasks}
          t={t}
          relativeTime={relativeTime}
          onOpen={setSelected}
          hasMore={hasMore}
          onLoadMore={onLoadMore}
          initialScrollTop={scrollTop.current}
          onScrollTop={(value) => { scrollTop.current = value; reportState() }}
        />
      )}

      {selected ? (
        <OratorioTaskDetail
          key={selected.id}
          task={selected}
          presentation="drawer"
          closeOnEscape={!newTaskOpen}
          onBack={() => setSelected(null)}
          onExpand={() => onOpenDetail(selected)}
          onOpenThread={() => onOpenThread(selected)}
          onTaskChange={(nextTask) => {
            setTaskItems((items) => items.map((task) => task.id === nextTask.id ? nextTask : task))
            setSelected(nextTask)
          }}
        />
      ) : null}
      {newTaskOpen ? <NewLocalTaskDialog tasks={taskItems} onCancel={() => setNewTaskOpen(false)} onCreate={(draft) => {
        if (!onCreateTask) return
        void onCreateTask(draft).then((task) => {
          setTaskItems((items) => [task, ...items.filter((item) => item.id !== task.id)])
          setNewTaskOpen(false)
          setSelected(task)
          showOratorioToast({ message: t('localTaskCreated'), tone: 'success' })
        }).catch(() => showOratorioToast({ message: t('localTaskNotCreated'), tone: 'error' }))
      }} /> : null}
    </main>
  )
}

function TaskCard({ task, t, relativeTime, selected, onOpen, onDragStart, onDragEnd }: {
  task: OratorioTask
  t: OratorioTranslate
  relativeTime: (value: string) => string
  selected?: boolean
  onOpen: () => void
  onDragStart: () => void
  onDragEnd: () => void
}) {
  const sourceName = task.provider === 'local' ? t('sourceLocal') : repositoryName(task.repository)
  const synced = task.syncedAt ? [task.headSha, t('metaSynced', { time: relativeTime(task.syncedAt) })].filter(Boolean).join(' · ') : ''
  const meta = [synced, t('metaUpdated', { time: relativeTime(task.updatedAt) })].filter(Boolean).join(' · ')
  return (
    <button type="button" className="ora-card" data-selected={selected ? 'true' : undefined} onClick={onOpen} draggable onDragStart={onDragStart} onDragEnd={onDragEnd}>
      <span className="ora-card__topline">
        <span className="ora-chip ora-chip--source" title={task.repository ? repositoryPath(task.repository) : undefined}><ProviderIcon task={task} />{sourceName ? <span>{sourceName}</span> : null}</span>
        <span className="ora-chip"><KindIcon task={task} /> {sourceLabel(task, t)}</span>
      </span>
      <span className="ora-card__title"><strong>{task.title}</strong><CardSignal task={task} t={t} /></span>
      <span className="ora-card__description">{task.description}</span>
      <span className="ora-card__meta">{meta}</span>
      <span className="ora-card__footer">
        {task.lifecycle && task.lifecycle !== 'open' ? <CompactStatus icon={<Archive size={11} />} label={t(task.lifecycle === 'merged' ? 'lifecycleMerged' : 'lifecycleClosed')} /> : null}
        {task.labels.slice(0, 2).map((label) => <span className="ora-label" key={label}><Tag size={11} />{label}</span>)}
        {task.labels.length > 2 ? <span className="ora-label-more">+{task.labels.length - 2}</span> : null}
      </span>
    </button>
  )
}

function ClosedTaskList({ title, tasks, t, relativeTime, onOpen, hasMore, onLoadMore, initialScrollTop, onScrollTop }: {
  title: string
  tasks: OratorioTask[]
  t: OratorioTranslate
  relativeTime: (value: string) => string
  onOpen: (task: OratorioTask) => void
  hasMore: boolean
  onLoadMore?: () => Promise<void>
  initialScrollTop: number
  onScrollTop: (value: number) => void
}) {
  const listRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (listRef.current) listRef.current.scrollTop = initialScrollTop
  }, [])

  return (
    <section className="ora-closed" aria-label={title}>
      <header><h2>{title}</h2><b>{tasks.length}</b></header>
      <div
        ref={listRef}
        className="ora-closed__list dc-scrollbar-stable"
        onScroll={(event) => {
          const element = event.currentTarget
          onScrollTop(element.scrollTop)
          if (hasMore && element.scrollHeight - element.scrollTop - element.clientHeight < 160) void onLoadMore?.()
        }}
      >
        {tasks.length ? tasks.map((task) => (
          <button type="button" key={task.id} className="ora-closed__row" onClick={() => onOpen(task)}>
            <ProviderIcon task={task} />
            <span><strong>{task.title}</strong><small title={task.repository ? repositoryPath(task.repository) : undefined}>{[task.repository && repositoryName(task.repository), sourceLabel(task, t)].filter(Boolean).join(' · ')}</small></span>
            <span className="ora-closed__labels">{task.labels.map((label) => <span className="ora-label" key={label}>{label}</span>)}</span>
            <span>{relativeTime(task.updatedAt)}</span>
          </button>
        )) : <p className="ora-board__empty">{t('noMatchingTasks')}</p>}
      </div>
      {hasMore ? <footer><Button size="sm" variant="secondary" onClick={() => void onLoadMore?.()}>{t('loadMore')}</Button></footer> : null}
    </section>
  )
}

function dragAction(task: OratorioTask, target: TaskColumn): BoardTaskAction | null {
  if (task.column === 'todo' && target === 'in-progress') return 'dispatch'
  if (task.column === 'in-review' && target === 'in-progress') return 'request-changes'
  if (task.column === 'in-review' && target === 'done') return 'approve'
  if (task.column === 'in-progress' && target === 'todo' && (task.state === 'dispatching' || task.state === 'running')) return 'cancel-run'
  return null
}

function BoardSkeleton({ label }: { label: string }) {
  return <div className="ora-board__columns" role="status" aria-label={label}>{ORATORIO_COLUMNS.map((column) => <section className="ora-column" key={column}><header className="ora-column__header"><Skeleton width={80} height={14} /><Skeleton circle width={24} height={24} /></header><div className="ora-column__stack dc-scrollbar-stable"><Skeleton height={190} radius={8} /></div></section>)}</div>
}

function InlineBoardError({ t, onRetry }: { t: OratorioTranslate; onRetry?: () => Promise<void> }) {
  const [retrying, setRetrying] = useState(false)
  return (
    <section className="ora-notice" data-tone="error" role="alert">
      <TriangleAlert size={20} aria-hidden="true" />
      <span className="ora-notice__copy">
        <strong>{t('boardErrorTitle')}</strong>
        <span>{t('boardErrorDescription')}</span>
      </span>
      {onRetry ? (
        <Button variant="secondary" iconLeft={<RotateCcw size={14} />} loading={retrying} onClick={() => {
          setRetrying(true)
          void onRetry().finally(() => setRetrying(false))
        }}>{t('retry')}</Button>
      ) : null}
    </section>
  )
}

function CardSignal({ task, t }: { task: OratorioTask; t: OratorioTranslate }) {
  const signal = cardSignal(task)
  return signal ? <span className="ora-state-dot" data-tone={signal.tone} title={t(signal.label)} /> : null
}

function cardSignal(task: OratorioTask): { tone: TaskTone; label: OratorioMessageKey } | null {
  if (task.column === 'in-progress') {
    const tone = taskStateTone(task)
    return tone === 'info' || tone === 'error' ? { tone, label: taskStateKey(task) } : null
  }
  if (task.column === 'in-review') return RESULT_SIGNALS[task.latestResult] ?? null
  return null
}

const RESULT_SIGNALS: Partial<Record<LatestResult, { tone: TaskTone; label: OratorioMessageKey }>> = {
  noIssues: { tone: 'success', label: 'resultNoIssues' },
  delivered: { tone: 'success', label: 'resultDelivered' },
  findings: { tone: 'warning', label: 'resultFindings' },
  changesReady: { tone: 'warning', label: 'resultChangesReady' },
  followUps: { tone: 'warning', label: 'resultFollowUps' },
}

function CompactStatus({ icon, label }: { icon: ReactNode; label: string }) {
  return <span className="ora-status">{icon}{label}</span>
}
