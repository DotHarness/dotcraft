import type { ClientRequestMethods } from '@dotcraft/sdk/contracts'

type SendRequest = <Method extends 'thread/read' | 'turn/interrupt' | 'thread/archive'>(
  method: Method, params: ClientRequestMethods[Method]['params']
) => Promise<unknown>

export async function stopBeforeArchive(sendRequest: SendRequest, threadId: string): Promise<void> {
  const result = await sendRequest('thread/read', { threadId }) as {
    thread?: { runtime?: { activeTurnId?: string | null } }
  }
  const turnId = result.thread?.runtime?.activeTurnId
  if (turnId) await sendRequest('turn/interrupt', { threadId, turnId })
  await sendRequest('thread/archive', { threadId })
}
