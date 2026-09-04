import './setupPluginRuntime'
import type { DesktopPluginHost, DesktopPluginSettingsSnapshot } from '@dotcraft/plugin'
import { waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { clearDesktopPluginKernel } from '../plugins/desktopPluginKernel'
import {
  clearDesktopPluginRegistry,
  useDesktopPluginRegistry
} from '../plugins/desktopPluginRegistry'
import { DesktopPluginRuntime } from '../plugins/desktopPluginRuntime'
import type { PluginEntry } from '../stores/pluginStore'
import { useToastStore } from '../stores/toastStore'
import { installDesktopApiMock } from './desktopApiMock'
import type { RawNotificationPayload } from '../../shared/appServerBoundary'

const revision = 'a'.repeat(64)
let runtime: DesktopPluginRuntime | null = null

beforeEach(() => {
  clearDesktopPluginRegistry()
  clearDesktopPluginKernel()
  useToastStore.setState({ toasts: [] })
})

afterEach(async () => {
  await runtime?.stop()
  runtime = null
  clearDesktopPluginRegistry()
  clearDesktopPluginKernel()
  document.head.querySelectorAll('link[data-dotcraft-desktop-plugin]').forEach((link) => link.remove())
})

function plugin(enabled = true): PluginEntry {
  return {
    id: 'fixture.desktop',
    displayName: 'Fixture Desktop Plugin',
    version: '1.0.0',
    enabled,
    installed: true,
    installable: false,
    removable: false,
    source: 'local',
    rootPath: 'X:\\fixtures\\fixture.desktop',
    functions: [],
    skills: [],
    apps: [],
    desktop: { entry: './desktop/dist/index.mjs', revision, styles: [] },
    mcpServers: [],
    lspServers: []
  }
}

function snapshotOf(value: Record<string, unknown>): DesktopPluginSettingsSnapshot {
  return {
    schema: { fields: [] },
    personal: value,
    workspace: {},
    value,
    writableScopes: ['personal']
  }
}

async function flush(): Promise<void> {
  for (let tick = 0; tick < 8; tick += 1) await Promise.resolve()
}

function installSettingsApi() {
  const notifiers = new Set<(payload: RawNotificationPayload) => void>()
  const held: (() => void)[] = []
  const state = {
    stored: { accent: 'blue' } as Record<string, unknown>,
    mutateFailure: null as Error | null,
    readFailure: null as Error | null,
    hold: new Set<string>()
  }
  function apply(method: string, params: unknown): () => DesktopPluginSettingsSnapshot {
    if (method === 'plugin/config/get') {
      const { readFailure, stored } = state
      return readFailure ? () => { throw readFailure } : () => snapshotOf(stored)
    }
    if (method !== 'plugin/config/mutate') throw new Error(`Unexpected AppServer request: ${method}`)
    const { mutateFailure } = state
    if (mutateFailure) return () => { throw mutateFailure }
    const { operations } = params as { operations: readonly { op: string; key: string; value?: unknown }[] }
    const next = { ...state.stored }
    for (const operation of operations) {
      if (operation.op === 'set') next[operation.key] = operation.value
      else delete next[operation.key]
    }
    state.stored = next
    return () => snapshotOf(next)
  }
  const sendRequestRaw = vi.fn(async (method: string, params: unknown) => {
    const respond = apply(method, params)
    if (!state.hold.has(method)) return respond()
    return await new Promise<DesktopPluginSettingsSnapshot>((resolve, reject) => {
      held.push(() => {
        try {
          resolve(respond())
        } catch (error) {
          reject(error)
        }
      })
    })
  })
  installDesktopApiMock({
    appServer: {
      sendRequestRaw,
      onNotificationRaw: (callback: (payload: RawNotificationPayload) => void) => {
        notifiers.add(callback)
        return () => {
          notifiers.delete(callback)
        }
      }
    }
  })
  return {
    state,
    reads: () => sendRequestRaw.mock.calls.filter(([method]) => method === 'plugin/config/get').length,
    hold: (...methods: string[]) => {
      for (const method of methods) state.hold.add(method)
    },
    release: (index: number) => held[index]!(),
    notify(regions: string[] = ['plugins.config']) {
      const payload: RawNotificationPayload = {
        method: 'workspace/configChanged',
        params: { source: 'plugin/config/mutate', regions, changedAt: new Date().toISOString() }
      }
      for (const notifier of [...notifiers]) notifier(payload)
    },
    watchers: () => notifiers.size
  }
}

async function activate(register: (host: DesktopPluginHost) => void): Promise<DesktopPluginHost> {
  let host!: DesktopPluginHost
  runtime = new DesktopPluginRuntime({
    registerModule: async () => ({ entryUrl: 'dotcraft-plugin://fixture.desktop/entry.mjs', styleUrls: [] }),
    removeModule: async () => ({ ok: true }),
    importModule: async () => ({
      activate: (value: DesktopPluginHost) => {
        host = value
        register(value)
        return { mainViews: [], settingsPages: [] }
      }
    })
  })
  runtime.reconcile([plugin()])
  await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.size).toBe(1))
  return host
}

