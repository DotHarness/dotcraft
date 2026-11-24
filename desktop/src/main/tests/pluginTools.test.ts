import { describe, expect, it, vi } from 'vitest'
import { activatePluginTool, type DesktopPluginToolHost } from '../plugins/pluginTools'
import type { AppServerRequestClient } from '../desktopRuntimeThreadTools'

function fakeClient(plugin: Record<string, unknown>, hooks: { key: string; trustStatus: string }[] = []) {
  const calls: Array<{ method: string; params: unknown }> = []
  const client: AppServerRequestClient = {
    sendRequest: async <T>(method: string, params?: unknown): Promise<T> => {
      calls.push({ method, params })
      if (method === 'plugin/view') return { plugin } as T
      if (method === 'hooks/list') return { hooks } as T
      return { outcome: 'applied' } as T
    }
  }
  return { client, mutations: () => calls.filter((call) => call.method !== 'plugin/view' && call.method !== 'hooks/list') }
}

function host(outcome: 'confirmed' | 'canceled', canSeeCard = true) {
  return {
    canSeeCard: vi.fn(async () => canSeeCard),
    confirm: vi.fn<DesktopPluginToolHost['confirm']>(async () => outcome)
  }
}

const call = { threadId: 'thread-1', turnId: 'turn-1' }

describe('ActivatePlugin', () => {
  it('confirms, then installs and trusts an uninstalled catalog plugin in order', async () => {
    const { client, mutations } = fakeClient({
      id: 'sample', displayName: 'Sample', installed: false, installable: true, marketplaceName: 'acme',
      hooks: [{ key: 'h1' }], dotnet: {}, desktop: {}
    })
    const confirmHost = host('confirmed')

    const result = await activatePluginTool(client, confirmHost, { id: 'sample', marketplaceName: 'acme' }, call)

    expect(result.success).toBe(true)
    expect(confirmHost.confirm).toHaveBeenCalledWith(
      { kind: 'pluginActivation', pluginId: 'sample', marketplaceName: 'acme' }, 'thread-1', 'turn-1')
    expect(mutations().map((entry) => entry.method)).toEqual(['plugin/install', 'hooks/trustPlugin', 'plugin/setTrusted'])
  })

  it('changes nothing when the user declines or no one at the composer can answer', async () => {
    const plugin = { id: 'sample', installed: true, enabled: false }
    const declined = fakeClient(plugin)
    expect(await activatePluginTool(declined.client, host('canceled'), { id: 'sample' }, call))
      .toMatchObject({ success: false, errorCode: 'PluginActivationCanceled' })
    expect(declined.mutations()).toEqual([])

    const unseen = fakeClient(plugin)
    const unseenHost = host('confirmed', false)
    expect(await activatePluginTool(unseen.client, unseenHost, { id: 'sample' }, call))
      .toMatchObject({ success: false, errorCode: 'TargetUnsupported' })
    expect(unseenHost.confirm).not.toHaveBeenCalled()
    expect(unseen.mutations()).toEqual([])
  })

  it('reloads an active, trusted plugin without asking', async () => {
    const { client, mutations } = fakeClient(
      { id: 'sample', installed: true, enabled: true, hooks: [{ key: 'h1' }] },
      [{ key: 'h1', trustStatus: 'trusted' }]
    )
    const confirmHost = host('confirmed')

    expect((await activatePluginTool(client, confirmHost, { id: 'sample' }, call)).success).toBe(true)
    expect(confirmHost.confirm).not.toHaveBeenCalled()
    expect(mutations()).toEqual([
      { method: 'plugin/setEnabled', params: { id: 'sample', enabled: false } },
      { method: 'plugin/setEnabled', params: { id: 'sample', enabled: true } }
    ])
  })
})
