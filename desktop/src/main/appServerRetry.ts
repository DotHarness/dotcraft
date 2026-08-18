import type { ConnectionMode } from './settings'
import type { RetryConnectionRequest } from '../shared/connectionStatus'
export type { RetryConnectionRequest } from '../shared/connectionStatus'

export interface AppServerRetryContext {
  currentWorkspacePath: string
  launchedWithRemote: boolean
  connectionMode: ConnectionMode
  reconnect: () => Promise<void>
  restartManaged: () => Promise<void>
}

export async function retryAppServerConnection(
  request: RetryConnectionRequest | undefined,
  context: AppServerRetryContext
): Promise<void> {
  if (!context.currentWorkspacePath) {
    throw new Error('Open a workspace before retrying the AppServer connection.')
  }

  if (context.launchedWithRemote || context.connectionMode === 'remote' || request?.restartManaged !== true) {
    await context.reconnect()
    return
  }

  await context.restartManaged()
}
