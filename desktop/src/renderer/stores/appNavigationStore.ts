import { create } from 'zustand'

import type {
  ActiveDetailTab,
  AutomationsTab,
  DesktopPluginMainView,
  PluginCatalogSurface,
  SelectedChannelKey
} from './uiStore'
import type { SettingsTab } from '../types/settings'
import { useUIStore } from './uiStore'
import { useThreadStore } from './threadStore'
import { usePluginStore } from './pluginStore'
import { useSkillsStore } from './skillsStore'
import { useAutomationsStore } from './automationsStore'
import { useCronStore } from './cronStore'
import { useViewerTabStore } from './viewerTabStore'
import {
  findDesktopPluginMainView,
  useDesktopPluginRegistry
} from '../plugins/desktopPluginRegistry'

const MAX_HISTORY_ENTRIES = 100

interface ConversationNavigationLocation {
  kind: 'conversation'
  threadId: string | null
  detailVisible: boolean
  activeDetailTab: ActiveDetailTab
  selectedChangedFile: string | null
}

interface SettingsNavigationLocation {
  kind: 'settings'
  tab: SettingsTab
}

interface CatalogNavigationLocation {
  kind: 'catalog'
  surface: PluginCatalogSurface
  selection: { kind: 'plugin' | 'skill'; id: string } | null
}

interface AutomationsNavigationLocation {
  kind: 'automations'
  tab: AutomationsTab
  selection: { kind: 'task' | 'cron'; id: string } | null
}

interface ChannelsNavigationLocation {
  kind: 'channels'
  selection: SelectedChannelKey
}

interface AgentsNavigationLocation {
  kind: 'agents'
}

interface DesktopPluginNavigationLocation {
  kind: 'desktopPlugin'
  view: DesktopPluginMainView
}

export type AppNavigationLocation =
  | ConversationNavigationLocation
  | SettingsNavigationLocation
  | CatalogNavigationLocation
  | AutomationsNavigationLocation
  | ChannelsNavigationLocation
  | AgentsNavigationLocation
  | DesktopPluginNavigationLocation

export interface NavigationHistoryState {
  entries: AppNavigationLocation[]
  index: number
  canGoBack: boolean
  canGoForward: boolean
}

interface AppNavigationHistoryStore extends NavigationHistoryState {
  push(location: AppNavigationLocation): void
  replaceCurrent(location: AppNavigationLocation): void
  goBack(): void
  goForward(): void
  reset(location?: AppNavigationLocation): void
}

let recordingSuppressionDepth = 0
let flushScheduled = false
let flushGeneration = 0
let activeWorkspaceKey: string | null = null
let unsubscribeSources: Array<() => void> = []

function sameLocation(left: AppNavigationLocation, right: AppNavigationLocation): boolean {
  return JSON.stringify(left) === JSON.stringify(right)
}

function historyState(entries: AppNavigationLocation[], index: number): NavigationHistoryState {
  return {
    entries,
    index,
    canGoBack: index > 0,
    canGoForward: index >= 0 && index < entries.length - 1
  }
}

export const useAppNavigationStore = create<AppNavigationHistoryStore>((set, get) => ({
  ...historyState([], -1),

  push(location) {
    const state = get()
    const current = state.entries[state.index]
    if (current && sameLocation(current, location)) return

    const branched = state.entries.slice(0, state.index + 1)
    branched.push(location)
    const entries = branched.slice(-MAX_HISTORY_ENTRIES)
    set(historyState(entries, entries.length - 1))
  },

  replaceCurrent(location) {
    const state = get()
    if (state.index < 0) {
      set(historyState([location], 0))
      return
    }
    const entries = [...state.entries]
    entries[state.index] = location
    set(historyState(entries, state.index))
  },

  goBack() {
    moveHistory(-1)
  },

  goForward() {
    moveHistory(1)
  },

  reset(location) {
    set(location ? historyState([location], 0) : historyState([], -1))
  }
}))

