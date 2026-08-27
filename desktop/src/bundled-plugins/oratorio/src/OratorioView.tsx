import { useCallback, useEffect, useRef, useState } from 'react'
import type { DesktopPluginOratorioBoardEvent, DesktopPluginViewProps } from '@dotcraft/plugin'
import { OratorioBoard, type OratorioBoardState } from './OratorioBoard'
import { oratorioClient } from './oratorio-client'
import { mapItemDetail, mapItemSummary } from './oratorio-mappers'
import type { OratorioTask, TaskStage } from './oratorio-model'
import { OratorioTaskDetail } from './OratorioTaskDetail'
import type { QuickActionId } from './oratorio-workflow'
import { consumeOratorioNavigation, onOratorioNavigation, type OratorioNavigationTarget } from './oratorio-navigation'
import './oratorio.css'

const defaultBoardState: OratorioBoardState = { mode: 'active', query: '', repository: 'all', assignee: 'all', selectedTaskId: null, scrollTop: 0 }
let retainedViewState: { board: OratorioBoardState; detailTaskId: string | null; stage: TaskStage; focus?: 'discussion' } = { board: defaultBoardState, detailTaskId: null, stage: 'review' }

export function OratorioView({ host }: DesktopPluginViewProps): JSX.Element {
  const [tasks, setTasks] = useState<OratorioTask[]>([])
  const [selectedTask, setSelectedTask] = useState<OratorioTask | null>(null)
  const [selectedStage, setSelectedStage] = useState<TaskStage>(retainedViewState.stage)
  const [selectedFocus, setSelectedFocus] = useState<'discussion' | undefined>(retainedViewState.focus)
  const [serviceError, setServiceError] = useState(false)
  const [loading, setLoading] = useState(true)
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const loadingMore = useRef(false)
  const restoreStarted = useRef(false)
  const selectedTaskId = useRef<string | null>(null)

  const loadBoard = useCallback(async (options?: { initial?: boolean }): Promise<void> => {
    if (options?.initial) setLoading(true)
    try {
      const response = await oratorioClient.listTasks('includeArchived=true&limit=50')
      const mapped = response.tasks.map(mapItemSummary)
      setTasks(mapped)
      setNextCursor(response.nextCursor)
      setServiceError(false)
    } catch { setServiceError(true) } finally {
      if (options?.initial) setLoading(false)
    }
  }, [])

  async function loadMore(): Promise<void> {
    if (!nextCursor || loadingMore.current) return
    loadingMore.current = true
    try {
      const response = await oratorioClient.listTasks(`includeArchived=true&limit=50&cursor=${encodeURIComponent(nextCursor)}`)
      const mapped = response.tasks.map(mapItemSummary)
      setTasks((current) => [...current, ...mapped.filter((task) => !current.some((existing) => existing.id === task.id))])
      setNextCursor(response.nextCursor)
    } finally { loadingMore.current = false }
  }

  const refreshDetail = useCallback(async (taskId: string): Promise<OratorioTask> => {
    const task = mapItemDetail(await oratorioClient.task(taskId))
    setSelectedTask(task)
    setTasks((current) => current.map((item) => item.id === task.id ? task : item))
    return task
  }, [])

  useEffect(() => {
    selectedTaskId.current = selectedTask?.id ?? null
  }, [selectedTask?.id])

  useEffect(() => {
    let active = true
    let refreshTimer: number | null = null
    void host.oratorio.getContext().then(() => { if (active) void loadBoard({ initial: true }) }).catch(() => { if (active) setServiceError(true) })
    const unsubscribe = host.oratorio.onEvent((event) => {
      if (event.type === 'board-event' && event.event?.type.startsWith('drawer/')) {
        const activity = liveActivity(event.event)
        if (event.event.runId && activity) {
          const update = (task: OratorioTask): OratorioTask => {
            if (!task.run || task.run.runId !== event.event?.runId) return task
            return { ...task, run: { ...task.run, activity } }
          }
          setTasks((current) => current.map(update))
          setSelectedTask((current) => current ? update(current) : current)
        }
      } else if (event.type === 'data-changed' || event.type === 'board-event') {
        if (refreshTimer !== null) window.clearTimeout(refreshTimer)
        refreshTimer = window.setTimeout(() => {
          refreshTimer = null
          void loadBoard()
          const taskId = selectedTaskId.current
          if (taskId) void refreshDetail(taskId).catch(() => setServiceError(true))
        }, 120)
      }
    })
    return () => {
      active = false
      if (refreshTimer !== null) window.clearTimeout(refreshTimer)
      unsubscribe()
    }
  }, [host, loadBoard, refreshDetail])

  useEffect(() => {
    const restoreId = retainedViewState.detailTaskId
    if (!restoreId || restoreStarted.current) return
    const task = tasks.find((item) => item.id === restoreId)
    if (!task) return
    restoreStarted.current = true
    void openDetail(task, retainedViewState.stage, { focus: retainedViewState.focus })
  }, [tasks])

  useEffect(() => {
    function navigate(target: OratorioNavigationTarget): void {
      if (target.kind === 'board') { retainedViewState = { ...retainedViewState, detailTaskId: null }; setSelectedTask(null); return }
      if (target.kind === 'settings') { openSettings(); return }
      const known = tasks.find((task) => task.id === target.taskId || task.shortId === target.taskId)
      if (known) void openDetail(known)
      else void oratorioClient.task(target.taskId).then((detail) => openDetail(mapItemDetail(detail))).catch(() => setServiceError(true))
    }
    const pending = consumeOratorioNavigation()
    if (pending) navigate(pending)
    return onOratorioNavigation(navigate)
  }, [tasks])

  async function openDetail(task: OratorioTask, stage: TaskStage = 'review', options?: { focus?: 'discussion' }): Promise<void> {
    retainedViewState = { ...retainedViewState, detailTaskId: task.id, stage, focus: options?.focus }
    setSelectedTask(task); setSelectedStage(stage); setSelectedFocus(options?.focus)
    try { await refreshDetail(task.id) } catch { setServiceError(true) }
  }

  function openSettings(): void {
    host.navigation.openSettingsPage('oratorio')
  }

  function openThread(task: OratorioTask): void {
    if (task.run?.threadId && task.run.workspacePath) void host.navigation.openThread(task.run.threadId, task.run.workspacePath)
  }

  async function performTaskAction(task: OratorioTask, action: QuickActionId, note?: string): Promise<OratorioTask> {
    const route = action === 'cancel-run' ? 'cancel-run' : action === 'request-changes' ? 'request-changes' : action === 're-review' ? 'rereview' : ['retry', 'implement', 'auto-target', 'review-only', 'dispatch'].includes(action) ? 'dispatch' : action
    const implementation = action === 'implement' || action === 'auto-target'
    const body = route === 'dispatch' ? { mode: 'appServer', workMode: implementation ? 'implementation' : 'reviewAnalysis', deliveryPolicy: action === 'auto-target' ? 'autoPr' : 'manualDelivery', note } : { body: note }
    const result = await oratorioClient.itemAction(task.id, route, body)
    const updated = mapItemDetail(result)
    setTasks((current) => current.map((item) => item.id === updated.id ? updated : item))
    if (selectedTask?.id === updated.id) setSelectedTask(updated)
    return updated
  }

  if (selectedTask) return <OratorioTaskDetail task={selectedTask} initialStage={selectedStage} initialFocus={selectedFocus} onStageChange={(stage) => { retainedViewState = { ...retainedViewState, stage }; setSelectedStage(stage) }} onBack={() => { retainedViewState = { ...retainedViewState, detailTaskId: null, focus: undefined }; setSelectedTask(null) }} onOpenThread={() => openThread(selectedTask)} onTaskChange={(task) => { setSelectedTask(task); setTasks((current) => current.map((item) => item.id === task.id ? task : item)) }} />

  return <>
    {serviceError ? <div className="oratorio-service-alert" role="alert">Oratorio is unavailable. <button type="button" onClick={() => void host.oratorio.retry().then(() => loadBoard({ initial: true }))}>Retry</button></div> : null}
    <OratorioBoard
      presentation={serviceError ? 'error' : loading ? 'loading' : tasks.length === 0 ? 'empty' : 'ready'}
      tasks={tasks}
      initialState={retainedViewState.board}
      onStateChange={(board) => {
        retainedViewState = { ...retainedViewState, board }
        void host.oratorio.focusRun(tasks.find((task) => task.id === board.selectedTaskId)?.run?.runId ?? null)
      }}
      onOpenSettings={openSettings}
      onOpenThread={openThread}
      onSync={async () => { const results = await Promise.allSettled(['github', 'gitlab'].map((provider) => oratorioClient.sync(provider))); if (results.every((result) => result.status === 'rejected')) throw new Error('oratorio.sync_failed'); await loadBoard() }}
      onCreateTask={async (draft) => mapItemSummary((await oratorioClient.createLocalTask(draft)).item)}
      hasMore={Boolean(nextCursor)}
      onLoadMore={loadMore}
      onReorder={async (task, column) => { const columnIndex = ['todo', 'in-progress', 'in-review', 'done'].indexOf(column); await oratorioClient.reorder({ updates: [{ taskId: task.id, sortOrder: columnIndex * 1000 }] }) }}
      onTaskAction={performTaskAction}
      onLoadTaskDetail={(task) => oratorioClient.task(task.id).then(mapItemDetail)}
      onOpenDetail={(task, stage, options) => void openDetail(task, stage, options)}
    />
  </>
}
function liveActivity(event: DesktopPluginOratorioBoardEvent): string | null {
  if (event.type === 'drawer/run/status' && ['succeeded', 'completed', 'failed', 'cancelled', 'timedOut'].includes(event.payload?.status ?? '')) return null
  if (event.type !== 'drawer/item.started' && event.type !== 'drawer/item.delta') return null
  const verb = event.payload?.type === 'agentMessage' ? 'Writing'
    : event.payload?.type === 'reasoning' || event.payload?.type === 'reasoningContent' ? 'Thinking'
      : event.payload?.type === 'commandExecution' ? 'Running command'
        : event.payload?.type === 'toolCall' ? 'Using tool' : 'Working'
  return event.payload?.text ? `${verb} · ${event.payload.text}` : verb
}
