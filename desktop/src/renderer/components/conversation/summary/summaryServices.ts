import type {
  DesktopPluginProjectActionsSnapshot,
  DesktopPluginSavedProjectAction,
  DesktopPluginSubAgent
} from '@dotcraft/plugin'
import type { ClientRequestMethods, ServerNotificationMethods } from '@dotcraft/sdk/contracts'

import { translate } from '../../../../shared/locales'
import { readDesktopPluginEnvironment } from '../../../plugins/desktopPluginEnvironment'
import {
  listDesktopPluginProjectActions,
  onDesktopPluginProjectActionsChange,
  openDesktopPluginProjectSettings,
  openDesktopPluginProjectTerminal,
  promptDesktopPluginProjectAction,
  runDesktopPluginProjectAction,
  selectDesktopPluginProjectEnvironment
} from '../../../plugins/desktopPluginProjectActions'
import { listDesktopPluginSubAgents, onDesktopPluginSubAgentsChange } from '../../../plugins/desktopPluginSubAgents'
import {
  dismissEnvironmentSetupTip,
  environmentSetupTipDismissed,
  onEnvironmentSetupTipDismissedChange
} from '../../projectActions/environmentSetupTip'
import { requestSetUpProjectDialog } from '../../projectActions/SetUpProjectDialog'
import {
  canSetUpWithAgent,
  onSetUpWithAgentAvailabilityChange,
  setUpEnvironmentWithAgent
} from '../../projectActions/setUpWithAgent'
import { useConnectionStore } from '../../../stores/connectionStore'
import { useConversationStore, type ContextUsage } from '../../../stores/conversationStore'
import { useThreadStore } from '../../../stores/threadStore'
import { useWorkspaceProjectsStore } from '../../../stores/workspaceProjectsStore'
import { showToast } from '../../../stores/toastStore'
import { useUIStore, type SystemDetailTab } from '../../../stores/uiStore'
import { openActiveThreadFile, openAutomation, openExternalUrl, revealToolCall } from '../../../utils/desktopNavigation'
import { compactThreadContext } from '../../../utils/manualCompaction'
import { openSkill } from '../../../utils/openSkill'
import { loadLocalImageDataUrl } from '../imagePreview/galleryImages'
import { viewerRootsFor } from '../../../utils/viewerRoots'
import {
  onThreadWorktreeEnvironmentChange,
  setThreadWorktreeEnvironment,
  threadWorktreeEnvironment,
  type ThreadWorktreeEnvironment
} from './worktreeEnvironment'

export interface SummaryContextState {
  readonly usage: ContextUsage | null
  readonly running: boolean
  readonly canCompact: boolean
}

export interface SummaryServices {
  readonly appServer: {
    connectionKey(): string
    onConnectionChange(listener: (connected: boolean) => void): () => void
    request<M extends keyof ClientRequestMethods>(
      method: M,
      params: ClientRequestMethods[M]['params']
    ): Promise<ClientRequestMethods[M]['result']>
    onNotification<M extends keyof ServerNotificationMethods>(
      method: M,
      listener: (params: ServerNotificationMethods[M]['params']) => void
    ): () => void
  }
  readonly subagents: {
    list(parentThreadId: string): readonly DesktopPluginSubAgent[]
    onChange(parentThreadId: string, listener: (subagents: readonly DesktopPluginSubAgent[]) => void): () => void
  }
  readonly projectActions: {
    list(workspacePath: string): Promise<DesktopPluginProjectActionsSnapshot>
    onChange(workspacePath: string, listener: (snapshot: DesktopPluginProjectActionsSnapshot) => void): () => void
    select(workspacePath: string, environmentId: string | null): Promise<void>
    run(request: { threadId: string; environmentId: string; actionId: string }): Promise<void>
    openTerminal(request: { threadId: string }): Promise<void>
    promptAddAction(request: { workspacePath: string; environmentId?: string }): Promise<DesktopPluginSavedProjectAction | null>
    openSettings(request: { workspacePath: string; environmentId?: string }): Promise<void>
  }
  readonly environmentSetup: {
    agentAvailable(threadId: string): boolean
    onAgentAvailableChange(threadId: string, listener: (available: boolean) => void): () => void
    setUpWithAgent(request: { threadId: string; workspacePath: string }): Promise<void>
    setUpManually(request: { threadId: string; workspacePath: string }): Promise<void>
    tipDismissed(): boolean
    onTipDismissedChange(listener: (dismissed: boolean) => void): () => void
    dismissTip(): void
  }
  readonly worktrees: {
    environment(threadId: string): ThreadWorktreeEnvironment | null
    onEnvironmentChange(threadId: string, listener: (environment: ThreadWorktreeEnvironment | null) => void): () => void
    setEnvironment(request: { threadId: string; environmentPath: string | null }): Promise<void>
  }
  readonly context: {
    state(threadId: string): SummaryContextState
    onChange(threadId: string, listener: (state: SummaryContextState) => void): () => void
    compact(threadId: string): Promise<void>
  }
  readonly navigation: {
    openDetailPanel(tab: SystemDetailTab): void
    openFile(path: string): Promise<void>
    openExternal(url: string): Promise<void>
    openSkill(name: string): void
    openAutomation(automationId: string): void
    revealToolCall(threadId: string, callId: string): void
  }
  readonly workspaces: {
    readImageAttachment(path: string): Promise<string>
    roots(workspacePath: string): string[]
  }
  readonly ui: {
    showError(message: string): void
  }
}