describe('host.settings.onChange', () => {
  it('reads the baseline once and stays quiet when a refetch finds the same snapshot', async () => {
    const api = installSettingsApi()
    const listener = vi.fn()
    await activate((host) => host.settings.onChange(listener))
    await waitFor(() => expect(api.reads()).toBe(1))

    api.notify(['workspace.defaultApprovalPolicy'])
    expect(api.reads()).toBe(1)

    api.notify()
    await waitFor(() => expect(api.reads()).toBe(2))
    expect(listener).not.toHaveBeenCalled()
  })

  it('notifies each listener once and re-reads once for three listeners', async () => {
    const api = installSettingsApi()
    const listeners = [vi.fn(), vi.fn(), vi.fn()]
    await activate((host) => {
      for (const listener of listeners) host.settings.onChange(listener)
    })
    await waitFor(() => expect(api.reads()).toBe(1))

    api.state.stored = { accent: 'red' }
    api.notify()

    await waitFor(() => expect(listeners[0]).toHaveBeenCalledOnce())
    expect(api.reads()).toBe(2)
    for (const listener of listeners) {
      expect(listener).toHaveBeenCalledOnce()
      expect(listener).toHaveBeenCalledWith(snapshotOf({ accent: 'red' }))
    }
  })

  it('publishes a successful mutate and then suppresses a broadcast that lands after it', async () => {
    const api = installSettingsApi()
    const listener = vi.fn()
    const host = await activate((value) => value.settings.onChange(listener))
    await waitFor(() => expect(api.reads()).toBe(1))

    await host.settings.mutate('personal', [{ op: 'set', key: 'accent', value: 'green' }])
    expect(listener).toHaveBeenCalledOnce()
    expect(listener).toHaveBeenCalledWith(snapshotOf({ accent: 'green' }))

    api.notify()
    await waitFor(() => expect(api.reads()).toBe(2))
    expect(listener).toHaveBeenCalledOnce()
  })

  it('publishes once when the broadcast overtakes the mutate response', async () => {
    const api = installSettingsApi()
    const listener = vi.fn()
    const host = await activate((value) => value.settings.onChange(listener))
    await waitFor(() => expect(api.reads()).toBe(1))
    api.hold('plugin/config/mutate', 'plugin/config/get')

    const mutation = host.settings.mutate('personal', [{ op: 'set', key: 'accent', value: 'green' }])
    api.notify()
    expect(api.reads()).toBe(2)

    api.release(0)
    await expect(mutation).resolves.toEqual(snapshotOf({ accent: 'green' }))
    api.release(1)
    await flush()

    expect(listener).toHaveBeenCalledOnce()
    expect(listener).toHaveBeenCalledWith(snapshotOf({ accent: 'green' }))
  })

  it('never hands a listener the older snapshot when an earlier read resolves last', async () => {
    const api = installSettingsApi()
    const listener = vi.fn()
    const host = await activate((value) => value.settings.onChange(listener))
    await waitFor(() => expect(api.reads()).toBe(1))
    api.hold('plugin/config/mutate', 'plugin/config/get')

    const first = host.settings.mutate('personal', [{ op: 'set', key: 'accent', value: 'red' }])
    api.notify()
    api.release(0)
    await first

    const second = host.settings.mutate('personal', [{ op: 'set', key: 'accent', value: 'green' }])
    api.notify()
    api.release(2)
    await second

    api.release(1)
    api.release(3)
    await flush()

    expect(listener.mock.calls.map(([snapshot]) => snapshot.value.accent)).toEqual(['red', 'green'])
  })

  it('rethrows a rejected mutate without announcing anything', async () => {
    const api = installSettingsApi()
    const listener = vi.fn()
    const host = await activate((value) => value.settings.onChange(listener))
    await waitFor(() => expect(api.reads()).toBe(1))
    api.state.mutateFailure = new Error('PluginConfigurationWriteFailed')

    await expect(host.settings.mutate('personal', [{ op: 'set', key: 'accent', value: 'gold' }]))
      .rejects.toThrow('PluginConfigurationWriteFailed')

    expect(listener).not.toHaveBeenCalled()
    expect(api.reads()).toBe(1)
  })

  it('publishes once when the broadcast read resolves before the mutate response', async () => {
    const api = installSettingsApi()
    const listener = vi.fn()
    const host = await activate((value) => value.settings.onChange(listener))
    await waitFor(() => expect(api.reads()).toBe(1))
    api.hold('plugin/config/mutate', 'plugin/config/get')

    const mutation = host.settings.mutate('personal', [{ op: 'set', key: 'accent', value: 'green' }])
    api.notify()
    expect(api.reads()).toBe(2)

    api.release(1)
    await flush()
    expect(listener).toHaveBeenCalledOnce()

    api.release(0)
    await expect(mutation).resolves.toEqual(snapshotOf({ accent: 'green' }))
    await flush()

    expect(listener).toHaveBeenCalledOnce()
    expect(listener).toHaveBeenCalledWith(snapshotOf({ accent: 'green' }))
  })

  it('drops a read that resolves after a newer read was issued', async () => {
    const api = installSettingsApi()
    const listener = vi.fn()
    await activate((host) => host.settings.onChange(listener))
    await waitFor(() => expect(api.reads()).toBe(1))
    api.hold('plugin/config/get')

    api.state.stored = { accent: 'red' }
    api.notify()
    api.state.stored = { accent: 'green' }
    api.notify()
    expect(api.reads()).toBe(3)

    api.release(1)
    await flush()
    api.release(0)
    await flush()

    expect(listener.mock.calls.map(([snapshot]) => snapshot.value.accent)).toEqual(['green'])
  })

  it('keeps notifying the remaining listeners when one throws', async () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => {})
    const api = installSettingsApi()
    const first = vi.fn()
    const last = vi.fn()
    const host = await activate((value) => {
      value.settings.onChange(first)
      value.settings.onChange(() => {
        throw new Error('listener failed')
      })
      value.settings.onChange(last)
    })
    await waitFor(() => expect(api.reads()).toBe(1))

    await host.settings.mutate('personal', [{ op: 'set', key: 'accent', value: 'violet' }])

    expect(first).toHaveBeenCalledOnce()
    expect(last).toHaveBeenCalledOnce()
    expect(error).toHaveBeenCalled()
    error.mockRestore()
  })

  it('detaches the shared watcher when the generation is disposed', async () => {
    const api = installSettingsApi()
    const listener = vi.fn()
    await activate((host) => host.settings.onChange(listener))
    await waitFor(() => expect(api.reads()).toBe(1))
    expect(api.watchers()).toBe(1)

    runtime!.reconcile([plugin(false)])
    await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.size).toBe(0))

    expect(api.watchers()).toBe(0)
    api.state.stored = { accent: 'red' }
    api.notify()
    expect(api.reads()).toBe(1)
    expect(listener).not.toHaveBeenCalled()
  })
})
