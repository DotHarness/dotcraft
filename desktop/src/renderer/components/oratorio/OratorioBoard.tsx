import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import {
  Archive,
  CircleDot,
  FileText,
  Folder,
  GitPullRequest,
  Plus,
  RefreshCw,
  RotateCcw,
  Search,
  Settings,
  Tag,
} from 'lucide-react'
import {
  Button,
  IconButton,
  Input,
  Select,
  Skeleton,
} from './ui'
import { GithubGlyph, GitlabGlyph } from './ProviderGlyphs'
import { OratorioQuickView } from './OratorioQuickView'
import {
  ORATORIO_COLUMNS,
  taskMatches,
  type BoardMode,
  type OratorioTask,
  type TaskColumn,
  type TaskStage,
} from './oratorio-model'
import { OratorioBrandMark } from './OratorioBrandMark'
import type { QuickActionId } from './oratorio-workflow'
import { addToast } from '../../stores/toastStore'
import { NewLocalTaskDialog, type NewLocalTaskDraft } from './NewLocalTaskDialog'

type BoardPresentation = 'ready' | 'loading' | 'empty' | 'error'

export interface OratorioBoardState {
  mode: BoardMode
  query: string
  repository: string
  assignee: string
  selectedTaskId: string | null
  scrollTop: number
}

