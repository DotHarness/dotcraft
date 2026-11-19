import type { TurnStartOrEnqueueResult } from '@dotcraft/sdk/contracts'
import type { AppServerRequestClient, DynamicToolCallResult } from './desktopRuntimeThreadTools'
import type { ThreadTaskReasoningEffort } from '../shared/threadTasks'
import { readAppServerErrorFields } from '../shared/appServerError'

export async function submitThreadMessage(
  client: AppServerRequestClient,
  threadId: string,
  prompt: string,
  model: string | undefined,
  reasoningEffort: ThreadTaskReasoningEffort | undefined
): Promise<DynamicToolCallResult> {
  try {
    const { turn, queuedInput } = await client.sendRequest<TurnStartOrEnqueueResult>('turn/startOrEnqueue', {
      threadId,
      input: [{ type: 'text', text: prompt }],
      ...(model ? { model } : {}),
      ...(reasoningEffort ? { reasoning: { enabled: true, effort: reasoningEffort } } : {})
    })
    const queued = queuedInput != null
    return {
      success: true,
      contentItems: [{ type: 'text', text: queued ? `Queued message for thread ${threadId}.` : `Sent message to thread ${threadId}.` }],
      structuredContent: { threadId, turn: turn ?? null, accepted: true, queued, ...(queued ? { queuedInput } : {}) }
    }
  } catch (error) {
    switch (readAppServerErrorFields(error).rpcCode) {
      case -32011:
        return { success: false, errorCode: 'ThreadArchived', errorMessage: `Thread '${threadId}' is archived.` }
      case -32601:
        return { success: false, errorCode: 'ThreadManagementUnavailable', errorMessage: 'The target AppServer does not support follow-up admission.' }
      default:
        throw error
    }
  }
}
