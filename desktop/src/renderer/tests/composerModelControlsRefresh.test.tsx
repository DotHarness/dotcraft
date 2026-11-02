// @vitest-environment jsdom

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, render, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConfigStore } from '../stores/configStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import { useProvidersStore } from '../stores/providersStore'
import { useThreadStore } from '../stores/threadStore'
import { useComposerModelControls, type ComposerModelControls } from '../components/conversation/useComposerModelControls'
import { installDesktopApiMock } from './desktopApiMock'
import type { Thread, ThreadConfigurationWire } from '../types/thread'
import { useToastStore } from '../stores/toastStore'

const listModels = vi.fn()
const sendRequest = vi.fn()

let controls: ComposerModelControls | null = null

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (error: Error) => void
  const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej })
  return { promise, resolve, reject }
}

function navigateToOtherThread(): void {
  useThreadStore.setState({
    activeThreadId: 'thread-2',
    activeThread: { ...threadWith({ providerId: 'other', model: 'other-model' }), id: 'thread-2' }
  })
}

const changes = [
  { name: 'model', apply: (current: ComposerModelControls) => current.onModelChange('next-model') },
  { name: 'provider', apply: (current: ComposerModelControls) => current.onProviderChange('anthropic') },
  { name: 'reasoning', apply: (current: ComposerModelControls) => current.onReasoningChange('high') },
  { name: 'speed', apply: (current: ComposerModelControls) => current.onSpeedChange('fast') }
]

function Probe(): JSX.Element {
  const activeThread = useThreadStore((s) => s.activeThread)
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  controls = useComposerModelControls({ activeThread, activeThreadId })
  return <div>{controls.modelName}</div>
}

function threadWith(configuration: ThreadConfigurationWire): Thread {
  return {
    id: 'thread-1',
    turns: [],
    queuedInputs: [],
    workspacePath: 'C:\\ws',
    configuration
  } as unknown as Thread
}

/** Mirrors the metadata refresh poll, which re-reads the thread and hands over fresh wire objects. */
async function deliverThreadSnapshot(configuration: ThreadConfigurationWire): Promise<void> {
  await act(async () => {
    const active = useThreadStore.getState().activeThread
    useThreadStore.getState().setActiveThread(
      JSON.parse(JSON.stringify({ ...active, configuration })) as Thread
    )
    await Promise.resolve()
  })
}

