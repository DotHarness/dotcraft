import { useUIStore, type ActiveMainView, type AutomationsTab, type SystemDetailTab } from '../stores/uiStore'
import { useThreadStore } from '../stores/threadStore'
import { useConversationStore } from '../stores/conversationStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useAutomationsStore } from '../stores/automationsStore'
import { useCronStore } from '../stores/cronStore'
import { usePluginStore } from '../stores/pluginStore'
import { useSkillsStore } from '../stores/skillsStore'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import { useProvidersStore } from '../stores/providersStore'
import { useMcpStore } from '../stores/mcpStore'
import { useSubAgentStore } from '../stores/subAgentStore'
import type { SettingsTab } from '../types/settings'

/**
 * Renderer automation bridge for screenshot/E2E tooling.
 *
 * Exposes two window globals so an out-of-process Playwright driver can navigate
 * to any surface and read store state without depending on localized button text:
 * - `window.__DOTCRAFT_STORES.<name>()` — read-only `getState()` snapshots.
 * - `window.__DOTCRAFT_E2E.<action>()` — locale-independent navigation actions.
 *
 * Installed unconditionally (including production `electron-vite build` output) so
 * the harness can drive packaged builds; the actions are the same ones the UI's own
 * controls call, and the main renderer hosts no untrusted content.
 */

export interface DotCraftStoreRegistry {
  ui: () => ReturnType<typeof useUIStore.getState>
  thread: () => ReturnType<typeof useThreadStore.getState>
  conversation: () => ReturnType<typeof useConversationStore.getState>
  connection: () => ReturnType<typeof useConnectionStore.getState>
  automations: () => ReturnType<typeof useAutomationsStore.getState>
  cron: () => ReturnType<typeof useCronStore.getState>
  plugin: () => ReturnType<typeof usePluginStore.getState>
  skills: () => ReturnType<typeof useSkillsStore.getState>
  modelCatalog: () => ReturnType<typeof useModelCatalogStore.getState>
  providers: () => ReturnType<typeof useProvidersStore.getState>
  mcp: () => ReturnType<typeof useMcpStore.getState>
  subAgent: () => ReturnType<typeof useSubAgentStore.getState>
}

export interface DotCraftE2EBridge {
  /** Switch the primary center surface. */
  setMainView: (view: ActiveMainView) => void
  /** Current primary surface. */
  getMainView: () => ActiveMainView
  /** Open Settings on a specific section. */
  setSettingsTab: (tab: SettingsTab) => void
  /** Open Automations on a specific tab (tasks/cron). */
  setAutomationsTab: (tab: AutomationsTab) => void
  /** Reveal the detail panel and switch to a system tab. */
  setDetailTab: (tab: SystemDetailTab) => void
  /** Show or hide the detail panel. */
  setDetailPanelVisible: (visible: boolean) => void
  /** Open a thread by id in the conversation surface. */
  openThread: (threadId: string) => void
  /** Deselect the current thread and show the welcome composer. */
  goToNewChat: () => void
  /** AppServer connection status (e.g. 'connected'). */
  connectionStatus: () => string
  /** Negotiated server capabilities, or null before connect. */
  capabilities: () => unknown
  /** Lightweight thread list snapshot for navigation targeting. */
  listThreads: () => Array<{ id: string; name: string | null; status: string }>
}

declare global {
  interface Window {
    __DOTCRAFT_STORES?: DotCraftStoreRegistry
    __DOTCRAFT_E2E?: DotCraftE2EBridge
  }
}

export function installAutomationBridge(): void {
  if (typeof window === 'undefined') return

  const stores: DotCraftStoreRegistry = {
    ui: () => useUIStore.getState(),
    thread: () => useThreadStore.getState(),
    conversation: () => useConversationStore.getState(),
    connection: () => useConnectionStore.getState(),
    automations: () => useAutomationsStore.getState(),
    cron: () => useCronStore.getState(),
    plugin: () => usePluginStore.getState(),
    skills: () => useSkillsStore.getState(),
    modelCatalog: () => useModelCatalogStore.getState(),
    providers: () => useProvidersStore.getState(),
    mcp: () => useMcpStore.getState(),
    subAgent: () => useSubAgentStore.getState()
  }

  const e2e: DotCraftE2EBridge = {
    setMainView(view) {
      useUIStore.getState().setActiveMainView(view)
    },
    getMainView() {
      return useUIStore.getState().activeMainView
    },
    setSettingsTab(tab) {
      const ui = useUIStore.getState()
      ui.setActiveMainView('settings')
      ui.setActiveSettingsTab(tab)
    },
    setAutomationsTab(tab) {
      const ui = useUIStore.getState()
      ui.setActiveMainView('automations')
      ui.setAutomationsTab(tab)
    },
    setDetailTab(tab) {
      useUIStore.getState().setActiveDetailTab(tab)
    },
    setDetailPanelVisible(visible) {
      useUIStore.getState().setDetailPanelVisible(visible)
    },
    openThread(threadId) {
      useUIStore.getState().setActiveMainView('conversation')
      useThreadStore.getState().setActiveThreadId(threadId)
    },
    goToNewChat() {
      useUIStore.getState().goToNewChat()
    },
    connectionStatus() {
      return useConnectionStore.getState().status
    },
    capabilities() {
      return useConnectionStore.getState().capabilities
    },
    listThreads() {
      return useThreadStore.getState().threadList.map((thread) => ({
        id: thread.id,
        name: thread.displayName ?? null,
        status: thread.status
      }))
    }
  }

  window.__DOTCRAFT_STORES = stores
  window.__DOTCRAFT_E2E = e2e
}
