import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import type { AppLocale } from '../../shared/locales'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'
import { prepareDesktopPluginWorkspaceChange } from '../plugins/desktopPluginSource'
import { useAutomationsStore } from '../stores/automationsStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useMcpStore } from '../stores/mcpStore'
import { usePluginStore } from '../stores/pluginStore'
import { activateWorkspaceResources, pruneWorkspaceResources, refreshWorkspaceResources } from '../stores/providerConnectionScope'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { useThreadRouteStore } from '../stores/threadRouteStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore, type PendingProjectThreadOpen } from '../stores/uiStore'
import { createWarmThreads, readyWarmThread } from '../stores/warmThreads'
import { canReloadForegroundThreadList, useForegroundThreadList } from './useForegroundThreadList'
import { useThreadRestoration } from './useThreadRestoration'

interface Props {
  projectKey: string
  protocolWorkspacePath: string
  remoteWorkspace: boolean
  locale: AppLocale
}

function resetWorkspaceScopedRendererState(preserveLocalPlugins = false): void {
  prepareDesktopPluginWorkspaceChange(preserveLocalPlugins)
  useThreadStore.getState().resetForeground()
  useMcpStore.getState().reset()
  usePluginStore.getState().resetForWorkspaceChange()
  useAutomationsStore.setState({ automations: [], runs: {}, presets: [], selectedAutomationId: null, loading: false, error: null })
  if (useUIStore.getState().activeMainView !== 'settings') {
    useUIStore.getState().setActiveMainView('conversation')
  }
  useUIStore.getState().setPendingWelcomeTurn(null)
  useThreadRouteStore.getState().setPendingRoute(null)
}

function recordNavigationTiming(stage: string, request: PendingProjectThreadOpen): void {
  const detail = {
    requestId: request.requestId, projectKey: request.projectKey, threadId: request.threadId,
    elapsedMs: Math.round(performance.now() - request.startedAt)
  }
  performance.mark(`app:thread-navigation-${stage}`, { detail })
  console.info('[desktop] thread navigation timing', { stage, ...detail })
}

