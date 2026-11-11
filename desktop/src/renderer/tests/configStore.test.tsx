import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { InstantInterruptRow } from '../components/settings/panels/instantInterrupt/InstantInterruptRow'
import { useComposerPreferencesStore } from '../stores/composerPreferencesStore'
import { configKeyPath, readConfigValue, useConfigStore } from '../stores/configStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useToastStore } from '../stores/toastStore'
import { workspaceResourceKey, type WorkspaceResourceScope } from '../stores/workspaceResourceScope'

const sendRequest = vi.fn()
const sendConnectionRequest = vi.fn()
const scope: WorkspaceResourceScope = { projectKey: '/workspace', connectionKey: '/workspace', connectionId: 'connection-1' }
const otherScope: WorkspaceResourceScope = { projectKey: '/other', connectionKey: '/other', connectionId: 'connection-2' }
let serverConfig: Record<string, unknown>

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (error: unknown) => void
  const promise = new Promise<T>((done, fail) => { resolve = done; reject = fail })
  return { promise, resolve, reject }
}

function renderRow(): void {
  render(<LocaleProvider><InstantInterruptRow /></LocaleProvider>)
}

function configReads(): number {
  return sendRequest.mock.calls.filter(([method]) => method === 'config/read').length
}

beforeEach(() => {
  serverConfig = { InstantInterruptEnabled: false, Tools: { CodeMode: { Mode: 'Only' } } }
  sendRequest.mockReset().mockImplementation(async (method: string) => {
    if (method === 'config/read') return { config: serverConfig, origins: {} }
    return { status: 'ok', version: 'sha256:1', filePath: '/remote/.craft/config.json' }
  })
  sendConnectionRequest.mockReset().mockImplementation((_key, method, params, timeout) => sendRequest(method, params, ...(timeout == null ? [] : [timeout])))
  Object.defineProperty(window, 'api', {
    configurable: true,
    value: {
      appServer: { sendRequest, sendConnectionRequest },
      settings: { get: async () => ({ locale: 'en' }), set: async () => {} }
    }
  })
  useConfigStore.getState().reset()
  useConfigStore.getState().activate(scope)
  useToastStore.setState({ toasts: [] })
  useComposerPreferencesStore.setState({ followUpQueueMode: 'steer' })
  useConnectionStore.setState({
    status: 'connected', connectionKey: scope.connectionKey, connectionId: scope.connectionId,
    capabilities: { workspaceConfigManagement: true }
  })
})