export function captureAppNavigationLocation(): AppNavigationLocation {
  const ui = useUIStore.getState()
  const activeThreadId = useThreadStore.getState().activeThreadId

  if (ui.activeMainView === 'conversation') {
    return {
      kind: 'conversation',
      threadId: activeThreadId,
      detailVisible: ui.detailPanelPreferredVisible,
      activeDetailTab: ui.activeDetailTab,
      selectedChangedFile: ui.selectedChangedFile
    }
  }

  if (ui.activeMainView === 'settings') {
    return { kind: 'settings', tab: ui.activeSettingsTab }
  }

  if (ui.activeMainView === 'skills') {
    const pluginId = usePluginStore.getState().selectedPluginId
    const skillName = useSkillsStore.getState().selectedSkillName
    const selection = ui.pluginCatalogSurface === 'plugins'
      ? pluginId ? { kind: 'plugin' as const, id: pluginId } : null
      : skillName ? { kind: 'skill' as const, id: skillName } : null
    return { kind: 'catalog', surface: ui.pluginCatalogSurface, selection }
  }

  if (ui.activeMainView === 'automations') {
    const taskId = useAutomationsStore.getState().selectedTaskId
    const cronId = useCronStore.getState().selectedCronJobId
    const selection = ui.automationsTab === 'tasks'
      ? taskId ? { kind: 'task' as const, id: taskId } : null
      : cronId ? { kind: 'cron' as const, id: cronId } : null
    return { kind: 'automations', tab: ui.automationsTab, selection }
  }

  if (ui.activeMainView === 'channels') {
    return { kind: 'channels', selection: ui.selectedChannelKey }
  }

  if (ui.activeMainView === 'agents') {
    return { kind: 'agents' }
  }

  return { kind: 'desktopPlugin', view: ui.activeMainView }
}

export function runWithoutAppNavigationRecording<T>(operation: () => T): T {
  recordingSuppressionDepth += 1
  try {
    return operation()
  } finally {
    recordingSuppressionDepth -= 1
  }
}

export function replaceCurrentAppNavigationLocation(): void {
  if (activeWorkspaceKey == null) return
  useAppNavigationStore.getState().replaceCurrent(captureAppNavigationLocation())
}

export function startAppNavigationHistory(workspaceKey: string): () => void {
  stopAppNavigationHistory(false)
  activeWorkspaceKey = workspaceKey
  useUIStore.getState().setSelectedChannelKey(null)
  useAppNavigationStore.getState().reset(captureAppNavigationLocation())

  const schedule = (): void => {
    if (recordingSuppressionDepth > 0 || flushScheduled || activeWorkspaceKey == null) return
    flushScheduled = true
    const generation = flushGeneration
    queueMicrotask(() => {
      flushScheduled = false
      if (generation !== flushGeneration || recordingSuppressionDepth > 0 || activeWorkspaceKey == null) return
      useAppNavigationStore.getState().push(captureAppNavigationLocation())
    })
  }

  unsubscribeSources = [
    useUIStore.subscribe(schedule),
    useThreadStore.subscribe(schedule),
    usePluginStore.subscribe(schedule),
    useSkillsStore.subscribe(schedule),
    useAutomationsStore.subscribe(schedule),
    useCronStore.subscribe(schedule),
    useViewerTabStore.subscribe(schedule),
    useDesktopPluginRegistry.subscribe(schedule)
  ]

  return () => {
    if (activeWorkspaceKey === workspaceKey) stopAppNavigationHistory(true)
  }
}

export function stopAppNavigationHistory(reset = true): void {
  for (const unsubscribe of unsubscribeSources) unsubscribe()
  unsubscribeSources = []
  activeWorkspaceKey = null
  flushScheduled = false
  flushGeneration += 1
  if (reset) useAppNavigationStore.getState().reset()
}

function moveHistory(direction: -1 | 1): void {
  flushGeneration += 1
  flushScheduled = false
  const store = useAppNavigationStore.getState()
  let targetIndex = store.index + direction

  while (targetIndex >= 0 && targetIndex < store.entries.length) {
    const target = normalizeLocation(store.entries[targetIndex])
    if (target) {
      runWithoutAppNavigationRecording(() => restoreLocation(target))
      useAppNavigationStore.setState(historyState(store.entries, targetIndex))
      return
    }
    targetIndex += direction
  }
}