export function useForegroundThreadNavigation(props: Props) {
  const { projectKey, protocolWorkspacePath, remoteWorkspace, locale } = props
  const { status, connectionKey, connectionId, connectionEpoch, capabilities } = useConnectionStore()
  const pending = useUIStore((state) => state.pendingProjectThreadOpen)
  const activeThread = useThreadStore((state) => state.activeThread)
  const workspaceKey = normalizeWorkspaceProjectKey(projectKey || protocolWorkspacePath)
  const identityKey = `${workspaceKey}\u0000${normalizeWorkspaceProjectKey(protocolWorkspacePath)}`
  const projectRef = useRef(projectKey)
  const pathRef = useRef(protocolWorkspacePath)
  projectRef.current = projectKey
  pathRef.current = protocolWorkspacePath
  const [warm] = useState(createWarmThreads)
  const list = useForegroundThreadList(projectRef, pathRef)
  const { invalidateThreadList, reloadThreadList } = list
  const previous = useRef({ identityKey, connectionKey, connectionId, status })
  const appliedNavigation = useRef<PendingProjectThreadOpen | null>(null)
  const foregroundReady = status === 'connected' && connectionId != null
    && workspaceKey !== '' && normalizeWorkspaceProjectKey(connectionKey) === workspaceKey
    && canReloadForegroundThreadList(projectKey, protocolWorkspacePath)
  const readyScope = foregroundReady ? `${identityKey}\u0000${connectionKey}\u0000${connectionId}` : null
  const projects = useWorkspaceProjectsStore((state) => state.projects)
  const chat = useWorkspaceProjectsStore((state) => state.chat)

  useLayoutEffect(() => {
    activateWorkspaceResources(foregroundReady ? {
      projectKey: workspaceKey, connectionKey: connectionKey!, connectionId: connectionId!
    } : null)
  }, [foregroundReady, workspaceKey, connectionKey, connectionId])

  useEffect(() => {
    if (readyScope) void refreshWorkspaceResources()
  }, [readyScope])

  useEffect(() => { pruneWorkspaceResources() }, [projects, chat, readyScope])

  useLayoutEffect(() => {
    const before = previous.current
    previous.current = { identityKey, connectionKey, connectionId, status }
    const identityChanged = before.identityKey !== identityKey
    const connectionChanged = status === 'connected' && before.status === 'connected'
      && (before.connectionKey !== connectionKey || before.connectionId !== connectionId)
    const connectionLost = (status === 'disconnected' || status === 'error') && before.status !== status
    if (identityChanged || connectionChanged || connectionLost) {
      const retainedSelection = connectionChanged && !identityChanged
        && before.connectionKey === connectionKey && foregroundReady
        ? useThreadStore.getState().activeThreadId : null
      invalidateThreadList()
      resetWorkspaceScopedRendererState(!connectionLost && !remoteWorkspace)
      if (retainedSelection) useThreadStore.setState({ activeThreadId: retainedSelection })
      appliedNavigation.current = null
    }
    if (!foregroundReady) return
    const request = useUIStore.getState().pendingProjectThreadOpen
    if (!request || request.projectKey !== workspaceKey) return
    const entry = readyWarmThread(warm, request.threadId)
    if (!entry || entry.target.workspaceKey !== workspaceKey
      || entry.target.connectionKey !== connectionKey || entry.target.connectionId !== connectionId) return
    const consumed = useUIStore.getState().consumePendingProjectThreadOpen(
      workspaceKey, [request.threadId], request.requestId
    )
    if (!consumed) return
    recordNavigationTiming('foreground-ready', consumed)
    appliedNavigation.current = consumed
    useUIStore.getState().setActiveMainView('conversation')
    useThreadStore.getState().setActiveThreadId(consumed.threadId)
  }, [identityKey, connectionKey, connectionId, status, foregroundReady, workspaceKey,
    remoteWorkspace, pending, invalidateThreadList, warm])

  const restoration = useThreadRestoration({ workspaceKey, protocolWorkspacePath, locale }, warm)

  useLayoutEffect(() => {
    const request = appliedNavigation.current
    const threads = useThreadStore.getState()
    if (!foregroundReady || !request || request.projectKey !== workspaceKey
      || threads.activeThreadId !== request.threadId || threads.activeThread?.id !== request.threadId) return
    recordNavigationTiming('warm-applied', request)
    appliedNavigation.current = null
  }, [activeThread, foregroundReady, workspaceKey])

  const refresh = useRef<{ scope: string; requestId?: number } | null>(null)
  useEffect(() => {
    if (!readyScope) {
      refresh.current = null
      return
    }
    const request = useUIStore.getState().pendingProjectThreadOpen
    const requestId = request?.projectKey === workspaceKey ? request.requestId : undefined
    if (refresh.current?.scope === readyScope && (requestId == null || refresh.current.requestId === requestId)) return
    refresh.current = { scope: readyScope, requestId }
    void reloadThreadList()
  }, [readyScope, workspaceKey, pending?.requestId, reloadThreadList])

  useEffect(() => {
    if (!readyScope) return
    useThreadRouteStore.getState().resetForConnection()
    if (useConnectionStore.getState().capabilities?.automations) {
      void useAutomationsStore.getState().fetchAutomations()
    }
  }, [readyScope])

  const lastPluginLoad = useRef<string | null>(null)
  useEffect(() => {
    if (!readyScope || capabilities?.pluginManagement !== true) return
    const loadKey = `${readyScope}\u0000${connectionEpoch}`
    if (lastPluginLoad.current === loadKey) return
    lastPluginLoad.current = loadKey
    void usePluginStore.getState().fetchPlugins()
  }, [readyScope, capabilities?.pluginManagement, connectionEpoch])

  return { ...list, ...restoration, foregroundReady }
}
