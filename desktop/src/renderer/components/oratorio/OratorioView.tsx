import { useEffect, useRef, useState } from 'react'
import { useUIStore } from '../../stores/uiStore'
import { buildExtensionSettingsPanelKey } from '../../utils/desktopExtensionRegistry'
import { OratorioBoard, type OratorioBoardState } from './OratorioBoard'
import { oratorioClient } from './oratorio-client'
import { mapItemDetail, mapItemSummary } from './oratorio-mappers'
import type { OratorioTask, TaskStage } from './oratorio-model'
import { OratorioTaskDetail } from './OratorioTaskDetail'
import type { QuickActionId } from './oratorio-workflow'
import { consumeOratorioNavigation, type OratorioNavigationTarget } from './oratorio-navigation'
import './oratorio.css'

interface NativeExtensionHost { navigation: { openThread(threadId: string, workspacePath?: string): Promise<void> } }

const defaultBoardState: OratorioBoardState = { mode: 'active', query: '', repository: 'all', assignee: 'all', selectedTaskId: null, scrollTop: 0 }
let retainedViewState: { board: OratorioBoardState; detailTaskId: string | null; stage: TaskStage; focus?: 'discussion' } = { board: defaultBoardState, detailTaskId: null, stage: 'review' }

export function OratorioView({ host }: { host: NativeExtensionHost; viewId?: string }): JSX.Element {
  const setActiveMainView = useUIStore((state) => state.setActiveMainView)
  const setActiveSettingsTab = useUIStore((state) => state.setActiveSettingsTab)
  const [tasks, setTasks] = useState<OratorioTask[]>([])
  const [selectedTask, setSelectedTask] = useState<OratorioTask | null>(null)
  const [selectedStage, setSelectedStage] = useState<TaskStage>(retainedViewState.stage)
  const [selectedFocus, setSelectedFocus] = useState<'discussion' | undefined>(retainedViewState.focus)
  const [serviceError, setServiceError] = useState(false)
  const [loading, setLoading] = useState(true)
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const loadingMore = useRef(false)
  const restoreStarted = useRef(false)

  async function loadBoard(): Promise<void> {
    setLoading(true)
    try {
      const response = await oratorioClient.listTasks('includeArchived=true&limit=50')
      const mapped = response.tasks.map(mapItemSummary)
      setTasks(mapped)
      setNextCursor(response.nextCursor)
      setServiceError(false)
      const restoreId = retainedViewState.detailTaskId
      if (restoreId && !restoreStarted.current) {
        const task = mapped.find((item) => item.id === restoreId)
        if (task) { restoreStarted.current = true; void openDetail(task, retainedViewState.stage, { focus: retainedViewState.focus }) }
      }
    } catch { setServiceError(true) } finally { setLoading(false) }
  }

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

  useEffect(() => {
    let active = true
    void window.api.oratorio.getContext().then(() => { if (active) void loadBoard() }).catch(() => { if (active) setServiceError(true) })
    const unsubscribe = window.api.oratorio.onEvent((event) => {
      if (event.type === 'board-event' && event.event?.type.startsWith('drawer/')) {
        const activity = liveActivity(event.event)
        if (event.event.runId && activity) {
          const update = (task: OratorioTask): OratorioTask => task.run?.runId === event.event?.runId ? { ...task, run: { ...task.run, activity } } : task
          setTasks((current) => current.map(update))
          setSelectedTask((current) => current ? update(current) : current)
        }
      } else if (event.type === 'data-changed' || event.type === 'board-event') {
        void loadBoard()
        if (selectedTask) void refreshDetail(selectedTask.id)
      }
    })
    return () => { active = false; unsubscribe() }
  }, [selectedTask?.id])

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
    const listener = (event: Event): void => navigate((event as CustomEvent<OratorioNavigationTarget>).detail)
    window.addEventListener('dotcraft:oratorio-navigate', listener)
    return () => window.removeEventListener('dotcraft:oratorio-navigate', listener)
  }, [tasks])

  async function refreshDetail(taskId: string): Promise<OratorioTask> {
    const task = mapItemDetail(await oratorioClient.task(taskId))
    setSelectedTask(task)
    setTasks((current) => current.map((item) => item.id === task.id ? task : item))
    return task
  }

  async function openDetail(task: OratorioTask, stage: TaskStage = 'review', options?: { focus?: 'discussion' }): Promise<void> {
    retainedViewState = { ...retainedViewState, detailTaskId: task.id, stage, focus: options?.focus }
    setSelectedTask(task); setSelectedStage(stage); setSelectedFocus(options?.focus)
    try { await refreshDetail(task.id) } catch { setServiceError(true) }
  }

  function openSettings(): void {
    setActiveSettingsTab(buildExtensionSettingsPanelKey('oratorio', 'oratorio', 'oratorio'))
    setActiveMainView('settings')
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
    {serviceError ? <div className="oratorio-service-alert" role="alert">Oratorio is unavailable. <button type="button" onClick={() => void window.api.oratorio.retry().then(loadBoard)}>Retry</button></div> : null}
    <OratorioBoard
      presentation={serviceError ? 'error' : loading ? 'loading' : tasks.length === 0 ? 'empty' : 'ready'}
      tasks={tasks}
      initialState={retainedViewState.board}
      onStateChange={(board) => {
        retainedViewState = { ...retainedViewState, board }
        void window.api.oratorio.focusRun(tasks.find((task) => task.id === board.selectedTaskId)?.run?.runId ?? null)
      }}
      onOpenSettings={openSettings}
      onOpenThread={openThread}
      onSync={async () => { const results = await Promise.allSettled(['github', 'gitlab'].map((provider) => oratorioClient.sync(provider))); if (results.every((result) => result.status === 'rejected')) throw new Error('oratorio.sync_failed'); await loadBoard() }}
      onCreateTask={async (draft) => mapItemSummary((await oratorioClient.createLocalTask(draft)).item)}
      hasMore={Boolean(nextCursor)}
      onLoadMore={loadMore}
      onReorder={async (task, column) => { const columnIndex = ['todo', 'in-progress', 'in-review', 'done'].indexOf(column); await oratorioClient.reorder({ updates: [{ taskId: task.id, sortOrder: columnIndex * 1000 }] }) }}
      onTaskAction={performTaskAction}
      onOpenDetail={(task, stage, options) => void openDetail(task, stage, options)}
    />
  </>
}

function liveActivity(event: NonNullable<import('../../../shared/oratorio').OratorioServiceEvent['event']>): string | null {
  if (event.type === 'drawer/run/status' && ['succeeded', 'completed', 'failed', 'cancelled', 'timedOut'].includes(event.payload?.status ?? '')) return null
  if (event.type !== 'drawer/item.started' && event.type !== 'drawer/item.delta') return null
  const verb = event.payload?.type === 'agentMessage' ? 'Writing'
    : event.payload?.type === 'reasoning' || event.payload?.type === 'reasoningContent' ? 'Thinking'
      : event.payload?.type === 'commandExecution' ? 'Running command'
        : event.payload?.type === 'toolCall' ? 'Using tool' : 'Working'
  return event.payload?.text ? `${verb} · ${event.payload.text}` : verb
}
