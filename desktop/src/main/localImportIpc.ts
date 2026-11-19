import type { LocalImportApi, LocalImportRunParams } from '../shared/localImport'
import type { ImportSettingsSetParams } from '@dotcraft/sdk/contracts'
import { DesktopHubError } from './desktopHubErrors'
import type { DesktopHubClient } from './desktopHub'
import type { WindowIpcContext } from './ipcBridgeContracts'

type HubAddress = { apiBaseUrl: string; token: string }

export function createLocalImportApi(ensureHub: (signal: AbortSignal) => Promise<HubAddress>): LocalImportApi {
  async function request<T>(operation: string, body?: object, timeout = 30_000): Promise<T> {
    const signal = AbortSignal.timeout(timeout)
    const hub = await ensureHub(signal)
    const response = await fetch(`${hub.apiBaseUrl}/v1/local-import/${operation}`, {
      method: body === undefined ? 'GET' : 'POST',
      headers: { Authorization: `Bearer ${hub.token}`, 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
      signal
    })
    if (!response.ok) {
      const data = await response.json().catch(() => null) as { error?: { code?: string; message?: string } } | null
      throw new DesktopHubError(
        data?.error?.code ?? 'localImportFailed',
        data?.error?.message ?? `Import request failed (${response.status}).`
      )
    }
    return await response.json() as T
  }

  return {
    detect: () => request('detect', undefined, 120_000),
    getState: () => request('state'),
    run: (params) => request('run', params),
    setSettings: (params) => request('settings', params)
  }
}

export function registerLocalImportHandlers(
  { handleSafe }: Pick<WindowIpcContext, 'handleSafe'>,
  getHubClient: () => DesktopHubClient
): void {
  handleSafe('local-import:detect', () => getHubClient().imports.detect())
  handleSafe('local-import:state', () => getHubClient().imports.getState())
  handleSafe('local-import:run', (_event, params: LocalImportRunParams) => getHubClient().imports.run(params))
  handleSafe('local-import:settings', (_event, params: ImportSettingsSetParams) => getHubClient().imports.setSettings(params))
}
