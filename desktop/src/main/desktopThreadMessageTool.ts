import type { AppServerRequestClient, DynamicToolCallResult } from './desktopRuntimeThreadTools'
import { buildReasoningConfig, desktopIdentity } from './threadTasks/childThreads'
import type { ThreadTaskReasoningEffort } from '../shared/threadTasks'
import { readAppServerErrorFields } from '../shared/appServerError'

interface TargetThread {
  status?: string
  configuration?: Record<string, unknown> | null
  runtime?: {
    busy?: boolean
    waitingOnPlanConfirmation?: boolean
  }
}

export async function submitThreadMessage(
  client: AppServerRequestClient,
  threadId: string,
  prompt: string,
  model: string | undefined,
  reasoningEffort: ThreadTaskReasoningEffort | undefined,
  workspacePath: string
): Promise<DynamicToolCallResult> {
  const { thread } = await client.sendRequest<{ thread?: TargetThread }>('thread/read', { threadId })
  if (!thread) return failure('ThreadNotFound', `Thread '${threadId}' was not found.`)
  if (thread.status === 'archived') return failure('ThreadArchived', `Thread '${threadId}' is archived.`)

  const busy = thread.runtime?.busy || thread.runtime?.waitingOnPlanConfirmation
  const config = { ...thread.configuration }
  if (reasoningEffort) {
    const existing = typeof config.reasoning === 'object' && config.reasoning !== null
      ? config.reasoning as Record<string, unknown> : undefined
    config.reasoning = buildReasoningConfig(reasoningEffort, existing)
  }
  const input = [{ type: 'text', text: prompt }]
  if (reasoningEffort && !busy) await updateConfiguration()
  if (!busy) {
    try {
      const result = await client.sendRequest<{ turn?: Record<string, unknown> }>('turn/start', {
        threadId, input, identity: desktopIdentity(workspacePath), ...(model ? { model } : {})
      })
      return {
        success: true,
        contentItems: [{ type: 'text', text: `Sent message to thread ${threadId}.` }],
        structuredContent: { threadId, turn: result.turn ?? null, accepted: true, queued: false }
      }
    } catch (error) {
      if (readAppServerErrorFields(error).rpcCode !== -32012) throw error
    }
  }

  if (model) {
    if (typeof config.providerId !== 'string' || !config.providerId) {
      return failure('UnsupportedOption', 'Cannot change the model of a busy chat without a configured provider.')
    }
    config.model = model
  }
  if (model || (busy && reasoningEffort)) await updateConfiguration()
  try {
    const result = await client.sendRequest<{ queuedInput?: Record<string, unknown> }>('turn/enqueue', { threadId, input })
    return {
      success: true,
      contentItems: [{ type: 'text', text: `Queued message for thread ${threadId}.` }],
      structuredContent: { threadId, turn: null, accepted: true, queued: true, queuedInput: result.queuedInput ?? null }
    }
  } catch (error) {
    if (readAppServerErrorFields(error).rpcCode === -32601) {
      return failure('ThreadBusy', `Thread '${threadId}' is busy and does not support queueing.`)
    }
    throw error
  }

  function updateConfiguration(): Promise<unknown> {
    return client.sendRequest('thread/config/update', { threadId, config })
  }
}

function failure(errorCode: string, errorMessage: string): DynamicToolCallResult {
  return { success: false, errorCode, errorMessage }
}