export function OratorioBoard({
  presentation = 'ready',
  initialMode = 'active',
  tasks: serverTasks = [],
  onSync,
  onCreateTask,
  onLoadMore,
  hasMore = false,
  onReorder,
  onTaskAction,
  onLoadTaskDetail,
  onOpenDetail,
  onOpenSettings,
  onOpenThread,
  initialState,
  onStateChange,
}: {
  presentation?: BoardPresentation
  initialMode?: BoardMode
  tasks?: OratorioTask[]
  onSync?: () => Promise<void>
  onCreateTask?: (task: NewLocalTaskDraft) => Promise<OratorioTask>
  onLoadMore?: () => Promise<void>
  hasMore?: boolean
  onReorder?: (task: OratorioTask, column: TaskColumn) => Promise<void>
  onTaskAction?: (task: OratorioTask, action: QuickActionId, note?: string) => Promise<OratorioTask>
  onLoadTaskDetail?: (task: OratorioTask) => Promise<OratorioTask>
  onOpenDetail: (task: OratorioTask, stage?: TaskStage, options?: { focus?: 'discussion' }) => void
  onOpenSettings: () => void
  onOpenThread: (task: OratorioTask) => void
  initialState?: OratorioBoardState
  onStateChange?: (state: OratorioBoardState) => void
}) {
  const boardRef = useRef<HTMLElement>(null)
  const [mode, setMode] = useState<BoardMode>(initialState?.mode ?? initialMode)
  const [query, setQuery] = useState(initialState?.query ?? '')
  const [repository, setRepository] = useState(initialState?.repository ?? 'all')
  const [assignee, setAssignee] = useState(initialState?.assignee ?? 'all')
  const [selected, setSelected] = useState<OratorioTask | null>(() => serverTasks.find((task) => task.id === initialState?.selectedTaskId) ?? null)
  const [syncing, setSyncing] = useState(false)
  const [taskItems, setTaskItems] = useState(serverTasks)
  const [draggingId, setDraggingId] = useState<string | null>(null)
  const [newTaskOpen, setNewTaskOpen] = useState(false)
  const [quickViewLoading, setQuickViewLoading] = useState(false)
  const [recovered, setRecovered] = useState(false)
  const effectivePresentation = presentation === 'error' && recovered ? 'ready' : presentation

  useEffect(() => {
    if (!initialState) setMode(initialMode)
  }, [initialMode])

  useEffect(() => {
    if (boardRef.current && initialState?.scrollTop) boardRef.current.scrollTop = initialState.scrollTop
  }, [])

  useEffect(() => {
    onStateChange?.({
      mode,
      query,
      repository,
      assignee,
      selectedTaskId: selected?.id ?? null,
      scrollTop: boardRef.current?.scrollTop ?? initialState?.scrollTop ?? 0
    })
  }, [assignee, mode, query, repository, selected?.id])

  useEffect(() => {
    setTaskItems(serverTasks)
    setSelected((current) => current ? serverTasks.find((task) => task.id === current.id) ?? current : null)
  }, [serverTasks])

  useEffect(() => {
    const selectedId = initialState?.selectedTaskId
    if (!selectedId) return
    const initialTask = taskItems.find((task) => task.id === selectedId)
    if (initialTask) setSelected(initialTask)
  }, [initialState?.selectedTaskId, taskItems])

  useEffect(() => {
    if (!selected || selected.detail || !onLoadTaskDetail) {
      setQuickViewLoading(false)
      return
    }
    let active = true
    setQuickViewLoading(true)
    void onLoadTaskDetail(selected).then((detailed) => {
      if (!active) return
      setTaskItems((items) => items.map((task) => task.id === detailed.id ? detailed : task))
      setSelected((current) => current?.id === detailed.id ? detailed : current)
    }).catch((error) => {
      if (active) addToast(error instanceof Error ? error.message : 'Task details could not be loaded', 'error')
    }).finally(() => {
      if (active) setQuickViewLoading(false)
    })
    return () => { active = false }
  }, [onLoadTaskDetail, selected?.detail, selected?.id])

  const source = useMemo(() => {
    if (effectivePresentation === 'empty') return []
    if (mode === 'active') return taskItems.filter((task) => task.lifecycle === 'open')
    if (mode === 'all') return taskItems
    if (mode === 'cancelled') return taskItems.filter((task) => task.cancelled)
    return taskItems.filter((task) => task.archived)
  }, [effectivePresentation, mode, taskItems])
  const tasks = useMemo(
    () => source.filter((task) => taskMatches(task, query, repository, assignee)),
    [assignee, query, repository, source],
  )
  const repositories = ['all', ...Array.from(new Set(taskItems.filter((task) => task.provider !== 'local').map((task) => task.repository)))]
  const assignees = ['all', 'unassigned', ...Array.from(new Set(taskItems.map((task) => task.assignee).filter((value): value is string => Boolean(value))))]

  function sync(): void {
    setSyncing(true)
    void (onSync?.() ?? Promise.resolve()).then(() => {
      addToast('Sources updated', 'success')
    }).catch(() => addToast('Source sync failed', 'error')).finally(() => setSyncing(false))
  }

  return (
    <main
      ref={boardRef}
      className="ora-board"
      aria-label="Oratorio task board"
      onScroll={() => {
        const element = boardRef.current
        onStateChange?.({ mode, query, repository, assignee, selectedTaskId: selected?.id ?? null, scrollTop: element?.scrollTop ?? 0 })
        if (mode !== 'active' && hasMore && element && element.scrollHeight - element.scrollTop - element.clientHeight < 160) void onLoadMore?.()
      }}
    >
      <header className="ora-board__brand">
        <OratorioBrandMark />
        <strong>Oratorio</strong>
      </header>

      <div className="ora-board__toolbar" aria-label="Board controls">
        <div className="ora-board__modes" role="group" aria-label="Task view">
          {(['active', 'all', 'cancelled', 'archived'] as BoardMode[]).map((value) => (
            <button type="button" key={value} aria-pressed={mode === value} onClick={() => setMode(value)}>
              {value === 'active' ? 'Active' : value[0].toUpperCase() + value.slice(1)}
            </button>
          ))}
        </div>
        <label className="ora-board__search">
          <Search size={15} aria-hidden="true" />
          <Input bare value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Search tasks · source:github · label:frontend" aria-label="Search tasks" />
        </label>
        <Select
          ariaLabel="Repository"
          value={repository}
          onValueChange={setRepository}
          options={repositories.map((value) => ({ value, label: value === 'all' ? 'All repositories' : value }))}
          appearance="frameless"
        />
        <Select
          ariaLabel="Assignee"
          value={assignee}
          onValueChange={setAssignee}
          options={assignees.map((value) => ({ value, label: value === 'all' ? 'All assignees' : value }))}
          appearance="frameless"
        />
        <span className="ora-board__actions">
          <IconButton icon={<Plus size={15} />} label="New local task" tooltipLabel="New local task" onClick={() => setNewTaskOpen(true)} />
          <IconButton icon={<RefreshCw size={15} className={syncing ? 'ora-spin' : undefined} />} label="Sync sources" tooltipLabel="Sync sources" onClick={sync} disabled={syncing} />
          <IconButton icon={<Settings size={15} />} label="Oratorio settings" tooltipLabel="Oratorio settings" onClick={onOpenSettings} />
        </span>
      </div>

      {effectivePresentation === 'loading' ? <BoardSkeleton /> : effectivePresentation === 'error' ? (
        <InlineBoardError onRetry={() => { setRecovered(true); addToast('Board restored', 'success') }} />
      ) : mode === 'active' ? (
        <div className="ora-board__columns">
          {ORATORIO_COLUMNS.map((column) => {
            const columnTasks = tasks.filter((task) => task.column === column.id)
            return (
              <section
                className="ora-column"
                key={column.id}
                aria-label={column.label}
                data-drag-target={draggingId ? 'true' : undefined}
                onDragOver={(event) => event.preventDefault()}
                onDrop={() => {
                  if (!draggingId) return
                  const moving = taskItems.find((task) => task.id === draggingId)
                  if (!moving) return
                  const action = dragAction(moving, column.id)
                  if (!action) {
                    if (moving.column === column.id) void onReorder?.(moving, column.id).catch(() => addToast('Task order could not be saved', 'error'))
                    else addToast(`Cannot move ${moving.shortId} from ${moving.column} to ${column.label}`, 'warning')
                    setDraggingId(null)
                    return
                  }
                  if (action === 'cancel-run' && !window.confirm(`Cancel the active run for ${moving.shortId}?`)) { setDraggingId(null); return }
                  void onTaskAction?.(moving, action).then((updated) => {
                    setTaskItems((items) => items.map((task) => task.id === updated.id ? updated : task))
                    addToast(action === 'cancel-run' ? 'Run cancelled' : `Task moved to ${column.label}`, 'success')
                  }).catch(() => addToast('Task action could not be completed', 'error'))
                  setDraggingId(null)
                }}
              >
                <header className="ora-column__header">
                  <strong>{column.label}</strong>
                  <b>{columnTasks.length}</b>
                </header>
                <div className="ora-column__stack">
                  {columnTasks.length ? columnTasks.map((task) => (
                    <TaskCard key={task.id} task={task} selected={selected?.id === task.id} onOpen={() => setSelected(task)} onDragStart={() => setDraggingId(task.id)} onDragEnd={() => setDraggingId(null)} />
                  )) : <p className="ora-board__empty">No tasks</p>}
                </div>
              </section>
            )
          })}
        </div>
      ) : (
        <ClosedTaskList mode={mode} tasks={tasks} onOpen={setSelected} hasMore={hasMore} onLoadMore={onLoadMore} />
      )}

      {selected ? (
        <OratorioQuickView
          key={selected.id}
          task={selected}
          loading={quickViewLoading}
          onClose={() => setSelected(null)}
          onOpenDetail={(stage, options) => onOpenDetail(selected, stage, options)}
          onOpenThread={() => onOpenThread(selected)}
          onTaskChange={(nextTask) => {
            setTaskItems((items) => items.map((task) => task.id === nextTask.id ? nextTask : task))
            setSelected(nextTask)
          }}
          onAction={onTaskAction ? (action, note) => onTaskAction(selected, action, note) : undefined}
        />
      ) : null}
      {newTaskOpen ? <NewLocalTaskDialog tasks={taskItems} onCancel={() => setNewTaskOpen(false)} onCreate={(draft) => {
        if (!onCreateTask) return
        void onCreateTask(draft).then((task) => {
          setTaskItems((items) => [task, ...items.filter((item) => item.id !== task.id)])
          setNewTaskOpen(false)
          setSelected(task)
          addToast('Local task created', 'success')
        }).catch(() => addToast('Local task could not be created', 'error'))
      }} /> : null}
    </main>
  )
}

