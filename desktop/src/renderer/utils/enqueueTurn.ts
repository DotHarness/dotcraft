import type { ClientRequestMethods } from '@dotcraft/sdk/contracts'
import { threadWorkspaceUpdateFor } from './workspaceRuntimeRoots'
import { captureComposerAttachmentTarget } from './prepareComposerAttachments'
import type { TypedAppServerRequestApi } from '../../shared/appServerBoundary'

type EnqueueTurnParams = ClientRequestMethods['turn/enqueue']['params'] & { identityWorkspacePath: string; request?: TypedAppServerRequestApi['sendRequest'] }

export async function enqueueTurn({ identityWorkspacePath, request: providedRequest, ...params }: EnqueueTurnParams): Promise<ClientRequestMethods['turn/enqueue']['result']> {
  const request = providedRequest ?? captureComposerAttachmentTarget(params.threadId).request
  const workspaceUpdate = threadWorkspaceUpdateFor(params.threadId, identityWorkspacePath)
  if (workspaceUpdate.runtimeWorkspaceRoots) {
    await request('thread/resume', { threadId: params.threadId, ...workspaceUpdate })
  }
  return request('turn/enqueue', params)
}
