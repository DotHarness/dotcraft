import { useEffect, useRef } from 'react'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'
import { useThreadStore } from '../stores/threadStore'

export function useWorkspaceReadinessTiming(workspacePath: string, status: string, loadedProjectKey: string | null): void {
  const projectKey = normalizeWorkspaceProjectKey(workspacePath)
  const loading = useThreadStore((s) => s.loading)
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  const activeThread = useThreadStore((s) => s.activeThread)
  const timing = useRef({ projectKey: '', startedAt: 0, connectionReady: false, contentReady: false })

  useEffect(() => {
    if (!projectKey) return
    if (timing.current.projectKey !== projectKey ||
      ((status === 'connecting' || status === 'disconnected') && timing.current.connectionReady)) {
      timing.current = { projectKey, startedAt: performance.now(), connectionReady: false, contentReady: false }
    }
    const current = timing.current
    const record = (stage: string): void => {
      const elapsedMs = Math.round(performance.now() - current.startedAt)
      performance.mark(`app:workspace-${stage}`, { detail: { workspacePath, elapsedMs } })
      console.info('[desktop] workspace renderer timing', { workspacePath, stage, elapsedMs })
    }
    if (status !== 'connected') return
    if (!current.connectionReady) {
      current.connectionReady = true
      record('connection-ready')
    }
    if (!current.contentReady && !loading &&
      normalizeWorkspaceProjectKey(loadedProjectKey) === projectKey &&
      (!activeThreadId || activeThread?.id === activeThreadId)) {
      current.contentReady = true
      record('content-data-ready')
    }
  }, [projectKey, workspacePath, status, loadedProjectKey, loading, activeThreadId, activeThread])
}
