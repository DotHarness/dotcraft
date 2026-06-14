import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { AgentBuilderView } from '../components/agents/AgentBuilderView'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import { useProvidersStore } from '../stores/providersStore'
import { useSubAgentStore } from '../stores/subAgentStore'
import { useThreadStore } from '../stores/threadStore'
import { useToastStore } from '../stores/toastStore'
import { useUIStore } from '../stores/uiStore'

const appServerSendRequest = vi.fn()

function renderView(): void {
  render(
    <LocaleProvider>
      <AgentBuilderView />
    </LocaleProvider>
  )
}

describe('AgentBuilderView intro composer', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'agent/profiles/list') return { profiles: [] }
      if (method === 'tool/list') return { tools: [] }
      if (method === 'skills/list') return { skills: [] }
      if (method === 'mcp/list') return { servers: [] }
      if (method === 'model/list') return { success: true, models: [{ id: 'gpt-5.5' }] }
      if (method === 'thread/start') return { thread: { id: 'builder-thread' } }
      return {}
    })

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: vi.fn(async () => ({ locale: 'en' })) },
        appServer: {
          sendRequest: appServerSendRequest,
          onNotification: vi.fn(() => vi.fn())
        },
        workspace: {
          saveImageToTemp: vi.fn(),
          pickFiles: vi.fn(async () => []),
          getPathForFile: vi.fn()
        },
        file: {
          readFile: vi.fn(async () => '{}')
        }
      }
    })

    useConversationStore.getState().reset()
    useConversationStore.setState({ workspacePath: 'F:\\dotcraft' })
    useConnectionStore.getState().reset()
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        modelCatalogManagement: true,
        workspaceConfigManagement: true
      }
    })
    useModelCatalogStore.getState().reset()
    useModelCatalogStore.setState({
      status: 'ready',
      models: [{ id: 'gpt-5.5' }],
      modelOptions: ['gpt-5.5'],
      providerId: null,
      requestedProviderId: null,
      modelListUnsupportedEndpoint: false,
      errorCode: null,
      errorMessage: null
    })
    useProvidersStore.getState().reset()
    useSubAgentStore.getState().reset()
    useThreadStore.getState().reset()
    useToastStore.setState({ toasts: [] })
    useUIStore.setState({ composerPrefill: null })
  })

  it('uses the real builder composer and starts the first builder turn from intro submit', async () => {
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: /New agent/i }))

    expect(screen.getByRole('button', { name: 'Add attachment' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Add attachment' }))
    expect(screen.queryByRole('menuitemcheckbox', { name: 'Plan mode' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Add attachment' }))

    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'thread/start')).toBe(false)

    fireEvent.click(screen.getByRole('button', { name: /Select model/i }))
    fireEvent.click(await screen.findByRole('option', { name: /gpt-5\.5/i }))
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Select model/i })).toHaveTextContent('gpt-5.5')
    })

    const textbox = screen.getByRole('textbox')
    textbox.textContent = 'Build a release notes helper'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter', code: 'Enter' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('turn/start', expect.objectContaining({
        threadId: 'builder-thread',
        input: [{ type: 'text', text: 'Build a release notes helper' }]
      }))
    })

    const lifecycleMethods = appServerSendRequest.mock.calls
      .map(([method]) => method)
      .filter((method) => [
        'thread/start',
        'agent/profiles/builderDraft/update',
        'turn/start',
        'turn/enqueue'
      ].includes(method))
    expect(lifecycleMethods).toEqual([
      'thread/start',
      'agent/profiles/builderDraft/update',
      'turn/start'
    ])
    expect(appServerSendRequest).toHaveBeenCalledWith('thread/start', expect.objectContaining({
      config: expect.objectContaining({
        agentBuilderTargetSource: 'workspace',
        model: 'gpt-5.5'
      })
    }))
    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'thread/config/update')).toBe(false)
    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'workspace/config/update')).toBe(false)
    expect(appServerSendRequest.mock.calls.some(([method, params]) =>
      method === 'turn/start' && Object.prototype.hasOwnProperty.call(params as Record<string, unknown>, 'text')
    )).toBe(false)
    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'turn/enqueue')).toBe(false)
  })

  it('does not create a builder thread when starting a blank local draft', async () => {
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: /New agent/i }))
    appServerSendRequest.mockClear()

    fireEvent.click(screen.getByRole('button', { name: /Start blank/i }))

    await waitFor(() => {
      expect(screen.getByText(/Untitled agent/i)).toBeInTheDocument()
    })
    expect(screen.getByText('How should we improve this agent?')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add attachment' })).toBeInTheDocument()
    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'thread/start')).toBe(false)
  })
})