function TaskCard({ task, selected, onOpen, onDragStart, onDragEnd }: { task: OratorioTask; selected?: boolean; onOpen: () => void; onDragStart: () => void; onDragEnd: () => void }) {
  return (
    <button type="button" className="ora-card" data-selected={selected ? 'true' : undefined} onClick={onOpen} draggable onDragStart={onDragStart} onDragEnd={onDragEnd}>
      <span className="ora-card__topline">
        <span className="ora-chip ora-chip--source"><ProviderIcon task={task} /> <span>{task.provider === 'local' ? 'Local' : task.repository}</span></span>
        <span className="ora-chip"><KindIcon task={task} /> {task.sourceLabel}</span>
      </span>
      <span className="ora-card__title"><strong>{task.title}</strong><StateDot task={task} /></span>
      <span className="ora-card__description">{task.description}</span>
      <span className="ora-card__meta">{task.synced ? `${task.headSha ? `${task.headSha} · ` : ''}synced ${task.synced} · ` : ''}updated {task.updated}</span>
      <span className="ora-card__footer">
        {task.lifecycle && task.lifecycle !== 'open' ? <CompactStatus icon={<Archive size={11} />} label={task.lifecycle} /> : null}
        {task.check ? <CompactStatus icon={task.check === 'passing' ? <span>✓</span> : <span>◌</span>} label={task.check} tone={task.check} /> : null}
        {task.labels.slice(0, 2).map((label) => <span className="ora-label" key={label}><Tag size={11} />{label}</span>)}
        {task.labels.length > 2 ? <span className="ora-label-more">+{task.labels.length - 2}</span> : null}
      </span>
    </button>
  )
}

