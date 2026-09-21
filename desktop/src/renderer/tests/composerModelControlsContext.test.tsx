// @vitest-environment jsdom

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, renderHook, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
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
  contextWindow: { mode: 'max' }
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
      workspacePath: 'C:\\ws',
      mode,
      activeThread: mode === 'thread' ? activeThread : null,
      activeThreadId: mode === 'thread' ? activeThreadId : null
    })
  }, { wrapper: LocaleProvider })
}

describe('composer MAX context persistence', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    configurations.clear()
    configurations.set('thread-1', structuredClone(initialConfig))
    configurations.set('thread-2', structuredClone(initialConfig))
    useModelCatalogStore.getState().reset()
    useProvidersStore.getState().reset()
    selectThread('thread-1')
    useConnectionStore.setState({
      status: 'connected',
      capabilities: { modelCatalogManagement: true, workspaceConfigManagement: true, providerManagement: true }
    })
    sendRequest.mockImplementation(async (method: string, params: { threadId: string; config?: ThreadConfigurationWire }) => {
      if (method === 'provider/list') return { providers: [] }
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
      workspaceConfig: {
        getCore: async () => ({
          workspace: { providerId: 'openai', providerPreferences: { openai: {
            model: 'gpt-5.4', reasoning: initialConfig.reasoning, speed: 'standard', contextWindow: { mode: 'max' }
          } } },
          userDefaults: {}
        })
      },
      settings: { get: async () => null, set: async () => undefined }
    })
  })

  it('persists MAX off across effort changes, snapshots, navigation, and remounts', async () => {
    const hook = mountControls()
    await waitFor(() => expect(hook.result.current.contextMode).toBe('max'))
    await act(async () => hook.result.current.onContextModeChange('default'))
    await waitFor(() => expect(configurations.get('thread-1')?.contextWindow).toEqual({ mode: 'default' }))
    await act(async () => hook.result.current.onReasoningChange('medium'))
    await waitFor(() => expect(hook.result.current.reasoningValue).toBe('medium'))
    expect(configurations.get('thread-1')?.reasoning?.effort).toBe('medium')
    expect(configurations.get('thread-1')?.contextWindow).toEqual({ mode: 'default' })
    await act(async () => selectThread('thread-1'))
    expect(hook.result.current.contextMode).toBe('default')
    await act(async () => selectThread('thread-2'))
    await waitFor(() => expect(hook.result.current.contextMode).toBe('max'))
    await act(async () => selectThread('thread-1'))
    await waitFor(() => expect(hook.result.current.contextMode).toBe('default'))
    hook.unmount()
    const remounted = mountControls()
    await waitFor(() => expect(remounted.result.current.modelName).toBe('gpt-5.4'))
    expect(remounted.result.current.contextMode).toBe('default')
    expect(configurations.get('thread-2')?.contextWindow).toEqual({ mode: 'max' })
  })

  it.each([undefined, null])('does not inherit MAX for an existing thread with contextWindow=%s', async (contextWindow) => {
    configurations.set('thread-1', { ...initialConfig, contextWindow })
    selectThread('thread-1')
    const hook = mountControls()
    await waitFor(() => expect(hook.result.current.modelName).toBe('gpt-5.4'))
    expect(hook.result.current.contextMode).toBe('default')
  })

  it('inherits untouched draft preferences but submits an explicit off selection', async () => {
    const hook = mountControls('detached')
    await waitFor(() => expect(hook.result.current.contextMode).toBe('max'))
    expect(hook.result.current.threadStartConfig.contextWindow).toBeUndefined()
    await act(async () => hook.result.current.onContextModeChange('default'))
    expect(hook.result.current.threadStartConfig.contextWindow).toEqual({ mode: 'default' })
    await act(async () => hook.result.current.onReasoningChange('medium'))
    expect(hook.result.current.contextMode).toBe('default')
    expect(hook.result.current.threadStartConfig.contextWindow).toEqual({ mode: 'default' })
    expect(sendRequest.mock.calls.some(([method]) => method === 'thread/config/update')).toBe(false)
  })

  it('stores default explicitly when switching to a model without MAX support', async () => {
    const hook = mountControls()
    await waitFor(() => expect(hook.result.current.contextMode).toBe('max'))
    await act(async () => hook.result.current.onModelChange('unknown-model'))
    await waitFor(() => expect(hook.result.current.modelName).toBe('unknown-model'))
    expect(configurations.get('thread-1')?.contextWindow).toEqual({ mode: 'default' })
    await act(async () => selectThread('thread-1'))
    expect(hook.result.current.contextMode).toBe('default')
  })

  it('restores MAX when saving the off selection fails', async () => {
    const hook = mountControls()
    await waitFor(() => expect(hook.result.current.contextMode).toBe('max'))
    const normalRequest = sendRequest.getMockImplementation()!
    sendRequest.mockImplementation(async (method, params) => {
      if (method === 'thread/config/update') throw new Error('save failed')
      return normalRequest(method, params)
    })
    await act(async () => hook.result.current.onContextModeChange('default'))
    await waitFor(() => expect(hook.result.current.contextMode).toBe('max'))
    expect(configurations.get('thread-1')?.contextWindow).toEqual({ mode: 'max' })
  })
})
