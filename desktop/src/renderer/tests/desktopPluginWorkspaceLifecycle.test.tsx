import type { DesktopPluginActivation, DesktopPluginHost } from '@dotcraft/plugin'
import { waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { DesktopPluginSource } from '../../shared/desktopPluginSource'
import { DesktopPluginRuntime } from '../plugins/desktopPluginLifecycle'
import { clearDesktopPluginRegistry, useDesktopPluginRegistry } from '../plugins/desktopPluginRegistry'
import { observeDesktopPluginSource, prepareDesktopPluginWorkspaceChange, useDesktopPluginSource } from '../plugins/desktopPluginSource'
import { useConnectionStore } from '../stores/connectionStore'
import { usePluginStore, type PluginEntry } from '../stores/pluginStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { installDesktopApiMock } from './desktopApiMock'

const revision = 'a'.repeat(64)
const localSourceKey = 'c'.repeat(64)
const plugin: PluginEntry = {
  id: 'sample-extension', displayName: 'Sample', version: '1.0.0', installed: true, enabled: true,
  installable: false, removable: true, source: 'local', rootPath: 'X:\\plugins\\sample-extension',
  desktop: { revision, entry: './desktop/dist/index.mjs' },
  functions: [], skills: [], mcpServers: [], lspServers: []
}
const localSource = (key: string): DesktopPluginSource => ({
  sourceKey: localSourceKey, workspacePath: `X:\\workspace-${key}`, remote: false, trusted: true, supported: true
})
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(accept => { resolve = accept })
  return { promise, resolve }
}
const icon = () => null
const activation = (dispose = vi.fn()): DesktopPluginActivation => ({
  mainViews: [{ id: 'main', label: { default: 'Sample' }, icon, component: () => null }], dispose
})
let source: DesktopPluginSource
let activate: ReturnType<typeof vi.fn>
let runtime: DesktopPluginRuntime
let stop: (() => void) | undefined
const sendRequest = vi.fn()
const oldDispose = vi.fn()
const removeModule = vi.fn()

beforeEach(() => {
  clearDesktopPluginRegistry()
  oldDispose.mockClear()
  removeModule.mockReset().mockResolvedValue({ ok: true })
  source = localSource('a')
  activate = vi.fn(() => activation(oldDispose))
  sendRequest.mockReset().mockResolvedValue({ plugins: [plugin], snapshotRevision: 0 })
  installDesktopApiMock({
    desktopPlugins: { context: async () => source },
    appServer: { sendRequest, sendRequestRaw: async () => ({ workspacePath: source.workspacePath, plugins: [], snapshotRevision: 0 }) }
  })
  usePluginStore.getState().resetForWorkspaceChange()
  usePluginStore.setState({ plugins: [plugin], catalogStatus: 'ready', hasCompleteCatalog: true })
  useConnectionStore.setState({ status: 'connected', connectionEpoch: 1 })
  useWorkspaceProjectsStore.setState({ foregroundWorkspacePath: source.workspacePath })
  useDesktopPluginSource.setState({ source: null, busy: false, errors: {} })
  runtime = new DesktopPluginRuntime({
    registerModule: async () => ({ entryUrl: 'sample.mjs', styleUrls: [] }),
    removeModule,
    importModule: async () => ({ activate: (host: DesktopPluginHost) => activate(host) })
  })
})
afterEach(async () => {
  stop?.()
  stop = undefined
  await runtime.stop()
  clearDesktopPluginRegistry()
  useWorkspaceProjectsStore.getState().reset()
})
async function start(): Promise<void> {
  stop = observeDesktopPluginSource(runtime)
  await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.size).toBe(1))
}
function promote(key: string): void {
  source = localSource(key)
  useWorkspaceProjectsStore.setState({ foregroundWorkspacePath: source.workspacePath })
  useConnectionStore.getState().setStatus({ status: 'connected' })
}

describe('plugin workspace lifecycle', () => {
  it('keeps the same local generation through both resets and uses the target workspace', async () => {
    await start()
    const original = useDesktopPluginRegistry.getState().generations.get(plugin.id)!
    const host = activate.mock.calls[0][0] as DesktopPluginHost
    const list = deferred<unknown>()
    sendRequest.mockReturnValueOnce(list.promise)
    prepareDesktopPluginWorkspaceChange(true)
    usePluginStore.getState().resetForWorkspaceChange()
    promote('b')
    const loading = usePluginStore.getState().fetchPlugins()
    expect(useDesktopPluginRegistry.getState().generations.get(plugin.id)).toBe(original)
    list.resolve({ plugins: [plugin], snapshotRevision: 0 })
    await loading
    await waitFor(() => expect(useDesktopPluginSource.getState().busy).toBe(false))
    expect(useDesktopPluginRegistry.getState().generations.get(plugin.id)).toBe(original)
    expect(activate).toHaveBeenCalledOnce()
    expect(oldDispose).not.toHaveBeenCalled()
    expect(removeModule).not.toHaveBeenCalled()
    expect(host.session.workspacePath).toBe(source.workspacePath)
    expect(await host.appServer.request('plugin/list', { includeDisabled: true })).toMatchObject({ workspacePath: source.workspacePath })
    sendRequest.mockResolvedValueOnce({ plugins: [], snapshotRevision: 0 })
    await usePluginStore.getState().fetchPlugins()
    await waitFor(() => expect(oldDispose).toHaveBeenCalledOnce())
    expect(removeModule).toHaveBeenCalledWith({ pluginId: plugin.id, revision, sourceKey: localSourceKey })
  })

  it('applies a successful disable after a catalog refresh failure', async () => {
    await start()
    sendRequest.mockRejectedValueOnce(new Error('Refresh unavailable'))
    await usePluginStore.getState().fetchPlugins()
    sendRequest.mockResolvedValueOnce({ plugin: { ...plugin, enabled: false }, snapshotRevision: 1, outcome: 'applied' })
    await usePluginStore.getState().togglePluginEnabled(plugin.id, false)
    await waitFor(() => expect(oldDispose).toHaveBeenCalledOnce())
    expect(useDesktopPluginRegistry.getState().generations.size).toBe(0)
  })

  it('disposes a late activation instead of publishing it into another workspace', async () => {
    await start()
    const intermediate = deferred<DesktopPluginActivation>()
    activate.mockReturnValueOnce(intermediate.promise)
    sendRequest.mockResolvedValueOnce({ plugins: [{ ...plugin, version: '2.0.0' }], snapshotRevision: 0 })
    promote('b')
    await usePluginStore.getState().fetchPlugins()
    await waitFor(() => expect(activate).toHaveBeenCalledTimes(2))
    const final = deferred<DesktopPluginActivation>()
    activate.mockReturnValueOnce(final.promise)
    promote('c')
    await usePluginStore.getState().fetchPlugins()
    await waitFor(() => expect(activate).toHaveBeenCalledTimes(3))
    const staleDispose = vi.fn()
    intermediate.resolve(activation(staleDispose))
    await waitFor(() => expect(staleDispose).toHaveBeenCalledOnce())
    expect(useDesktopPluginRegistry.getState().generations.size).toBe(0)
    final.resolve(activation())
    await waitFor(() => expect(useDesktopPluginRegistry.getState().generations.size).toBe(1))
  })
})