function ClosedTaskList({ mode, tasks, onOpen, hasMore, onLoadMore }: { mode: BoardMode; tasks: OratorioTask[]; onOpen: (task: OratorioTask) => void; hasMore: boolean; onLoadMore?: () => Promise<void> }) {
  return (
    <section className="ora-closed" aria-label={`${mode} tasks`}>
      <header><strong>{mode[0].toUpperCase() + mode.slice(1)}</strong><b>{tasks.length}</b></header>
      <div className="ora-closed__list">
        {tasks.length ? tasks.map((task) => (
          <button type="button" key={task.id} onClick={() => onOpen(task)}>
            <ProviderIcon task={task} />
            <span><strong>{task.title}</strong><small>{task.repository} · {task.sourceLabel}</small></span>
            <span className="ora-closed__labels">{task.labels.map((label) => <span className="ora-label" key={label}>{label}</span>)}</span>
            <span>{task.updated}</span>
          </button>
        )) : <p className="ora-board__empty">No matching tasks</p>}
      </div>
      {hasMore ? <footer><Button size="sm" variant="secondary" onClick={() => void onLoadMore?.()}>Load more</Button></footer> : null}
    </section>
  )
}

function dragAction(task: OratorioTask, target: TaskColumn): QuickActionId | null {
  if (task.column === 'todo' && target === 'in-progress') return 'dispatch'
  if (task.column === 'in-review' && target === 'in-progress') return 'request-changes'
  if (task.column === 'in-review' && target === 'done') return 'approve'
  if (task.column === 'in-progress' && target === 'todo' && (task.state === 'dispatching' || task.state === 'running')) return 'cancel-run'
  return null
}

function BoardSkeleton() {
  return <div className="ora-board__columns" role="status" aria-label="Loading board">{ORATORIO_COLUMNS.map((column) => <section className="ora-column" key={column.id}><header className="ora-column__header"><Skeleton width={80} height={14} /><Skeleton circle width={24} height={24} /></header><div className="ora-column__stack"><Skeleton height={190} radius={8} /></div></section>)}</div>
}

function InlineBoardError({ onRetry }: { onRetry: () => void }) {
  return <section className="ora-board__recovery" role="alert"><span><strong>Oratorio couldn’t load this board</strong><small>The managed service did not answer. Your filters and navigation state are preserved.</small></span><Button variant="secondary" iconLeft={<RotateCcw size={14} />} onClick={onRetry}>Retry</Button></section>
}

function ProviderIcon({ task }: { task: OratorioTask }) {
  if (task.provider === 'github') return <GithubGlyph />
  if (task.provider === 'gitlab') return <GitlabGlyph />
  return <Folder size={14} />
}

function KindIcon({ task }: { task: OratorioTask }) {
  if (task.kind === 'Pull request') return <GitPullRequest size={13} />
  if (task.kind === 'Issue') return <CircleDot size={13} />
  return <FileText size={13} />
}

function StateDot({ task }: { task: OratorioTask }) {
  const tone = task.state === 'awaiting-review' ? 'warning' : task.state === 'failed' ? 'error' : task.state === 'approved' ? 'success' : task.state === 'running' || task.state === 'dispatching' ? 'info' : 'neutral'
  return <span className="ora-state-dot" data-tone={tone} title={task.state} />
}

function CompactStatus({ icon, label, tone }: { icon: ReactNode; label: string; tone?: string }) {
  return <span className="ora-status" data-tone={tone}>{icon}{label}</span>
}
