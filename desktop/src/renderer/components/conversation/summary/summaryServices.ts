import type {
  DesktopPluginProjectActionsSnapshot,
  DesktopPluginSavedProjectAction,
  DesktopPluginSubAgent
} from '@dotcraft/plugin'
import type { ClientRequestMethods, ServerNotificationMethods } from '@dotcraft/sdk/contracts'

import {
  listDesktopPluginProjectActions,
  onDesktopPluginProjectActionsChange,
  openDesktopPluginProjectTerminal,
  promptDesktopPluginProjectAction,
  runDesktopPluginProjectAction,
  selectDesktopPluginProjectEnvironment
} from '../../../plugins/desktopPluginProjectActions'
import { listDesktopPluginSubAgents, onDesktopPluginSubAgentsChange } from '../../../plugins/desktopPluginSubAgents'
import { useConversationStore } from '../../../stores/conversationStore'
import { showToast } from '../../../stores/toastStore'
import { useUIStore, type SystemDetailTab } from '../../../stores/uiStore'
import { openActiveThreadFile, openAutomation, openExternalUrl, revealToolCall } from '../../../utils/desktopNavigation'
import { openSkill } from '../../../utils/openSkill'
import { loadLocalImageDataUrl } from '../imagePreview/galleryImages'

export interface SummaryServices {
  readonly appServer: {
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
    select(workspacePath: string, environmentId: string): Promise<void>
    run(request: { threadId: string; environmentId: string; actionId: string }): Promise<void>
    openTerminal(request: { threadId: string }): Promise<void>
    promptAddAction(request: { workspacePath: string; environmentId?: string }): Promise<DesktopPluginSavedProjectAction | null>
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
  }
  readonly ui: {
    showError(message: string): void
  }
}

export const desktopSummaryServices: SummaryServices = {
  appServer: {
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
    promptAddAction: (request) => promptDesktopPluginProjectAction(request, () => () => undefined)
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
    }
  },
  ui: {
    showError: (message) => {
      showToast({ message, type: 'error' })
    }
  }
}
