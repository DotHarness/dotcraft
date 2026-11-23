import { useEffect, useState } from 'react'
import { sameWorkspaceProjectKey } from '../../../shared/workspaceProjectKey'
import { useThreadStore } from '../../stores/threadStore'
import { useWorkspaceProjectsStore } from '../../stores/workspaceProjectsStore'
import { requestWorkspaceResource, workspaceResourceKey, type WorkspaceResourceScope } from '../../stores/workspaceResourceScope'
import type { ThreadSummary } from '../../types/thread'
import { projectIdentity } from '../sidebar/projectThreads'

const pendingTitles = new Map<string, Promise<string | undefined>>()

function readTitle(scope: WorkspaceResourceScope, threadId: string): Promise<string | undefined> {
  const key = JSON.stringify([workspaceResourceKey(scope), threadId])
  const pending = pendingTitles.get(key)
  if (pending) return pending

  const request = requestWorkspaceResource(scope, 'thread/read', { threadId })
    .then(({ thread }) => thread?.displayName?.trim(), () => undefined)
    .finally(() => pendingTitles.delete(key))
  pendingTitles.set(key, request)
  return request
}

export function useThreadActionTitle(
  projectKey: string,
  threadId: string,
  fallbackTitle: string | undefined
): string | undefined {
  const foregroundTitle = useThreadStore((state) =>
    sameWorkspaceProjectKey(state.threadListProjectKey, projectKey)
      ? state.threadList.find((thread) => thread.id === threadId)?.displayName?.trim()
      : undefined
  )
  const project = useWorkspaceProjectsStore((state) =>
    state.projects.find((candidate) => sameWorkspaceProjectKey(projectIdentity(candidate), projectKey))
    ?? (state.chat && sameWorkspaceProjectKey(projectIdentity(state.chat), projectKey) ? state.chat : undefined)
  )
  const projectTitle = (project?.threads as ThreadSummary[] | undefined)
    ?.find((thread) => thread.id === threadId)?.displayName?.trim()
  const knownTitle = foregroundTitle || projectTitle || fallbackTitle
  const { connectionKey, connectionId } = project ?? {}
  const [resolvedTitle, setResolvedTitle] = useState<string>()

  useEffect(() => {
    if (knownTitle || !connectionKey || !connectionId) return
    let current = true
    void readTitle({ projectKey, connectionKey, connectionId }, threadId).then((title) => {
      if (current && title) setResolvedTitle(title)
    })
    return () => { current = false }
  }, [knownTitle, connectionKey, connectionId, projectKey, threadId])

  return knownTitle || resolvedTitle
}
