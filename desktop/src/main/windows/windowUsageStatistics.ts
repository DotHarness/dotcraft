import type { UsageStatisticsConnection } from '../../shared/usageStatistics'
import { resolveDefaultChatWorkspacePath } from '../defaultChatWorkspace'
import type { InitializeResult } from '../DesktopAppServerClient'
import type { DesktopWindowRuntime } from './windowRuntime'
import type { WorkspaceConnectionEntry } from './windowTypes'

type UsageConnectionContext = Pick<DesktopWindowRuntime, 'ensureDefaultChatConnection' | 'getWorkspaceConnection'>

export async function resolveLocalUsageConnection(ctx: UsageConnectionContext): Promise<UsageStatisticsConnection> {
  await ctx.ensureDefaultChatConnection()
  const entry = ctx.getWorkspaceConnection(resolveDefaultChatWorkspacePath())
  if (!entry || entry.kind === 'remote') throw new Error('The local usage statistics connection is unavailable.')
  const initialized = await initializedConnection(entry)
  return { connectionKey: entry.key, usageTelemetry: initialized.capabilities.usageTelemetry === true }
}

function initializedConnection(entry: WorkspaceConnectionEntry): Promise<InitializeResult> {
  if (entry.connected && entry.initializeResult) return Promise.resolve(entry.initializeResult)
  if (entry.errorMessage) return Promise.reject(new Error(entry.errorMessage))
  return new Promise((resolve, reject) => {
    const client = entry.client
    const cleanup = (): void => {
      client.off('ready', ready)
      client.off('reconnected', ready)
      client.off('reconnect-error', failed)
      client.off('close', closed)
      client.off('disposed', closed)
    }
    const ready = (result: InitializeResult): void => { cleanup(); resolve(result) }
    const failed = (error: unknown): void => { cleanup(); reject(error) }
    const closed = (): void => failed(new Error('The local usage statistics connection closed.'))
    client.once('ready', ready)
    client.once('reconnected', ready)
    client.once('reconnect-error', failed)
    client.once('close', closed)
    client.once('disposed', closed)
  })
}
