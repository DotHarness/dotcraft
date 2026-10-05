// @vitest-environment jsdom

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, renderHook, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConfigStore } from '../stores/configStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import { useProvidersStore } from '../stores/providersStore'
import { useThreadStore } from '../stores/threadStore'
import { useComposerModelControls } from '../components/conversation/useComposerModelControls'
import { installDesktopApiMock } from './desktopApiMock'
import type { Thread, ThreadConfigurationWire } from '../types/thread'

const sendRequest = vi.fn()
const configurations = new Map<string, ThreadConfigurationWire>()
const initialConfig: ThreadConfigurationWire = {
  providerId: 'openai',
  model: 'gpt-5.4',
  reasoning: { enabled: true, effort: 'extraHigh', output: 'full' },
}

function selectThread(id: string): void {
  useThreadStore.getState().setActiveThread({
    id,
    turns: [],
    queuedInputs: [],
    workspacePath: 'C:\\ws',
    configuration: structuredClone(configurations.get(id))
  } as unknown as Thread)
}

function mountControls(mode: 'thread' | 'detached' = 'thread') {
  return renderHook(() => {
    const activeThread = useThreadStore((s) => s.activeThread)
    const activeThreadId = useThreadStore((s) => s.activeThreadId)
    return useComposerModelControls({
      mode,
      activeThread: mode === 'thread' ? activeThread : null,
      activeThreadId: mode === 'thread' ? activeThreadId : null
    })
  }, { wrapper: LocaleProvider })
}

describe('composer reasoning persistence', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    configurations.clear()
    configurations.set('thread-1', structuredClone(initialConfig))
    configurations.set('thread-2', structuredClone(initialConfig))
    useModelCatalogStore.getState().reset()
    useProvidersStore.getState().reset()
    useConfigStore.getState().reset()
    selectThread('thread-1')
    useConnectionStore.setState({
      status: 'connected',
      capabilities: { modelCatalogManagement: true, workspaceConfigManagement: true, providerManagement: true }
    })
    sendRequest.mockImplementation(async (method: string, params: { threadId: string; config?: ThreadConfigurationWire }) => {
      if (method === 'provider/list') return { providers: [] }
      if (method === 'config/read') {
        return {
          config: {
            ProviderId: 'openai',
            ProviderPreferences: { openai: { model: 'gpt-5.4', reasoning: initialConfig.reasoning, speed: 'standard' } }
          },
          origins: {}
        }
      }
      if (method === 'thread/read') return { thread: { configuration: structuredClone(configurations.get(params.threadId)) } }
      if (method === 'thread/config/update') configurations.set(params.threadId, structuredClone(params.config!))
      return {}
    })
    installDesktopApiMock({
      appServer: {
        sendRequest,
        listModels: async () => ({ success: false, providerId: 'openai', errorCode: 'EndpointNotSupported' }),
        onNotification: () => () => undefined
      },
      settings: { get: async () => null, set: async () => undefined }
    })
  })

  it('persists Max across navigation and remounts', async () => {
    const hook = mountControls()
    await waitFor(() => expect(hook.result.current.reasoningValue).toBe('extraHigh'))
    await act(async () => hook.result.current.onReasoningChange('max'))
    await waitFor(() => expect(configurations.get('thread-1')?.reasoning?.effort).toBe('max'))
    await act(async () => selectThread('thread-2'))
    await waitFor(() => expect(hook.result.current.reasoningValue).toBe('extraHigh'))
    await act(async () => selectThread('thread-1'))
    await waitFor(() => expect(hook.result.current.reasoningValue).toBe('max'))
    hook.unmount()
    const remounted = mountControls()
    await waitFor(() => expect(remounted.result.current.reasoningValue).toBe('max'))
  })

  it('captures Max in a detached draft without updating a thread', async () => {
    const hook = mountControls('detached')
    await waitFor(() => expect(hook.result.current.modelName).toBe('gpt-5.4'))
    await act(async () => hook.result.current.onReasoningChange('max'))
    expect(hook.result.current.threadStartConfig.reasoning?.effort).toBe('max')
    expect(sendRequest.mock.calls.some(([method]) => method === 'thread/config/update')).toBe(false)
  })
})
