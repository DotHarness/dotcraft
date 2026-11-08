import { useCallback, useEffect, useRef, useState } from 'react'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'
import { readStateMark, useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useTurnBookmarkStore } from '../stores/turnBookmarkStore'
import { withReadStates } from '../utils/threadReadState'
import type { SessionIdentity, ThreadSummary, ThreadReadState } from '../types/thread'

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
  const setThreadList = useThreadStore((s) => s.setThreadList)
  const setLoading = useThreadStore((s) => s.setLoading)
  const threadListReloadGenerationRef = useRef(0)
  const threadListRetryRef = useRef<{ key: string; attempts: number; timer: number | null }>({
    key: '',
    attempts: 0,
    timer: null
  })

  const reloadThreadList = useCallback(async () => {
    const requestGeneration = ++threadListReloadGenerationRef.current
    const path = protocolWorkspacePathRef.current
    if (!canReloadForegroundThreadList(activeProjectKeyRef.current, path)) {
      return
    }
    const requestProtocolWorkspaceKey = normalizeWorkspaceProjectKey(path)
    const projectKey = currentForegroundThreadListKey(activeProjectKeyRef.current, path)
    const isCurrentRequest = (): boolean =>
      requestGeneration === threadListReloadGenerationRef.current &&
      projectKey === currentForegroundThreadListKey(activeProjectKeyRef.current, protocolWorkspacePathRef.current) &&
      requestProtocolWorkspaceKey === normalizeWorkspaceProjectKey(protocolWorkspacePathRef.current)
    const identity: SessionIdentity = {
      channelName: 'dotcraft-desktop',
      userId: 'local',
      channelContext: `workspace:${path}`,
      workspacePath: path
    }
    const startedAt = performance.now()
    setLoading(true)
    try {
      const pending = useUIStore.getState().pendingProjectThreadOpen
      const includeArchived = pending?.projectKey === projectKey && pending.includeArchived === true
      const params = {
        identity: { ...identity }, scope: 'workspace' as const, includeSubAgents: true,
        ...(includeArchived ? { includeArchived: true } : {})
      }
      const requestedAtMark = readStateMark()
      const [settings, result] = await Promise.all([
        window.api.settings.get(),
        window.api.appServer.sendRequest('thread/list', params)
      ])
      if (!isCurrentRequest()) return
      const res = result as unknown as { data: ThreadSummary[]; readStates?: Record<string, ThreadReadState> }
      const threadStore = useThreadStore.getState()
      const listedThreads = res.data ?? []
      const activeThreads = includeArchived ? listedThreads.filter((thread) => thread.status !== 'archived') : listedThreads
      threadStore.setThreadList(withReadStates(activeThreads, res.readStates, requestedAtMark), projectKey)
      if (threadListRetryRef.current.timer != null) {
        window.clearTimeout(threadListRetryRef.current.timer)
      }
      threadListRetryRef.current = { key: '', attempts: 0, timer: null }
      threadStore.hydratePinnedThreadIds(
        projectKey,
        resolvePinnedThreadIdsForWorkspace(settings.pinnedThreadIdsByWorkspace, projectKey)
      )
      threadStore.prunePinnedThreadIds()
      useTurnBookmarkStore.getState().hydrate(settings.turnBookmarksByThread)
      const pendingProjectThreadOpen = useUIStore.getState().consumePendingProjectThreadOpen(
        projectKey,
        listedThreads.map((thread) => thread.id)
      )
      if (pendingProjectThreadOpen) {
        useUIStore.getState().setActiveMainView('conversation')
        useThreadStore.getState().setActiveThreadId(pendingProjectThreadOpen.threadId)
      }
      setLoadedProjectKey(projectKey)
      const elapsedMs = Math.round(performance.now() - startedAt)
      performance.mark('app:workspace-thread-list-ready', { detail: { workspacePath: path, elapsedMs } })
      console.info('[desktop] workspace renderer timing', { workspacePath: path, stage: 'thread-list-ready', elapsedMs })
    } catch (err: unknown) {
      if (!isCurrentRequest()) return
      console.error('Failed to load thread list:', err)
      const currentThreadListProjectKey = useThreadStore.getState().threadListProjectKey
      if (currentThreadListProjectKey !== projectKey) {
        setThreadList([], projectKey)
      }
      const retryKey = `${projectKey}\u0000${requestProtocolWorkspaceKey}`
      const retry = threadListRetryRef.current
      if (retry.key !== retryKey) {
        if (retry.timer != null) window.clearTimeout(retry.timer)
        threadListRetryRef.current = { key: retryKey, attempts: 0, timer: null }
      }
      const nextRetry = threadListRetryRef.current
      if (nextRetry.attempts < 3 && nextRetry.timer == null) {
        nextRetry.attempts += 1
        nextRetry.timer = window.setTimeout(() => {
          nextRetry.timer = null
          void reloadThreadList()
        }, 1200)
      } else if (nextRetry.attempts >= 3) {
        useUIStore.getState().clearPendingProjectThreadOpen(projectKey)
      }
    } finally {
      if (isCurrentRequest()) {
        setLoading(false)
      }
    }
  }, [activeProjectKeyRef, protocolWorkspacePathRef, setThreadList, setLoading])

  useEffect(() => {
    return () => {
      if (threadListRetryRef.current.timer != null) {
        window.clearTimeout(threadListRetryRef.current.timer)
        threadListRetryRef.current.timer = null
      }
    }
  }, [])

  const invalidateThreadList = useCallback(() => {
    threadListReloadGenerationRef.current += 1
    setLoadedProjectKey(null)
  }, [])

  return { reloadThreadList, invalidateThreadList, loadedProjectKey }
}
