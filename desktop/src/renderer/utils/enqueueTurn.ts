import type { ClientRequestMethods } from '@dotcraft/sdk/contracts'
import { runtimeWorkspaceRootsFor } from './workspaceRuntimeRoots'

type EnqueueTurnParams = ClientRequestMethods['turn/enqueue']['params'] & { identityWorkspacePath: string }

export async function enqueueTurn({ identityWorkspacePath, ...params }: EnqueueTurnParams): Promise<ClientRequestMethods['turn/enqueue']['result']> {
  const runtimeWorkspaceRoots = runtimeWorkspaceRootsFor(identityWorkspacePath)
  if (runtimeWorkspaceRoots) {
    await window.api.appServer.sendRequest('thread/resume', { threadId: params.threadId, runtimeWorkspaceRoots })
  }
  return window.api.appServer.sendRequest('turn/enqueue', params)
}