describe('configuration client', () => {
  it('shows the connected server value without reading local files', async () => {
    renderRow()

    await waitFor(() => expect(screen.getByRole('switch')).toHaveAttribute('aria-checked', 'false'))
    expect(sendRequest).toHaveBeenCalledWith('config/read', {})
    expect(sendRequest.mock.calls.every(([method]) => method === 'config/read')).toBe(true)
  })

  it('writes the key path when a setting is toggled', async () => {
    renderRow()
    await waitFor(() => expect(screen.getByRole('switch')).toHaveAttribute('aria-checked', 'false'))

    fireEvent.click(screen.getByRole('switch'))

    expect(screen.getByRole('switch')).toHaveAttribute('aria-checked', 'true')
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith(
      'config/value/write',
      { keyPath: 'InstantInterruptEnabled', value: true, mergeStrategy: 'replace' },
      20_000
    ))
  })

  it('restores the previous value and reports an error when the write fails', async () => {
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'config/read') return { config: serverConfig, origins: {} }
      throw new Error('configValidationError')
    })
    renderRow()
    await waitFor(() => expect(screen.getByRole('switch')).toHaveAttribute('aria-checked', 'false'))

    fireEvent.click(screen.getByRole('switch'))

    await waitFor(() => expect(useToastStore.getState().toasts.some((toast) => toast.type === 'error')).toBe(true))
    expect(screen.getByRole('switch')).toHaveAttribute('aria-checked', 'false')
  })

  it('writes several key paths in one batch', async () => {
    await useConfigStore.getState().ensureLoaded()

    await useConfigStore.getState().write([
      { keyPath: 'ProviderId', value: 'openai' },
      { keyPath: 'ProviderPreferences.openai', value: { model: 'gpt-5' } }
    ])

    expect(sendRequest).toHaveBeenCalledWith('config/batchWrite', {
      edits: [
        { keyPath: 'ProviderId', value: 'openai', mergeStrategy: 'replace' },
        { keyPath: 'ProviderPreferences.openai', value: { model: 'gpt-5' }, mergeStrategy: 'replace' }
      ]
    }, 20_000)
  })

  it('refreshes when a change names a key path it holds', async () => {
    renderRow()
    await waitFor(() => expect(screen.getByRole('switch')).toHaveAttribute('aria-checked', 'false'))
    serverConfig = { ...serverConfig, InstantInterruptEnabled: true }

    act(() => useConfigStore.getState().handleConfigChanged(['InstantInterruptEnabled']))

    await waitFor(() => expect(screen.getByRole('switch')).toHaveAttribute('aria-checked', 'true'))
    expect(configReads()).toBe(2)
  })

  it('ignores regions outside the configuration it holds', async () => {
    await useConfigStore.getState().ensureLoaded()

    useConfigStore.getState().handleConfigChanged(['providers', 'skills', 'plugins.config', 'memory'])

    expect(configReads()).toBe(1)
  })

  it('loads the configuration when a setting changes during the initial read', async () => {
    let releaseInitialRead!: () => void
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'config/read') {
        if (configReads() === 1) await new Promise<void>((resolve) => { releaseInitialRead = resolve })
        return { config: serverConfig, origins: {} }
      }
      return { status: 'ok', version: 'sha256:1', filePath: '/remote/.craft/config.json' }
    })

    const initialRead = useConfigStore.getState().ensureLoaded()
    await useConfigStore.getState().write([{ keyPath: 'InstantInterruptEnabled', value: true }])
    releaseInitialRead()
    await initialRead

    expect(useConfigStore.getState().config).toEqual(serverConfig)
  })

  it('discards old workspace reads without clearing the replacement in-flight read', async () => {
    let resolvePrevious!: (value: unknown) => void
    let resolveCurrent!: (value: unknown) => void
    sendRequest
      .mockImplementationOnce(() => new Promise((resolve) => { resolvePrevious = resolve }))
      .mockImplementationOnce(() => new Promise((resolve) => { resolveCurrent = resolve }))
    const store = useConfigStore.getState()
    const previousLoad = store.ensureLoaded()
    store.reset()
    store.activate(scope)
    const currentLoad = store.ensureLoaded()
    resolvePrevious({ config: { ProviderId: 'previous' } })
    await previousLoad

    expect(useConfigStore.getState().config).toBeNull()
    const duplicateLoad = store.ensureLoaded()
    expect(configReads()).toBe(2)
    resolveCurrent({ config: { ProviderId: 'current' } })
    await Promise.all([currentLoad, duplicateLoad])
    expect(useConfigStore.getState().config).toEqual({ ProviderId: 'current' })
  })

  it('reuses loaded data and in-flight reads across consumers and remounts', async () => {
    const pending = deferred<unknown>()
    sendRequest.mockReturnValueOnce(pending.promise)
    const store = useConfigStore.getState()
    const first = store.ensureLoaded()
    expect(store.ensureLoaded()).toBe(first)
    pending.resolve({ config: serverConfig })
    await first

    const row = render(<LocaleProvider><InstantInterruptRow /></LocaleProvider>)
    row.unmount()
    renderRow()
    await store.ensureLoaded()

    expect(configReads()).toBe(1)
    expect(sendConnectionRequest).toHaveBeenCalledWith(
      scope.connectionKey, 'config/read', {}, undefined, { connectionId: scope.connectionId }
    )
  })

  it('restores a workspace immediately and revalidates without clearing usable data', async () => {
    const store = useConfigStore.getState()
    await store.refresh()
    store.activate(otherScope)
    serverConfig = { ProviderId: 'other' }
    await store.refresh()
    store.activate(scope)

    expect(useConfigStore.getState()).toMatchObject({
      config: { InstantInterruptEnabled: false }, status: 'ready', confirmed: false
    })
    const pending = deferred<unknown>()
    sendRequest.mockReturnValueOnce(pending.promise)
    const refresh = store.refresh()
    expect(useConfigStore.getState()).toMatchObject({
      config: { InstantInterruptEnabled: false }, status: 'ready'
    })
    pending.resolve({ config: { InstantInterruptEnabled: true } })
    await refresh
    expect(useConfigStore.getState()).toMatchObject({
      config: { InstantInterruptEnabled: true }, status: 'ready', confirmed: true
    })
  })

  it('keeps a successful snapshot after a failed refresh and supports retry', async () => {
    const store = useConfigStore.getState()
    await store.refresh()
    sendRequest.mockRejectedValueOnce(new Error('offline'))

    await store.refresh()

    expect(useConfigStore.getState()).toMatchObject({
      config: serverConfig, status: 'ready', error: 'offline'
    })
    await store.refresh()
    expect(useConfigStore.getState()).toMatchObject({ error: null, confirmed: true })
  })

  it('exposes a cold-load failure without retrying on every render', async () => {
    sendRequest.mockRejectedValueOnce(new Error('timeout'))
    renderRow()
    await waitFor(() => expect(useConfigStore.getState().status).toBe('error'))
    expect(useConfigStore.getState()).toMatchObject({ config: null, error: 'timeout' })
    renderRow()
    expect(configReads()).toBe(1)

    await act(async () => { await useConfigStore.getState().refresh() })

    expect(useConfigStore.getState()).toMatchObject({ config: serverConfig, status: 'ready', error: null })
  })

  it('lets a late response update only its original live workspace', async () => {
    const pending = deferred<unknown>()
    sendRequest.mockReturnValueOnce(pending.promise)
    const store = useConfigStore.getState()
    const originalRead = store.refresh()
    store.activate(otherScope)
    await store.refresh()
    pending.resolve({ config: { ProviderId: 'original' } })
    await originalRead

    expect(useConfigStore.getState().config).toEqual(serverConfig)
    store.activate(scope)
    expect(useConfigStore.getState().config).toEqual({ ProviderId: 'original' })
    expect(useConfigStore.getState().confirmed).toBe(false)
  })

  it('invalidates snapshots and requests when the same project has a new connection lifetime', async () => {
    const pending = deferred<unknown>()
    sendRequest.mockReturnValueOnce(pending.promise)
    const store = useConfigStore.getState()
    const oldRead = store.refresh()
    store.activate({ ...scope, connectionId: 'replacement' })
    await store.refresh()
    pending.resolve({ config: { ProviderId: 'obsolete' } })
    await oldRead

    expect(useConfigStore.getState().config).toEqual(serverConfig)
    store.activate(scope)
    expect(useConfigStore.getState().config).toBeNull()
  })

  it('prunes inactive connection entries without evicting the active snapshot', async () => {
    const store = useConfigStore.getState()
    await store.refresh()
    store.activate(otherScope)
    await store.refresh()
    store.prune(new Set())

    expect(useConfigStore.getState().config).toEqual(serverConfig)
    store.activate(scope)
    expect(useConfigStore.getState().config).toBeNull()
    store.prune(new Set([workspaceResourceKey(otherScope)]))
    store.activate(otherScope)
    expect(useConfigStore.getState().config).toEqual(serverConfig)
  })

  it('revalidates configuration changes for previously absent keys', async () => {
    const store = useConfigStore.getState()
    await store.refresh()
    serverConfig = { ...serverConfig, NewSetting: true }

    store.handleConfigChanged(['NewSetting'])
    await store.refresh()

    expect(useConfigStore.getState().config).toEqual(serverConfig)
    expect(configReads()).toBe(2)
  })

  it('does not lose a change notification during the initial configuration read', async () => {
    const pending = deferred<unknown>()
    sendRequest.mockReturnValueOnce(pending.promise)
    const store = useConfigStore.getState()
    const read = store.ensureLoaded()
    serverConfig = { AddedWhileLoading: true }
    store.handleConfigChanged(['AddedWhileLoading'])
    pending.resolve({ config: { AddedWhileLoading: false } })
    await read

    expect(useConfigStore.getState()).toMatchObject({ config: serverConfig, confirmed: true })
    expect(configReads()).toBe(2)
  })

  it('detaches without discarding snapshots and rejects writes without an active workspace', async () => {
    const store = useConfigStore.getState()
    await store.refresh()
    store.activate(null)

    await expect(store.write([{ keyPath: 'ProviderId', value: 'wrong-target' }])).rejects.toThrow()
    await store.ensureLoaded()
    expect(useConfigStore.getState().config).toBeNull()
    expect(configReads()).toBe(1)
    store.activate(scope)
    expect(useConfigStore.getState().config).toEqual(serverConfig)
  })

  it('rolls back concurrent failed writes without restoring another failed optimistic value', async () => {
    const store = useConfigStore.getState()
    await store.refresh()
    const firstWrite = deferred<unknown>()
    const secondWrite = deferred<unknown>()
    const finalRead = deferred<unknown>()
    sendRequest
      .mockReturnValueOnce(firstWrite.promise)
      .mockReturnValueOnce(secondWrite.promise)
      .mockReturnValueOnce(finalRead.promise)
    const first = store.write([{ keyPath: 'InstantInterruptEnabled', value: true }]).catch(() => undefined)
    const second = store.write([{ keyPath: 'InstantInterruptEnabled', value: 'latest' }]).catch(() => undefined)

    firstWrite.reject(new Error('first write failed'))
    await first
    expect(useConfigStore.getState().config?.InstantInterruptEnabled).toBe('latest')
    secondWrite.reject(new Error('second write failed'))
    await waitFor(() => expect(useConfigStore.getState().pendingKeyPaths).toEqual([]))
    expect(useConfigStore.getState().config?.InstantInterruptEnabled).toBe(false)
    finalRead.resolve({ config: serverConfig })
    await second
  })

  it('keeps a newer successful edit when an earlier write fails later', async () => {
    const store = useConfigStore.getState()
    await store.refresh()
    const firstWrite = deferred<unknown>()
    const secondWrite = deferred<unknown>()
    const finalRead = deferred<unknown>()
    sendRequest
      .mockReturnValueOnce(firstWrite.promise)
      .mockReturnValueOnce(secondWrite.promise)
      .mockReturnValueOnce(finalRead.promise)
    const first = store.write([{ keyPath: 'InstantInterruptEnabled', value: true }]).catch(() => undefined)
    const second = store.write([{ keyPath: 'InstantInterruptEnabled', value: 'latest' }])

    secondWrite.resolve({})
    await second
    expect(useConfigStore.getState().config?.InstantInterruptEnabled).toBe('latest')
    firstWrite.reject(new Error('late failure'))
    await waitFor(() => expect(useConfigStore.getState().pendingKeyPaths).toEqual([]))
    expect(useConfigStore.getState().config?.InstantInterruptEnabled).toBe('latest')
    finalRead.resolve({ config: { ...serverConfig, InstantInterruptEnabled: 'latest' } })
    await first
  })

  it('finishes writes and their refresh on the originating workspace after a switch', async () => {
    const store = useConfigStore.getState()
    await store.refresh()
    const pendingWrite = deferred<unknown>()
    sendRequest.mockReturnValueOnce(pendingWrite.promise)
    const write = store.write([{ keyPath: 'InstantInterruptEnabled', value: true }])
    store.activate(otherScope)
    await store.refresh()
    pendingWrite.resolve({})
    await write

    expect(useConfigStore.getState().config).toEqual(serverConfig)
    expect(sendConnectionRequest.mock.calls.at(-1)).toEqual([
      scope.connectionKey, 'config/read', {}, undefined, { connectionId: scope.connectionId }
    ])
    store.activate(scope)
    expect(useConfigStore.getState().pendingKeyPaths).toEqual([])
  })

  it('quotes key path segments that contain dots', async () => {
    serverConfig = { ProviderPreferences: { 'openai.personal': { model: 'gpt-5' } } }
    await useConfigStore.getState().ensureLoaded()

    const keyPath = configKeyPath('ProviderPreferences', 'openai.personal')

    expect(keyPath).toBe('ProviderPreferences."openai.personal"')
    expect(readConfigValue(useConfigStore.getState().config, `${keyPath}.model`)).toBe('gpt-5')
  })
})
