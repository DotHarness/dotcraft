import { useEffect, useMemo, useState } from 'react'
import {
  devServerPortOfUrl,
  type DevServerFailure,
  type DevServerList,
  type DevServerScope,
  type DevServerSnapshot
} from '../../shared/devServers'
import { useConversationStore } from '../stores/conversationStore'
import { useDetailPanelStore } from '../stores/detailPanelStore'
import { useThreadStore } from '../stores/threadStore'
import { addToast } from '../stores/toastStore'

type Translator = (key: string, params?: Record<string, string | number>) => string

export function useDevServerScope(threadId: string | null): DevServerScope | null {
  const remote = useConversationStore((state) => state.remoteWorkspaceActive)
  const workspacePath = useConversationStore((state) => state.workspacePath)
  const threadFolder = useThreadStore((state) => {
    const thread = state.activeThread?.id === threadId ? state.activeThread : state.threadList.find((entry) => entry.id === threadId)
    return thread?.worktree?.path || thread?.effectiveWorkspacePath || thread?.workspacePath || ''
  })
  const folder = remote || !threadId ? null : threadFolder || workspacePath || null
  return useMemo(() => (folder && threadId ? { folder, threadId } : null), [folder, threadId])
}

export function useDevServers(scope: DevServerScope | null): DevServerList | null {
  const [list, setList] = useState<DevServerList | null>(null)
  useEffect(() => {
    setList(null)
    if (!scope) return
    let active = true
    const read = (): void => {
      window.api.devServers.list(scope).then((next) => { if (active) setList(next) }, () => undefined)
    }
    read()
    const stop = window.api.devServers.watch(scope.folder, read)
    return () => {
      active = false
      stop()
    }
  }, [scope])
  return list
}

export function openDevServerTab(threadId: string, url: string, label?: string): string {
  const store = useDetailPanelStore.getState()
  const port = devServerPortOfUrl(url)
  const existing = port === null
    ? null
    : store.getThreadState(threadId).tabs.find((tab) => tab.kind === 'browser' && devServerPortOfUrl(tab.currentUrl) === port)
  const tabId = existing?.id ?? store.openBrowser({ threadId, initialUrl: url, initialLabel: label ?? url })
  useDetailPanelStore.getState().setActiveViewerTab(tabId)
  useDetailPanelStore.getState().setDetailPanelVisible(true)
  return tabId
}

export function devServerFailureText(failure: DevServerFailure, t: Translator): string {
  switch (failure.kind) {
    case 'portInUse':
      return t(`devServers.failure.portInUse.${failure.holder}`, { port: failure.port })
    case 'folderLimit':
      return t('devServers.failure.folderLimit')
    case 'launchFile':
      return t('devServers.failure.launchFile')
    case 'spawn':
      return t('devServers.failure.spawn')
    case 'neverReady':
      return t('devServers.failure.neverReady')
    case 'exited':
      return t('devServers.failure.exited')
  }
}

/** A server that never got its port has no page, so opening it explains why instead of showing the holder's page. */
export function openDevServer(server: DevServerSnapshot, t: Translator): void {
  const { failure } = server
  if (server.status === 'error' && failure?.kind === 'portInUse' && failure.holder !== 'otherThread') {
    addToast(devServerFailureText(failure, t), 'warning')
    return
  }
  openDevServerTab(server.threadId, server.url, server.name)
}

export async function runDevServer(scope: DevServerScope, name: string, t: Translator): Promise<void> {
  try {
    const result = await window.api.devServers.start(scope, name)
    if (result.ok) {
      if ('attachUrl' in result) openDevServerTab(scope.threadId, result.attachUrl, name)
      else openDevServerTab(scope.threadId, result.server.url, result.server.name)
      return
    }
    if (result.server) openDevServer(result.server, t)
    else addToast(devServerFailureText(result.failure, t), 'warning')
  } catch {
    addToast(t('devServers.startFailed'), 'warning')
  }
}

export async function stopDevServer(serverId: string, t: Translator): Promise<void> {
  try {
    await window.api.devServers.stop(serverId)
  } catch {
    addToast(t('devServers.stopFailed'), 'warning')
  }
}
