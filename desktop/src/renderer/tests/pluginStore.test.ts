// @vitest-environment jsdom

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { usePluginStore, type PluginEntry } from '../stores/pluginStore'
import { installDesktopApiMock } from './desktopApiMock'

const plugin = (overrides: Partial<PluginEntry> = {}): PluginEntry => ({
  id: 'acme.review-core',
  displayName: 'Review Core',
  description: 'Native review tools.',
  version: '1.0.0',
  enabled: false,
  installed: true,
  installable: false,
  removable: true,
  source: 'workspace',
  rootPath: '/workspace/.craft/plugins/acme.review-core',
  functions: [],
  skills: [],
  mcpServers: [],
  lspServers: [],
  dotnet: {
    entryAssembly: './dotnet/ReviewCore.dll',
    entryType: 'ReviewCore.Plugin',
    exportedApiAssemblies: [],
    minHostVersion: '0.5.0'
  },
  dotnetRuntime: {
    state: 'blocked',
    generationId: null,
    blockers: [{ code: 'PluginUntrusted', message: 'No trust grant.' }],
    leakedGenerations: 0,
    restartRecommended: false,
    trustStatus: 'untrusted'
  },
  ...overrides
})

const trusted = (): PluginEntry => plugin({
  enabled: true,
  dotnetRuntime: {
    state: 'active',
    generationId: 'gen-1',
    blockers: [],
    leakedGenerations: 0,
    restartRecommended: false,
    trustStatus: 'trusted'
  }
})

const sendRequest = vi.fn()

function resetStore(patch: Partial<ReturnType<typeof usePluginStore.getState>> = {}): void {
  usePluginStore.setState({
    plugins: [],
    marketplaces: [],
    diagnostics: [],
    loading: false,
    error: null,
    selectedPluginId: null,
    selectedPlugin: null,
    detailLoading: false,
    snapshotRevision: 0,
    completeSnapshotRevision: 0,
    ...patch
  })
}

describe('pluginStore snapshot revisions', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    resetStore()
    installDesktopApiMock({ appServer: { sendRequest } })
  })

  it('refreshes a complete list for a newer invalidation and ignores duplicates', async () => {
    sendRequest
      .mockResolvedValueOnce({ plugins: [plugin()], diagnostics: [], snapshotRevision: 3 })
      .mockResolvedValueOnce({ plugins: [trusted()], diagnostics: [], snapshotRevision: 5 })

    await usePluginStore.getState().fetchPlugins()
    expect(usePluginStore.getState().snapshotRevision).toBe(3)
    expect(usePluginStore.getState().completeSnapshotRevision).toBe(3)

    usePluginStore.getState().handleSnapshotUpdated(5)
    await vi.waitFor(() => expect(usePluginStore.getState().plugins[0]?.enabled).toBe(true))
    expect(usePluginStore.getState().snapshotRevision).toBe(5)
    expect(usePluginStore.getState().completeSnapshotRevision).toBe(5)

    usePluginStore.getState().handleSnapshotUpdated(5)
    expect(sendRequest).toHaveBeenCalledTimes(2)
  })

  it('does not apply a list older than an observed invalidation', async () => {
    sendRequest.mockResolvedValue({ plugins: [plugin()], diagnostics: [], snapshotRevision: 4 })
    resetStore({ snapshotRevision: 6, completeSnapshotRevision: 3, plugins: [trusted()] })

    await usePluginStore.getState().fetchPlugins()

    expect(usePluginStore.getState().snapshotRevision).toBe(6)
    expect(usePluginStore.getState().plugins[0]?.enabled).toBe(true)
  })

  it('reports a malformed list response instead of treating it as the current snapshot', async () => {
    sendRequest.mockResolvedValue({ plugins: [plugin()], diagnostics: [] })

    await usePluginStore.getState().fetchPlugins()

    expect(usePluginStore.getState().plugins).toEqual([])
    expect(usePluginStore.getState().error).toContain('snapshotRevision')
  })

  it('drops a list response superseded by a later request', async () => {
    const state = usePluginStore.getState()
    sendRequest
      .mockImplementationOnce(async () => ({ plugins: [plugin()], diagnostics: [], snapshotRevision: 7 }))
      .mockImplementationOnce(async () => ({ plugins: [trusted()], diagnostics: [], snapshotRevision: 7 }))

    await Promise.all([state.fetchPlugins(), state.fetchPlugins()])

    expect(usePluginStore.getState().plugins).toHaveLength(1)
    expect(usePluginStore.getState().plugins[0]?.enabled).toBe(true)
  })

  it('refreshes the complete baseline when a mutation already observed the same revision', async () => {
    resetStore({ plugins: [plugin()], snapshotRevision: 3, completeSnapshotRevision: 3 })
    sendRequest
      .mockResolvedValueOnce({ outcome: 'applied', plugin: trusted(), affectedPlugins: [], snapshotRevision: 5 })
      .mockResolvedValueOnce({ plugins: [trusted()], diagnostics: [], snapshotRevision: 5 })

    await usePluginStore.getState().setPluginTrusted('acme.review-core', true)
    expect(usePluginStore.getState().snapshotRevision).toBe(5)
    expect(usePluginStore.getState().completeSnapshotRevision).toBe(3)

    usePluginStore.getState().handleSnapshotUpdated(5)
    await vi.waitFor(() => expect(usePluginStore.getState().completeSnapshotRevision).toBe(5))
    expect(sendRequest).toHaveBeenNthCalledWith(2, 'plugin/list', { includeDisabled: true })
  })
})