describe('useComposerModelControls catalog loading', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    useModelCatalogStore.getState().reset()
    useProvidersStore.getState().reset()
    useConfigStore.getState().reset()
    useToastStore.setState({ toasts: [] })
    useThreadStore.setState({
      activeThreadId: 'thread-1',
      activeThread: threadWith({ providerId: 'openai', model: 'gpt-5.4' })
    })
    useConnectionStore.setState({
      status: 'connected',
      capabilities: { modelCatalogManagement: true, workspaceConfigManagement: true, providerManagement: true }
    })
    // A failing catalog makes every reload observable; a successful one is cached in the renderer.
    listModels.mockResolvedValue({
      success: false,
      providerId: 'openai',
      errorCode: 'EndpointNotSupported',
      errorMessage: 'Endpoint does not support model listing.'
    })
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/read') return { thread: { configuration: { providerId: 'openai', model: 'gpt-5.4' } } }
      if (method === 'provider/list') return { providers: [] }
      if (method === 'config/read') return { config: { ProviderId: 'openai', ProviderPreferences: {} }, origins: {} }
      return {}
    })
    installDesktopApiMock({
      appServer: { listModels, sendRequest, onNotification: () => () => undefined },
      settings: { get: async () => null, set: async () => undefined }
    })
  })

  it('does not reload the catalog for an unchanged thread snapshot', async () => {
    render(<LocaleProvider><Probe /></LocaleProvider>)
    await waitFor(() => expect(listModels).toHaveBeenCalled())
    const reasoning = { enabled: true, effort: 'high', output: 'full' }

    await deliverThreadSnapshot({ providerId: 'openai', model: 'gpt-5.4', reasoning })
    const afterFirstSnapshot = listModels.mock.calls.length

    await deliverThreadSnapshot({ providerId: 'openai', model: 'gpt-5.4', reasoning })
    await deliverThreadSnapshot({ providerId: 'openai', model: 'gpt-5.4', reasoning })

    expect(listModels.mock.calls.length).toBe(afterFirstSnapshot)
  })

  it.each(changes)(
    'discards $name changes after a workspace reset while thread/read is pending', async ({ apply }) => {
      listModels.mockImplementation(async (providerId) => ({ success: true, providerId, models: [{ id: 'next-model' }] }))
      const pending = deferred<unknown>()
      const original = sendRequest.getMockImplementation()!
      sendRequest.mockImplementation((method: string, ...args: unknown[]) =>
        method === 'thread/read' ? pending.promise : original(method, ...args))
      render(<LocaleProvider><Probe /></LocaleProvider>)
      await waitFor(() => expect(controls?.providerId).toBe('openai'))
      await act(async () => apply(controls!))
      await waitFor(() => expect(sendRequest.mock.calls.some(([method]) => method === 'thread/read')).toBe(true))

      await act(async () => {
        useModelCatalogStore.getState().reset()
      })
      await act(async () => pending.resolve({ thread: { configuration: { providerId: 'openai', model: 'gpt-5.4' } } }))

      expect(sendRequest.mock.calls.some(([method]) => method === 'thread/config/update')).toBe(false)
      expect(controls?.modelName).toBe('gpt-5.4')
      expect(controls?.reasoningValue).toBe('off')
      expect(controls?.speedValue).toBe('standard')
      expect(controls?.modelDisabled).toBe(false)
      expect(useToastStore.getState().toasts).toEqual([])
    }
  )

  it('does not seed or write a provider preference when its catalog request is invalidated', async () => {
    const pendingCatalog = deferred<unknown>()
    listModels.mockImplementation((providerId: string) => providerId === 'anthropic'
      ? pendingCatalog.promise
      : Promise.resolve({ success: true, providerId, models: [{ id: 'current-model' }] }))
    render(<LocaleProvider><Probe /></LocaleProvider>)
    await waitFor(() => expect(controls?.providerId).toBe('openai'))
    await act(async () => controls!.onProviderChange('anthropic'))
    await waitFor(() => expect(listModels).toHaveBeenCalledWith('anthropic'))
    await act(async () => useModelCatalogStore.getState().reset())
    await act(async () => pendingCatalog.resolve({ success: true, providerId: 'anthropic', models: [{ id: 'wrong-model' }] }))

    expect(sendRequest.mock.calls.some(([method]) => method === 'thread/read' || method === 'thread/config/update')).toBe(false)
    expect(controls?.providerId).toBe('openai')
    expect(controls?.modelName).toBe('gpt-5.4')
  })

  it('ignores queued old-thread changes without blocking actions on the newly selected thread', async () => {
    const oldRead = deferred<unknown>()
    const original = sendRequest.getMockImplementation()!
    sendRequest.mockImplementation((method: string, params: { threadId?: string }, ...args: unknown[]) =>
      method === 'thread/read' && params.threadId === 'thread-1' ? oldRead.promise : original(method, params, ...args))
    render(<LocaleProvider><Probe /></LocaleProvider>)
    await waitFor(() => expect(controls?.providerId).toBe('openai'))
    await act(async () => {
      controls!.onModelChange('old-next-model')
      controls!.onSpeedChange('fast')
    })
    await waitFor(() => expect(sendRequest.mock.calls.some(([method]) => method === 'thread/read')).toBe(true))
    await act(async () => navigateToOtherThread())
    await act(async () => controls!.onModelChange('other-next-model'))
    await waitFor(() => expect(controls?.modelName).toBe('other-next-model'))
    await act(async () => oldRead.resolve({ thread: { configuration: { model: 'gpt-5.4' } } }))

    const writes = sendRequest.mock.calls.filter(([method]) => method === 'thread/config/update')
    expect(writes).toHaveLength(1)
    expect(writes[0][1]).toMatchObject({ threadId: 'thread-2', config: { model: 'other-next-model' } })
    expect(controls?.speedValue).toBe('standard')
  })

  it('does not apply stale state after thread/config/update completes on an old thread', async () => {
    const pendingUpdate = deferred<unknown>()
    const original = sendRequest.getMockImplementation()!
    sendRequest.mockImplementation((method: string, ...args: unknown[]) =>
      method === 'thread/config/update' ? pendingUpdate.promise : original(method, ...args))
    render(<LocaleProvider><Probe /></LocaleProvider>)
    await waitFor(() => expect(controls?.providerId).toBe('openai'))
    await act(async () => controls!.onModelChange('old-next-model'))
    await waitFor(() => expect(sendRequest.mock.calls.some(([method]) => method === 'thread/config/update')).toBe(true))
    await act(async () => navigateToOtherThread())
    await act(async () => pendingUpdate.resolve({}))

    expect(controls?.modelName).toBe('other-model')
    expect(useThreadStore.getState().activeThread?.configuration?.model).toBe('other-model')
    expect(useToastStore.getState().toasts).toEqual([])
  })

  it('does not reload an old provider catalog after a stale provider-change failure', async () => {
    const oldRead = deferred<unknown>()
    listModels.mockImplementation(async (providerId) => ({ success: true, providerId, models: [{ id: 'model' }] }))
    const original = sendRequest.getMockImplementation()!
    sendRequest.mockImplementation((method: string, ...args: unknown[]) =>
      method === 'thread/read' ? oldRead.promise : original(method, ...args))
    render(<LocaleProvider><Probe /></LocaleProvider>)
    await waitFor(() => expect(controls?.providerId).toBe('openai'))
    await act(async () => controls!.onProviderChange('anthropic'))
    await waitFor(() => expect(sendRequest.mock.calls.some(([method]) => method === 'thread/read')).toBe(true))
    await act(async () => {
      useModelCatalogStore.getState().reset()
      navigateToOtherThread()
    })
    const catalogCalls = listModels.mock.calls.length
    await act(async () => oldRead.reject(new Error('old connection closed')))

    expect(listModels).toHaveBeenCalledTimes(catalogCalls)
    expect(useToastStore.getState().toasts).toEqual([])
  })

  it('reloads the catalog when the thread switches provider', async () => {
    render(<LocaleProvider><Probe /></LocaleProvider>)
    await waitFor(() => expect(listModels).toHaveBeenCalled())
    const before = listModels.mock.calls.length

    await deliverThreadSnapshot({ providerId: 'anthropic', model: 'gpt-5.4' })

    await waitFor(() => expect(listModels.mock.calls.length).toBe(before + 1))
    expect(listModels).toHaveBeenLastCalledWith('anthropic')
  })
})
