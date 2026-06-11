/**
 * Seeds the real Desktop Zustand stores with canned demo data and keeps the
 * conversation/detail panels in sync when the user switches threads in the
 * sidebar. This replaces the App.tsx orchestration layer (which talks to the
 * AppServer) with a deterministic, local controller.
 */
import { useThreadStore } from '@renderer/stores/threadStore'
import { useConversationStore } from '@renderer/stores/conversationStore'
import { useConnectionStore } from '@renderer/stores/connectionStore'
import { useWorkspaceProjectsStore } from '@renderer/stores/workspaceProjectsStore'
import { useUIStore } from '@renderer/stores/uiStore'
import type { Thread, ThreadSummary } from '@renderer/types/thread'
import { getDemoThreads, planToMarkdown, type DemoThread } from './data/demoThreads'
import { DEMO_WORKSPACE_NAME, DEMO_WORKSPACE_PATH, demoLocale } from './mockApi'

const demoThreads = getDemoThreads(demoLocale)
const threadsById = new Map(demoThreads.map((thread) => [thread.id, thread]))

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
    configuration: { mode: thread.mode, model: 'claude-fable-5' },
    turns: [],
    contextUsage: thread.contextUsage
  }
}

function loadThread(thread: DemoThread): void {
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

  // Per-thread detail tabs: Changes always, Plan only for plan threads.
  const ui = useUIStore.getState()
  if (thread.plan) {
    ui.setActiveDetailTab('changes')
    ui.setActiveDetailTab('plan')
  } else {
    ui.closeSystemTab('plan')
    ui.setActiveDetailTab('changes')
  }
}

export function bootstrapDemo(): void {
  useConnectionStore.getState().setStatus({
    status: 'connected',
    serverInfo: { name: 'DotCraft AppServer', version: 'web-demo', protocolVersion: '1.0' },
    capabilities: {
      threadManagement: true,
      approvalFlow: true,
      modeSwitch: true,
      modelCatalogManagement: true,
      threadGoals: true,
      manualCompaction: true
    }
  })

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
        threads: []
      }
    ]
  })

  const ui = useUIStore.getState()
  ui.setDetailPanelVisible(true)

  // The demo only models the conversation surface; keep nav clicks
  // (Channels / Plugins / Settings) from switching to unpopulated views.
  useUIStore.setState({ setActiveMainView: () => {} } as Parameters<typeof useUIStore.setState>[0])

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
