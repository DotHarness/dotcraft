import type { ClientRequestMethods } from '@dotcraft/sdk/contracts'

type SendRequest = <Method extends 'turn/interrupt' | 'thread/archive'>(
  method: Method, params: ClientRequestMethods[Method]['params']
) => Promise<unknown>

export async function stopBeforeArchive(
  sendRequest: SendRequest,
  threadId: string,
  activeTurnId: string | null | undefined
): Promise<void> {
  if (activeTurnId) await sendRequest('turn/interrupt', { threadId, turnId: activeTurnId })
  await sendRequest('thread/archive', { threadId })
}
