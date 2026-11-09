import type { ClientRequestMethods } from '@dotcraft/sdk/contracts'
import type { AppServerResponseOptions, AppServerConnectionSource } from '../../shared/appServerBoundary'
import { useConnectionStore } from './connectionStore'
import { useWorkspaceProjectsStore } from './workspaceProjectsStore'
import type { ThreadObservationTarget } from '../utils/threadObservationScope'

const targets = new Map<string, ThreadObservationTarget>()

export function getThreadConnection(threadId: string): ThreadObservationTarget | undefined {
  return targets.get(threadId)
}

export function registerThreadConnection(threadId: string, target: ThreadObservationTarget): void {
  targets.set(threadId, target)
}

export function releaseThreadConnection(threadId: string): void {
  targets.delete(threadId)
}

export function retainedThreadIds(): Iterable<string> {
  return targets.keys()
}

export function isThreadObservationCurrent(target: AppServerConnectionSource): boolean {
  const foreground = useConnectionStore.getState()
  if (foreground.connectionKey === target.connectionKey) {
    return foreground.status === 'connected' && foreground.connectionId === target.connectionId
  }
  const projects = useWorkspaceProjectsStore.getState()
  const project = [...projects.projects, ...(projects.chat ? [projects.chat] : [])].find(
    (project) => project.connectionKey === target.connectionKey
  )
  return project?.connectionId === target.connectionId && project.loaded
}

export function matchesThreadConnection(threadId: string, source: Partial<AppServerConnectionSource>): boolean {
  const target = targets.get(threadId)
  return (
    target != null &&
    source.connectionKey === target.connectionKey &&
    source.connectionId === target.connectionId &&
    isThreadObservationCurrent(target)
  )
}

export function requestThreadConnection<M extends keyof ClientRequestMethods>(
  threadId: string,
  method: M,
  params: ClientRequestMethods[M]['params'],
  timeoutMs?: number | null,
  options?: AppServerResponseOptions<ClientRequestMethods[M]['result']>
): Promise<ClientRequestMethods[M]['result']> {
  const target = targets.get(threadId)
  return target
    ? window.api.appServer.sendConnectionRequest(target.connectionKey, method, params, timeoutMs, {
        ...options,
        connectionId: target.connectionId
      })
    : window.api.appServer.sendRequest(method, params, timeoutMs, options)
}
