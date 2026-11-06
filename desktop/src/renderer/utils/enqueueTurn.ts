import type { ClientRequestMethods } from '@dotcraft/sdk/contracts'
import { threadWorkspaceUpdateFor } from './workspaceRuntimeRoots'

type EnqueueTurnParams = ClientRequestMethods['turn/enqueue']['params'] & { identityWorkspacePath: string }

export async function enqueueTurn({ identityWorkspacePath, ...params }: EnqueueTurnParams): Promise<ClientRequestMethods['turn/enqueue']['result']> {
  const workspaceUpdate = threadWorkspaceUpdateFor(params.threadId, identityWorkspacePath)
  if (workspaceUpdate.runtimeWorkspaceRoots) {
    await window.api.appServer.sendRequest('thread/resume', { threadId: params.threadId, ...workspaceUpdate })
  }
  return window.api.appServer.sendRequest('turn/enqueue', params)
}
