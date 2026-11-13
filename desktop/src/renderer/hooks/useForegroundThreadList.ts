import { useCallback, useEffect, useRef, useState } from 'react'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore, type PendingProjectThreadOpen } from '../stores/uiStore'
import { useTurnBookmarkStore } from '../stores/turnBookmarkStore'
import { useConnectionStore } from '../stores/connectionStore'
import type { SessionIdentity } from '../types/thread'
import { createThreadListPages, THREAD_LIST_PAGE_SIZE, type ThreadListPage } from './threadListPages'

function resolvePinnedThreadIdsForWorkspace(
  pinnedByWorkspace: Record<string, string[]> | undefined,
  workspacePath: string
): string[] {
  if (!pinnedByWorkspace) return []
  const exact = pinnedByWorkspace[workspacePath]
  if (Array.isArray(exact)) return exact

  const normalizedWorkspacePath = normalizeWorkspaceProjectKey(workspacePath)
  const match = Object.entries(pinnedByWorkspace).find(
    ([candidate]) => normalizeWorkspaceProjectKey(candidate) === normalizedWorkspacePath
  )
  return match?.[1] ?? []
}

export function currentForegroundThreadListKey(
  activeProjectKey: string | null | undefined,
  protocolWorkspacePath: string | null | undefined
): string {
  return normalizeWorkspaceProjectKey(activeProjectKey || protocolWorkspacePath || '')
}

export function canReloadForegroundThreadList(
  activeProjectKey: string | null | undefined,
  protocolWorkspacePath: string | null | undefined
): boolean {
  const protocolKey = normalizeWorkspaceProjectKey(protocolWorkspacePath)
  if (!protocolKey) return false
  const projectKey = normalizeWorkspaceProjectKey(activeProjectKey)
  if (!projectKey || projectKey.startsWith('remote:')) return true
  return projectKey === protocolKey
}