export const desktopSummaryServices: SummaryServices = {
  appServer: {
    connectionKey: () => useWorkspaceProjectsStore.getState().foregroundProjectId,
    onConnectionChange(listener) {
      const stopConnection = useConnectionStore.subscribe((next, previous) => {
        if (next.connectionEpoch !== previous.connectionEpoch) listener(next.status === 'connected')
      })
      const stopProject = useWorkspaceProjectsStore.subscribe((next, previous) => {
        if (next.foregroundProjectId !== previous.foregroundProjectId) listener(false)
      })
      return () => { stopConnection(); stopProject() }
    },
    request(method, params) {
      return window.api.appServer.sendRequestRaw(method, params) as never
    },
    onNotification(method, listener) {
      return window.api.appServer.onNotificationRaw((notification) => {
        if (notification.method === method) listener(notification.params as never)
      })
    }
  },
  subagents: {
    list: listDesktopPluginSubAgents,
    onChange: onDesktopPluginSubAgentsChange
  },
  projectActions: {
    list: listDesktopPluginProjectActions,
    onChange: onDesktopPluginProjectActionsChange,
    select: selectDesktopPluginProjectEnvironment,
    run: runDesktopPluginProjectAction,
    openTerminal: openDesktopPluginProjectTerminal,
    promptAddAction: (request) => promptDesktopPluginProjectAction(request, () => () => undefined),
    openSettings: openDesktopPluginProjectSettings
  },
  environmentSetup: {
    agentAvailable: canSetUpWithAgent,
    onAgentAvailableChange: onSetUpWithAgentAvailabilityChange,
    setUpWithAgent: (request) => setUpEnvironmentWithAgent({
      ...request,
      t: (key, vars) => translate(readDesktopPluginEnvironment().locale, key, vars)
    }),
    setUpManually: async (request) => {
      await requestSetUpProjectDialog(request).result
    },
    tipDismissed: environmentSetupTipDismissed,
    onTipDismissedChange: onEnvironmentSetupTipDismissedChange,
    dismissTip: dismissEnvironmentSetupTip
  },
  worktrees: {
    environment: threadWorktreeEnvironment,
    onEnvironmentChange: onThreadWorktreeEnvironmentChange,
    setEnvironment: setThreadWorktreeEnvironment
  },
  context: {
    state: contextState,
    onChange(threadId, listener) {
      let last = contextState(threadId)
      const notify = (): void => {
        const next = contextState(threadId)
        if (next.usage === last.usage && next.running === last.running && next.canCompact === last.canCompact) return
        last = next
        listener(next)
      }
      const stops = [useConversationStore.subscribe(notify), useConnectionStore.subscribe(notify), useThreadStore.subscribe(notify)]
      return () => stops.forEach((stop) => stop())
    },
    async compact(threadId) {
      await compactThreadContext(threadId, (key, vars) => translate(readDesktopPluginEnvironment().locale, key, vars))
    }
  },
  navigation: {
    openDetailPanel: (tab) => useUIStore.getState().setActiveDetailTab(tab),
    openFile: openActiveThreadFile,
    openExternal: openExternalUrl,
    openSkill,
    openAutomation,
    revealToolCall
  },
  workspaces: {
    async readImageAttachment(path) {
      if (useConversationStore.getState().remoteWorkspaceActive) {
        throw new Error('Image attachments are unavailable in a remote workspace.')
      }
      return loadLocalImageDataUrl(path)
    },
    roots: viewerRootsFor
  },
  ui: {
    showError: (message) => {
      showToast({ message, type: 'error' })
    }
  }
}

function contextState(threadId: string): SummaryContextState {
  const conversation = useConversationStore.getState()
  if (useThreadStore.getState().activeThreadId !== threadId) return { usage: null, running: false, canCompact: false }
  const idle = conversation.turnStatus === 'idle' && conversation.maintenanceKind == null
  return {
    usage: conversation.contextUsage,
    running: !idle,
    canCompact: idle && conversation.turns.length > 0 && useConnectionStore.getState().capabilities?.manualCompaction === true
  }
}
