import { DesktopAppServerClient } from '../DesktopAppServerClient'
import type { ProviderListLike } from '../../shared/sshMachineRemote'

const INITIALIZE_TIMEOUT_MS = 15_000
const REQUEST_TIMEOUT_MS = 15_000

export function listRemoteProviders(wsUrl: string): Promise<ProviderListLike | null> {
  return new Promise((resolve, reject) => {
    const client = DesktopAppServerClient.fromWebSocket(wsUrl, {
      autoReconnect: false,
      initializeTimeoutMs: INITIALIZE_TIMEOUT_MS
    })
    let settled = false
    const settle = (finish: () => void): void => {
      if (settled) return
      settled = true
      client.removeAllListeners()
      client.dispose()
      finish()
    }

    client.once('ready', () => {
      client
        .sendRequest<ProviderListLike>('provider/list', {}, REQUEST_TIMEOUT_MS)
        .then((result) => settle(() => resolve(result ?? null)))
        .catch((error: unknown) => settle(() => reject(error instanceof Error ? error : new Error(String(error)))))
    })
    client.once('reconnect-error', (error) => {
      settle(() => reject(error instanceof Error ? error : new Error(String(error))))
    })
    client.once('close', () => {
      settle(() => reject(new Error('The machine closed the AppServer connection before the model check finished.')))
    })
  })
}