describe('pluginStore operation outcomes', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    resetStore()
    installDesktopApiMock({ appServer: { sendRequest } })
  })

  it('applies the projections of every plugin a batch changed', async () => {
    const consumer = plugin({ id: 'acme.review-ui', displayName: 'Review UI', enabled: true })
    resetStore({ plugins: [plugin(), consumer], snapshotRevision: 1, completeSnapshotRevision: 1 })
    sendRequest.mockResolvedValueOnce({
      outcome: 'applied',
      plugin: trusted(),
      affectedPlugins: [{
        id: 'acme.review-ui',
        installed: true,
        enabled: true,
        dotnetRuntime: {
          state: 'active',
          generationId: 'gen-2',
          blockers: [],
          leakedGenerations: 0,
          restartRecommended: false,
          trustStatus: 'trusted'
        }
      }],
      diagnostics: [],
      snapshotRevision: 2
    })

    const result = await usePluginStore.getState().setPluginTrusted('acme.review-core', true)

    expect(result.outcome).toBe('applied')
    expect(sendRequest).toHaveBeenCalledWith('plugin/setTrusted', { id: 'acme.review-core', trusted: true })
    const state = usePluginStore.getState()
    expect(state.plugins.find((entry) => entry.id === 'acme.review-core')?.dotnetRuntime?.state).toBe('active')
    expect(state.plugins.find((entry) => entry.id === 'acme.review-ui')?.dotnetRuntime?.generationId).toBe('gen-2')
    expect(state.snapshotRevision).toBe(2)
  })

  it('forwards trust revocation intent', async () => {
    resetStore({ plugins: [trusted()], snapshotRevision: 2, completeSnapshotRevision: 2 })
    sendRequest.mockResolvedValueOnce({
      outcome: 'applied',
      plugin: plugin(),
      affectedPlugins: [],
      diagnostics: [],
      snapshotRevision: 3
    })

    await usePluginStore.getState().setPluginTrusted('acme.review-core', false)

    expect(sendRequest).toHaveBeenCalledWith('plugin/setTrusted', {
      id: 'acme.review-core',
      trusted: false
    })
    expect(usePluginStore.getState().plugins[0]?.dotnetRuntime?.trustStatus).toBe('untrusted')
  })

  it('keeps the rejected state and its reason when an operation is not applied', async () => {
    resetStore({ plugins: [plugin()], snapshotRevision: 4, completeSnapshotRevision: 4 })
    sendRequest.mockResolvedValueOnce({
      outcome: 'notApplied',
      plugin: plugin(),
      affectedPlugins: [],
      diagnostics: [{
        severity: 'error',
        code: 'PluginTrustNotPersisted',
        message: 'The trust grant could not be written.'
      }],
      snapshotRevision: 4
    })

    const result = await usePluginStore.getState().setPluginTrusted('acme.review-core', true)

    expect(result.outcome).toBe('notApplied')
    expect(usePluginStore.getState().plugins[0]?.dotnetRuntime?.trustStatus).toBe('untrusted')
    expect(usePluginStore.getState().plugins[0]?.enabled).toBe(false)
    expect(sendRequest).toHaveBeenCalledTimes(1)
  })

  it('writes nothing beyond the returned record for a no-op enable', async () => {
    resetStore({ plugins: [trusted()], snapshotRevision: 9, completeSnapshotRevision: 9 })
    sendRequest.mockResolvedValueOnce({
      outcome: 'noChange',
      plugin: trusted(),
      affectedPlugins: [],
      diagnostics: [],
      snapshotRevision: 9
    })

    const result = await usePluginStore.getState().togglePluginEnabled('acme.review-core', true)

    expect(result.outcome).toBe('noChange')
    expect(sendRequest).toHaveBeenCalledTimes(1)
    expect(usePluginStore.getState().snapshotRevision).toBe(9)
  })

  it('re-reads the list when a mutation leaves no record for the id', async () => {
    resetStore({ plugins: [plugin()], selectedPluginId: 'acme.review-core' })
    sendRequest
      .mockResolvedValueOnce({ outcome: 'applied', plugin: null, affectedPlugins: [], snapshotRevision: 2 })
      .mockResolvedValueOnce({ plugins: [], diagnostics: [], snapshotRevision: 2 })

    await usePluginStore.getState().removePlugin('acme.review-core')

    expect(sendRequest).toHaveBeenNthCalledWith(2, 'plugin/list', { includeDisabled: true })
    expect(usePluginStore.getState().plugins).toHaveLength(0)
    expect(usePluginStore.getState().selectedPluginId).toBeNull()
  })

  it('defaults the runtime projection fields the Host may omit', async () => {
    sendRequest.mockResolvedValueOnce({
      plugins: [{
        ...plugin(),
        dependencies: undefined,
        dotnetRuntime: { state: 'reclaiming', trustStatus: 'trusted' }
      }],
      diagnostics: [],
      snapshotRevision: 1
    })

    await usePluginStore.getState().fetchPlugins()

    const runtime = usePluginStore.getState().plugins[0]?.dotnetRuntime
    expect(runtime?.state).toBe('reclaiming')
    expect(runtime?.blockers).toEqual([])
    expect(runtime?.leakedGenerations).toBe(0)
    expect(runtime?.restartRecommended).toBe(false)
    expect(usePluginStore.getState().plugins[0]?.dependencies).toEqual([])
  })
})
