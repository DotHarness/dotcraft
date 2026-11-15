import type { IpcMainInvokeEvent } from 'electron'
import type { CodexRemoteResult } from '../../shared/codexRemote'
import type { DesktopHubClient } from '../desktopHub'

interface CodexRemoteIpcDeps {
  handleSafe: (channel: string, listener: (event: IpcMainInvokeEvent, ...args: unknown[]) => unknown) => void
  getHubClient: () => DesktopHubClient
}

function failureOf(error: unknown): { code: string; message: string } {
  return {
    code: (error as { code?: string })?.code ?? 'hubUnavailable',
    message: error instanceof Error ? error.message : String(error)
  }
}

async function settle<T>(operation: () => Promise<T>): Promise<CodexRemoteResult<T>> {
  try {
    return { ok: true, value: await operation() }
  } catch (error) {
    return { ok: false, failure: failureOf(error) }
  }
}

export function registerCodexRemoteHandlers({ handleSafe, getHubClient }: CodexRemoteIpcDeps): void {
  handleSafe('codexRemote:status', () => settle(() => getHubClient().getCodexRemote()))
  handleSafe('codexRemote:enable', () => settle(() => getHubClient().enableCodexRemote()))
  handleSafe('codexRemote:disable', () => settle(() => getHubClient().disableCodexRemote()))
  handleSafe('codexRemote:create-pairing', () => settle(() => getHubClient().createCodexRemotePairing()))
  handleSafe('codexRemote:list-clients', () => settle(() => getHubClient().listCodexRemoteClients()))
  handleSafe('codexRemote:revoke-client', (_event, clientId) =>
    typeof clientId === 'string' && clientId
      ? settle(() => getHubClient().revokeCodexRemoteClient(clientId))
      : { ok: false, failure: { code: 'invalidClient', message: '' } })
}
