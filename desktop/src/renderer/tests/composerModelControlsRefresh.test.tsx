// @vitest-environment jsdom

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, render, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConnectionStore } from '../stores/connectionStore'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import { useProvidersStore } from '../stores/providersStore'
import { useThreadStore } from '../stores/threadStore'
import { useComposerModelControls, type ComposerModelControls } from '../components/conversation/useComposerModelControls'
import { installDesktopApiMock } from './desktopApiMock'
import type { Thread, ThreadConfigurationWire } from '../types/thread'

const listModels = vi.fn()
const sendRequest = vi.fn()
const getCore = vi.fn()

let controls: ComposerModelControls | null = null

function Probe(): JSX.Element {
  const activeThread = useThreadStore((s) => s.activeThread)
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  controls = useComposerModelControls({ workspacePath: 'C:\\ws', activeThread, activeThreadId })
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
    vi.clearAllMocks()
    useModelCatalogStore.getState().reset()
    useProvidersStore.getState().reset()
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
    getCore.mockResolvedValue({ workspace: { providerId: 'openai', providerPreferences: {} }, userDefaults: {} })
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/read') return { thread: { configuration: { providerId: 'openai', model: 'gpt-5.4' } } }
      if (method === 'provider/list') return { providers: [] }
      return {}
    })
    installDesktopApiMock({
      appServer: { listModels, sendRequest, onNotification: () => () => undefined },
      workspaceConfig: { getCore },
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

  it('reloads the catalog when the thread switches provider', async () => {
    render(<LocaleProvider><Probe /></LocaleProvider>)
    await waitFor(() => expect(listModels).toHaveBeenCalled())
    const before = listModels.mock.calls.length

    await deliverThreadSnapshot({ providerId: 'anthropic', model: 'gpt-5.4' })

    await waitFor(() => expect(listModels.mock.calls.length).toBe(before + 1))
    expect(listModels).toHaveBeenLastCalledWith('anthropic')
  })
})