function normalizeLocation(location: AppNavigationLocation): AppNavigationLocation | null {
  if (location.kind === 'conversation') {
    if (
      location.threadId != null &&
      !useThreadStore.getState().threadList.some((thread) => thread.id === location.threadId)
    ) {
      return null
    }
    if (location.activeDetailTab.kind === 'viewer') {
      if (!location.threadId) return { ...location, activeDetailTab: { kind: 'launcher' }, detailVisible: false }
      const viewerTabId = location.activeDetailTab.id
      const viewerExists = useViewerTabStore
        .getState()
        .getThreadState(location.threadId)
        .tabs
        .some((tab) => tab.id === viewerTabId)
      if (!viewerExists) return null
    }
    return location
  }

  if (location.kind === 'catalog' && location.selection) {
    if (location.selection.kind === 'plugin') {
      const exists = usePluginStore.getState().plugins.some((plugin) => plugin.id === location.selection?.id)
      return exists ? location : { ...location, selection: null }
    }
    const exists = useSkillsStore.getState().skills.some((skill) => skill.name === location.selection?.id)
    return exists ? location : { ...location, selection: null }
  }

  if (location.kind === 'automations' && location.selection) {
    if (location.selection.kind === 'task') {
      const exists = useAutomationsStore.getState().tasks.some((task) => task.id === location.selection?.id)
      return exists ? location : { ...location, selection: null }
    }
    const exists = useCronStore.getState().jobs.some((job) => job.id === location.selection?.id)
    return exists ? location : { ...location, selection: null }
  }

  if (
    location.kind === 'desktopPlugin'
    && findDesktopPluginMainView(location.view) == null
  ) {
    return null
  }

  return location
}

function restoreLocation(location: AppNavigationLocation): void {
  const ui = useUIStore.getState()

  if (location.kind === 'conversation') {
    useThreadStore.getState().setActiveThreadId(location.threadId)
    ui.setActiveMainView('conversation')
    ui.selectChangedFile(location.selectedChangedFile)
    if (location.activeDetailTab.kind === 'system') {
      ui.setActiveDetailTab(location.activeDetailTab.id, { reveal: false })
    } else if (location.activeDetailTab.kind === 'viewer' && location.threadId) {
      useViewerTabStore.getState().setActiveTab(location.threadId, location.activeDetailTab.id)
      ui.setActiveViewerTab(location.activeDetailTab.id, { reveal: false })
    } else {
      useUIStore.setState({ activeDetailTab: { kind: 'launcher' } })
    }
    ui.setDetailPanelVisible(location.detailVisible)
    return
  }

  if (location.kind === 'settings') {
    ui.setActiveSettingsTab(location.tab)
    ui.setActiveMainView('settings')
    return
  }

  if (location.kind === 'catalog') {
    ui.setPluginCatalogSurface(location.surface)
    ui.setActiveMainView('skills')
    usePluginStore.getState().clearSelection()
    useSkillsStore.getState().clearSelection()
    if (location.selection?.kind === 'plugin') {
      void usePluginStore.getState().selectPlugin(location.selection.id)
    } else if (location.selection?.kind === 'skill') {
      void useSkillsStore.getState().selectSkill(location.selection.id)
    }
    return
  }

  if (location.kind === 'automations') {
    ui.setAutomationsTab(location.tab)
    ui.setActiveMainView('automations')
    useAutomationsStore.getState().selectTask(location.selection?.kind === 'task' ? location.selection.id : null)
    useCronStore.getState().selectCronJob(location.selection?.kind === 'cron' ? location.selection.id : null)
    return
  }

  if (location.kind === 'channels') {
    ui.setSelectedChannelKey(location.selection)
    ui.setActiveMainView('channels')
    return
  }

  if (location.kind === 'agents') {
    ui.setActiveMainView('agents')
    return
  }

  ui.setActiveMainView(location.view)
}
