import { useThreadStore } from '@renderer/stores/threadStore'
import { useConversationStore } from '@renderer/stores/conversationStore'
import { openThreadState } from '@renderer/stores/threadState'
import { activateWorkspaceResources, refreshWorkspaceResources } from '@renderer/stores/providerConnectionScope'
import { useModelCatalogStore } from '@renderer/stores/modelCatalogStore'
import { useConnectionStore } from '@renderer/stores/connectionStore'
import { useWorkspaceProjectsStore } from '@renderer/stores/workspaceProjectsStore'
import { useUIStore } from '@renderer/stores/uiStore'
import { useDetailPanelStore } from '@renderer/stores/detailPanelStore'
import type { Thread, ThreadSummary } from '@renderer/types/thread'
import { getDemoProjects } from './data/demoProjects'
import { getDemoThreads, planToMarkdown, type DemoThread } from './data/demoThreads'
import { demoStart } from './demoStart'
import {
  DEMO_MODEL,
  DEMO_PROVIDER_ID,
  DEMO_REASONING,
  DEMO_WORKSPACE_NAME,
  DEMO_WORKSPACE_PATH,
  demoLocale,
  connectionStatusPayload,
  threadConfigurations
} from './mockApi'

const demoThreads = getDemoThreads(demoLocale)
const threadsById = new Map(demoThreads.map((thread) => [thread.id, thread]))
for (const thread of demoThreads) {
  threadConfigurations.set(thread.id, {
    mode: thread.mode,
    providerId: DEMO_PROVIDER_ID,
    model: DEMO_MODEL,
    reasoning: { ...DEMO_REASONING }
  })
}

function toSummary(thread: DemoThread): ThreadSummary {
  return {
    id: thread.id,
    workspacePath: DEMO_WORKSPACE_PATH,
    displayName: thread.displayName,
    status: 'active',
    originChannel: 'dotcraft-desktop',
    createdAt: thread.createdAt,
    lastActiveAt: thread.lastActiveAt
  }
}

function toThread(thread: DemoThread): Thread {
  return {
    ...toSummary(thread),
    workspacePath: DEMO_WORKSPACE_PATH,
    userId: 'local',
    metadata: {},
    configuration: { ...threadConfigurations.get(thread.id) },
    turns: [],
    contextUsage: thread.contextUsage
  }
}

function loadThread(thread: DemoThread): void {
  openThreadState(thread.id)
  const conversation = useConversationStore.getState()
  conversation.reset()

  const fresh = useConversationStore.getState()
  fresh.setWorkspacePath(DEMO_WORKSPACE_PATH)
  fresh.setThreadMode(thread.mode)
  fresh.setTurns(thread.turns)
  fresh.setContextUsage(thread.contextUsage)
  if (thread.plan) {
    fresh.onPlanUpdated({ ...thread.plan, content: planToMarkdown(thread.plan) })
  }

  useThreadStore.getState().setActiveThread(toThread(thread))

  const panel = useDetailPanelStore.getState()
  panel.onWorkspaceSwitched(DEMO_WORKSPACE_PATH)
  panel.onThreadSwitched(thread.id)
  panel.setActiveDetailTab('changes')
  if (thread.plan) panel.setActiveDetailTab('plan')
}

export async function bootstrapDemo(): Promise<void> {
  useConnectionStore.getState().setStatus(connectionStatusPayload)
  activateWorkspaceResources({
    projectKey: DEMO_WORKSPACE_PATH,
    connectionKey: connectionStatusPayload.connectionKey,
    connectionId: connectionStatusPayload.connectionId
  })
  await refreshWorkspaceResources()
  const catalog = await useModelCatalogStore.getState().loadIfNeeded(false, DEMO_PROVIDER_ID)
  if (catalog?.status !== 'ready') throw new Error('Demo model catalog failed to load.')

  useWorkspaceProjectsStore.getState().setPayload({
    foregroundWorkspacePath: DEMO_WORKSPACE_PATH,
    foregroundProjectId: DEMO_WORKSPACE_PATH,
    secondaryLimit: 8,
    projects: [
      {
        projectId: DEMO_WORKSPACE_PATH,
        kind: 'local',
        path: DEMO_WORKSPACE_PATH,
        name: DEMO_WORKSPACE_NAME,
        state: 'foreground',
        running: true,
        loaded: true,
        threadCount: demoThreads.length,
        threads: [],
        pinned: false
      },
      ...(demoStart === 'several-projects' ? getDemoProjects(demoLocale) : [])
    ]
  })

  const ui = useUIStore.getState()

  // The demo models the conversation surface, plus Settings when a page starts
  // there; keep other nav clicks from switching to unpopulated views.
  const setActiveMainView = ui.setActiveMainView
  const reachableViews = new Set<string>(demoStart === 'token-activity' ? ['conversation', 'settings'] : [])
  useUIStore.setState({
    setActiveMainView: (view) => {
      if (reachableViews.has(view)) setActiveMainView(view)
    }
  })
  if (demoStart === 'token-activity') {
    setActiveMainView('settings')
    ui.setActiveSettingsTab('profile')
  }

  const threadStore = useThreadStore.getState()
  threadStore.setThreadList(demoThreads.map(toSummary), DEMO_WORKSPACE_PATH)
  threadStore.setActiveThreadId(demoThreads[0].id)
  loadThread(demoThreads[0])

  // Zustand notifies synchronously on set, so canned turns land in the
  // conversation store before React re-renders the switched thread.
  useThreadStore.subscribe((state, prev) => {
    if (state.activeThreadId === prev.activeThreadId) return
    if (state.activeThreadId) {
      const next = threadsById.get(state.activeThreadId)
      if (next) loadThread(next)
      return
    }
    // "New chat" and similar actions clear the selection; the demo has no
    // empty-thread flow, so snap back to the previously active thread.
    const restoreId = prev.activeThreadId ?? demoThreads[0].id
    queueMicrotask(() => {
      useThreadStore.getState().setActiveThreadId(restoreId)
    })
  })
}
