import { beforeEach, afterEach, describe, expect, it, vi } from 'vitest'
import { waitFor } from '@testing-library/react'
import { installDesktopApiMock } from './desktopApiMock'
import { usePluginStore, type PluginEntry } from '../stores/pluginStore'
import { useConnectionStore } from '../stores/connectionStore'
import {
  observeDesktopPluginSource, useDesktopPluginSource, authorizeDesktopPlugins, revokeDesktopPlugins
} from '../plugins/desktopPluginSource'
import type { DesktopPluginRuntime } from '../plugins/desktopPluginLifecycle'

const { confirm } = vi.hoisted(() => ({ confirm: vi.fn() }))
vi.mock('../components/ui/ConfirmDialog', () => ({ requestConfirmDialog: confirm }))

const source = { sourceKey: 'a'.repeat(64), workspacePath: '/remote/work', remote: true, supported: true, trusted: false }
const plugin = { id: 'remote-ui', installed: true, enabled: true, desktop: { revision: 'b'.repeat(64) } } as PluginEntry
let stop: (() => void) | undefined
beforeEach(() => {
  confirm.mockReset()
  usePluginStore.setState({ plugins: [] })
  useConnectionStore.setState({ status: 'connected', connectionEpoch: 1 })
  useDesktopPluginSource.setState({ source: null, errors: {}, busy: false })
})
afterEach(() => { stop?.(); stop = undefined })

describe('remote Desktop source authorization', () => {
  it('does not prompt or activate when the remote server lacks artifact support', async () => {
    const unsupported = { ...source, supported: false, trusted: false }
    installDesktopApiMock({ desktopPlugins: { context: async () => unsupported } })
    const reconcile = vi.fn()
    usePluginStore.setState({ plugins: [plugin] })
    stop = observeDesktopPluginSource({ reconcile } as unknown as DesktopPluginRuntime)
    await waitFor(() => expect(useDesktopPluginSource.getState().source).toEqual(unsupported))
    expect(reconcile).toHaveBeenLastCalledWith([])
    expect(reconcile).not.toHaveBeenCalledWith([plugin], unsupported.sourceKey)
    expect(confirm).not.toHaveBeenCalled()
  })

  it('asks once, activates after the grant and deactivates on revoke', async () => {
    let trusted = false
    const setTrusted = vi.fn(async (params: { trusted: boolean }) => { trusted = params.trusted; return { ...source, trusted } })
    installDesktopApiMock({ desktopPlugins: { context: async () => ({ ...source, trusted }), setTrusted } })
    confirm.mockReturnValue({ result: Promise.resolve(true), dismiss: vi.fn() })
    const reconcile = vi.fn()
    stop = observeDesktopPluginSource({ reconcile } as unknown as DesktopPluginRuntime)
    usePluginStore.setState({ plugins: [plugin] })
    await waitFor(() => expect(reconcile).toHaveBeenCalledWith([plugin], source.sourceKey))
    expect(setTrusted).toHaveBeenCalledWith({ sourceKey: source.sourceKey, trusted: true })
    usePluginStore.setState({ plugins: [{ ...plugin }] })
    await waitFor(() => expect(useDesktopPluginSource.getState().busy).toBe(false))
    expect(confirm).toHaveBeenCalledTimes(1)
    await revokeDesktopPlugins()
    expect(reconcile).toHaveBeenLastCalledWith([])
    expect(useDesktopPluginSource.getState().source?.trusted).toBe(false)
    expect(usePluginStore.getState().plugins[0].enabled).toBe(true)
  })

  it('does not repeatedly prompt after refusal and permits explicit authorization', async () => {
    installDesktopApiMock({ desktopPlugins: {
      context: async () => source,
      setTrusted: async () => ({ ...source, trusted: true })
    } })
    confirm.mockReturnValueOnce({ result: Promise.resolve(false), dismiss: vi.fn() })
    confirm.mockReturnValue({ result: Promise.resolve(true), dismiss: vi.fn() })
    const reconcile = vi.fn()
    usePluginStore.setState({ plugins: [plugin] })
    stop = observeDesktopPluginSource({ reconcile } as unknown as DesktopPluginRuntime)
    await waitFor(() => expect(confirm).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(useDesktopPluginSource.getState().busy).toBe(false))
    usePluginStore.setState({ plugins: [{ ...plugin }] })
    await waitFor(() => expect(useDesktopPluginSource.getState().busy).toBe(false))
    expect(confirm).toHaveBeenCalledTimes(1)
    expect(reconcile).not.toHaveBeenCalledWith([plugin], source.sourceKey)
    await authorizeDesktopPlugins()
    expect(reconcile).toHaveBeenLastCalledWith([plugin], source.sourceKey)
  })

  it('ignores a pending grant when the connection switches', async () => {
    let resolve!: (value: boolean) => void
    const pending = new Promise<boolean>(accept => { resolve = accept })
    const dismiss = vi.fn(() => resolve(false))
    confirm.mockReturnValue({ result: pending, dismiss })
    const setTrusted = vi.fn(async () => ({ ...source, trusted: true }))
    installDesktopApiMock({ desktopPlugins: { context: async () => source, setTrusted } })
    const reconcile = vi.fn()
    usePluginStore.setState({ plugins: [plugin] })
    stop = observeDesktopPluginSource({ reconcile } as unknown as DesktopPluginRuntime)
    await waitFor(() => expect(confirm).toHaveBeenCalledTimes(1))
    useConnectionStore.setState({ status: 'connecting', connectionEpoch: 2 })
    await pending
    expect(dismiss).toHaveBeenCalled()
    expect(setTrusted).not.toHaveBeenCalled()
    expect(reconcile).toHaveBeenLastCalledWith([])
  })

  it('waits for the new catalog instead of activating the previous workspace snapshot', async () => {
    let currentSource = { ...source, trusted: true }
    installDesktopApiMock({ desktopPlugins: { context: async () => currentSource } })
    const reconcile = vi.fn()
    usePluginStore.setState({ plugins: [plugin] })
    stop = observeDesktopPluginSource({ reconcile } as unknown as DesktopPluginRuntime)
    await waitFor(() => expect(reconcile).toHaveBeenCalledWith([plugin], source.sourceKey))
    currentSource = { ...currentSource, sourceKey: 'c'.repeat(64), workspacePath: '/other' }
    useConnectionStore.setState({ status: 'connected', connectionEpoch: 2 })
    await waitFor(() => expect(useDesktopPluginSource.getState().source?.sourceKey).toBe(currentSource.sourceKey))
    expect(reconcile).not.toHaveBeenCalledWith([plugin], currentSource.sourceKey)
    expect(usePluginStore.getState().plugins).toEqual([])
    usePluginStore.setState({ plugins: [plugin] })
    await waitFor(() => expect(reconcile).toHaveBeenCalledWith([plugin], currentSource.sourceKey))
  })
})