export function useForegroundThreadList(
  activeProjectKeyRef: { current: string | null | undefined },
  protocolWorkspacePathRef: { current: string }
) {
  const [loadedProjectKey, setLoadedProjectKey] = useState<string | null>(null)
  const setLoading = useThreadStore((s) => s.setLoading)
  const threadListReloadGenerationRef = useRef(0)
  const stopPagesRef = useRef<(() => void) | null>(null)
  const threadListRetryRef = useRef<{ key: string; attempts: number; timer: number | null }>({
    key: '',
    attempts: 0,
    timer: null
  })

  const loadThreadList = useCallback(async function load(pending: PendingProjectThreadOpen | null): Promise<void> {
    const requestGeneration = ++threadListReloadGenerationRef.current
    stopPagesRef.current?.()
    stopPagesRef.current = null
    const path = protocolWorkspacePathRef.current
    if (!canReloadForegroundThreadList(activeProjectKeyRef.current, path)) {
      return
    }
    if (threadListRetryRef.current.timer != null) {
      window.clearTimeout(threadListRetryRef.current.timer)
      threadListRetryRef.current.timer = null
    }
    const requestProtocolWorkspaceKey = normalizeWorkspaceProjectKey(path)
    const projectKey = currentForegroundThreadListKey(activeProjectKeyRef.current, path)
    const connection = useConnectionStore.getState()
    const isCurrentRequest = (): boolean => {
      const currentConnection = useConnectionStore.getState()
      return requestGeneration === threadListReloadGenerationRef.current &&
        projectKey === currentForegroundThreadListKey(activeProjectKeyRef.current, protocolWorkspacePathRef.current) &&
        requestProtocolWorkspaceKey === normalizeWorkspaceProjectKey(protocolWorkspacePathRef.current) &&
        connection.connectionKey === currentConnection.connectionKey &&
        connection.connectionId === currentConnection.connectionId &&
        connection.status === currentConnection.status
    }
    const identity: SessionIdentity = {
      channelName: 'dotcraft-desktop',
      userId: 'local',
      channelContext: `workspace:${path}`,
      workspacePath: path
    }
    const startedAt = performance.now()
    let directoryComplete = false
    let settingsReady = false
    const prunePins = (): void => {
      if (directoryComplete && settingsReady && isCurrentRequest()) useThreadStore.getState().prunePinnedThreadIds()
    }
    const openPendingThread = (): void => {
      if (!pending) return
      const ids = useThreadStore.getState().threadList.map((thread) => thread.id)
      if (!directoryComplete && !ids.includes(pending.threadId)) return
      const target = useUIStore.getState().consumePendingProjectThreadOpen(projectKey, ids, pending.requestId)
      if (target) {
        useUIStore.getState().setActiveMainView('conversation')
        useThreadStore.getState().setActiveThreadId(target.threadId)
      }
    }
    const pages = createThreadListPages(projectKey, connection, isCurrentRequest)
    stopPagesRef.current = pages.dispose
    setLoading(true)
    try {
      void window.api.settings.get().then(settings => {
        if (!isCurrentRequest()) return
        useThreadStore.getState().hydratePinnedThreadIds(
          projectKey,
          resolvePinnedThreadIdsForWorkspace(settings.pinnedThreadIdsByWorkspace, projectKey)
        )
        useTurnBookmarkStore.getState().hydrate(settings.turnBookmarksByThread)
        settingsReady = true
        prunePins()
      }).catch((error: unknown) => {
        if (isCurrentRequest()) console.error('Failed to load thread settings:', error)
      })
      const includeArchived = pending?.projectKey === projectKey && pending.includeArchived === true
      const params = {
        identity: { ...identity }, scope: 'workspace' as const, includeSubAgents: true, limit: THREAD_LIST_PAGE_SIZE,
        ...(includeArchived ? { includeArchived: true } : {})
      }
      const applyFirstPage = pages.beginRequest()
      const res = await window.api.appServer.sendRequest('thread/list', params) as unknown as ThreadListPage
      if (!isCurrentRequest()) {
        pages.dispose()
        return
      }
      applyFirstPage(res, true)
      threadListRetryRef.current = { key: '', attempts: 0, timer: null }
      directoryComplete = !res.nextCursor
      prunePins()
      openPendingThread()
      setLoadedProjectKey(projectKey)
      const elapsedMs = Math.round(performance.now() - startedAt)
      performance.mark('app:workspace-thread-list-ready', { detail: { workspacePath: path, elapsedMs } })
      console.info('[desktop] workspace renderer timing', { workspacePath: path, stage: 'thread-list-ready', elapsedMs })
      if (res.nextCursor) {
        void (async () => {
          let cursor: string | null | undefined = res.nextCursor
          try {
            while (cursor && isCurrentRequest()) {
              const applyPage = pages.beginRequest()
              const page = await window.api.appServer.sendRequest('thread/list', { ...params, cursor }) as unknown as ThreadListPage
              if (!isCurrentRequest()) return
              applyPage(page, false)
              cursor = page.nextCursor
              directoryComplete = !cursor
              openPendingThread()
            }
            prunePins()
          } catch (error: unknown) {
            if (isCurrentRequest()) console.error('Failed to load remaining thread list pages:', error)
          } finally {
            pages.dispose()
          }
        })()
      } else {
        pages.dispose()
      }
    } catch (err: unknown) {
      pages.dispose()
      if (!isCurrentRequest()) return
      console.error('Failed to load thread list:', err)
      const retryKey = [projectKey, requestProtocolWorkspaceKey, connection.connectionKey, connection.connectionId].join('\u0000')
      const retry = threadListRetryRef.current
      if (retry.key !== retryKey) {
        threadListRetryRef.current = { key: retryKey, attempts: 0, timer: null }
      }
      const nextRetry = threadListRetryRef.current
      if (nextRetry.attempts < 3) {
        nextRetry.attempts += 1
        nextRetry.timer = window.setTimeout(() => {
          nextRetry.timer = null
          if (isCurrentRequest()) void load(pending)
        }, 1200)
      } else if (pending) {
        useUIStore.getState().clearPendingProjectThreadOpen(projectKey, pending.threadId, pending.requestId)
      }
    } finally {
      if (isCurrentRequest()) {
        setLoading(false)
      }
    }
  }, [activeProjectKeyRef, protocolWorkspacePathRef, setLoading])

  const reloadThreadList = useCallback(
    () => loadThreadList(useUIStore.getState().pendingProjectThreadOpen), [loadThreadList]
  )

  useEffect(() => {
    return () => {
      threadListReloadGenerationRef.current += 1
      stopPagesRef.current?.()
      stopPagesRef.current = null
      if (threadListRetryRef.current.timer != null) {
        window.clearTimeout(threadListRetryRef.current.timer)
        threadListRetryRef.current.timer = null
      }
    }
  }, [])

  const invalidateThreadList = useCallback(() => {
    threadListReloadGenerationRef.current += 1
    stopPagesRef.current?.()
    stopPagesRef.current = null
    if (threadListRetryRef.current.timer != null) window.clearTimeout(threadListRetryRef.current.timer)
    threadListRetryRef.current = { key: '', attempts: 0, timer: null }
    setLoadedProjectKey(null)
    setLoading(false)
  }, [setLoading])

  return { reloadThreadList, invalidateThreadList, loadedProjectKey }
}
