import type { AppServerRequestClient, DynamicToolCallResult } from './desktopRuntimeThreadTools'

export async function submitThreadMessage(
  client: AppServerRequestClient,
  threadId: string,
  prompt: string,
  model?: string,
  reasoningEffort?: string
): Promise<DynamicToolCallResult> {
  const { thread } = await client.sendRequest<{ thread?: { status?: string } }>('thread/read', { threadId })
  if (!thread) {
    return { success: false, errorCode: 'ThreadNotFound', errorMessage: `Thread '${threadId}' was not found.` }
  }
  if (thread.status === 'archived') {
    return { success: false, errorCode: 'ThreadArchived', errorMessage: `Thread '${threadId}' is archived.` }
  }

  const result = await client.sendRequest<{ turn?: Record<string, unknown> }>('turn/start', {
    threadId,
    input: [{ type: 'text', text: prompt }],
    ...(model ? { model } : {}),
    ...(reasoningEffort ? { reasoning: { enabled: true, effort: reasoningEffort } } : {})
  })
  return {
    success: true,
    contentItems: [{ type: 'text', text: `Sent message to thread ${threadId}.` }],
    structuredContent: { threadId, turn: result.turn ?? null, accepted: true }
  }
}
