import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
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
const appServerOnNotification = vi.fn()
type NotificationPayload = { method: string; params?: Record<string, unknown> }
let notificationHandlers: Array<(payload: NotificationPayload) => void> = []

class ResizeObserverMock {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}

Object.defineProperty(globalThis, 'ResizeObserver', {
  configurable: true,
  writable: true,
  value: ResizeObserverMock
})

const toolCatalog = [
  { name: 'WebSearch', description: 'Search the web', icon: '🔍' },
  { name: 'WebFetch', description: 'Fetch a URL', icon: '🌐' },
  { name: 'WriteFile', description: 'Write files', icon: '🖊️' },
  { name: 'ReadFile', description: 'Read files', icon: '📄' },
  { name: 'TodoWrite', description: 'Track todos', icon: '📝' },
  { name: 'Cron', description: 'Schedule jobs', icon: '⏰' },
  { name: 'RequestUserInput', description: 'Ask the user', icon: '❓' }
]

function emitNotification(payload: NotificationPayload): void {
  act(() => {
    for (const handler of [...notificationHandlers]) handler(payload)
  })
}

function renderView(): void {
  render(
    <LocaleProvider>
      <AgentBuilderView />
    </LocaleProvider>
  )
}

async function openBlankBuilder(): Promise<void> {
  renderView()
  fireEvent.click(await screen.findByRole('button', { name: /New agent/i }))
  fireEvent.click(screen.getByRole('button', { name: /Start blank/i }))
  await waitFor(() => {
    expect(screen.getByText(/Untitled agent/i)).toBeInTheDocument()
  })
}

async function addToolFromBuilder(name: string): Promise<void> {
  fireEvent.click(screen.getByRole('combobox', { name: 'Tool access' }))
  fireEvent.click(await screen.findByRole('option', { name: 'Only selected tools' }))
  fireEvent.click(screen.getByRole('button', { name: /Add tool/i }))
  fireEvent.click(await screen.findByRole('option', { name: new RegExp(name, 'i') }))
  fireEvent.keyDown(document, { key: 'Escape' })
}

async function startBuilderTurn(): Promise<void> {
  renderView()
  fireEvent.click(await screen.findByRole('button', { name: /New agent/i }))

  const textbox = screen.getByRole('textbox')
  textbox.textContent = 'Name this agent Slate'
  fireEvent.input(textbox)
  fireEvent.keyDown(textbox, { key: 'Enter', code: 'Enter' })

  await waitFor(() => {
    expect(appServerSendRequest).toHaveBeenCalledWith('turn/start', expect.objectContaining({
      threadId: 'builder-thread'
    }))
  })
  await waitFor(() => expect(appServerOnNotification).toHaveBeenCalled())
}

function emitBuilderToolStarted(callId: string, toolName: string): void {
  emitNotification({
    method: 'item/started',
    params: {
      threadId: 'builder-thread',
      item: {
        id: `tool-call-${callId}`,
        type: 'toolCall',
        payload: {
          callId,
          toolName
        }
      }
    }
  })
}

describe('AgentBuilderView intro composer', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    notificationHandlers = []
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'agent/profiles/list') return { profiles: [] }
      if (method === 'tool/list') return { tools: toolCatalog }
      if (method === 'skills/list') return { skills: [] }
      if (method === 'mcp/list') return { servers: [] }
      if (method === 'provider/list') {
        return { providers: [{ id: 'openai', displayName: 'OpenAI', protocol: 'responses', authMethod: 'apiKey' }] }
      }
      if (method === 'model/list') return { success: true, providerId: 'openai', models: [{ id: 'gpt-5.5' }] }
      if (method === 'thread/start') return { thread: { id: 'builder-thread' } }
      return {}
    })

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: vi.fn(async () => ({ locale: 'en' })) },
        appServer: {
          sendRequest: appServerSendRequest,
          onNotification: appServerOnNotification
        },
        workspace: {
          saveImageToTemp: vi.fn(),
          getPathForFile: vi.fn()
        },
        workspaceConfig: {
          getCore: vi.fn(async () => ({
            workspace: {
              providerId: 'openai',
              providerPreferences: {
                openai: {
                  model: 'gpt-5.5',
                  reasoning: { enabled: true, effort: 'high', output: 'full' },
                  speed: 'fast',
                  contextWindow: { mode: 'max' }
                }
              }
            },
            userDefaults: { providerId: null, providerPreferences: {} }
          }))
        },
        file: {
          readFile: vi.fn(async () => '{}')
        }
      }
    })
    appServerOnNotification.mockImplementation((handler: (payload: NotificationPayload) => void) => {
      notificationHandlers.push(handler)
      return () => {
        notificationHandlers = notificationHandlers.filter((existing) => existing !== handler)
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
    Object.defineProperty(window, 'innerWidth', {
      configurable: true,
      writable: true,
      value: 1600
    })
    useUIStore.setState({
      composerPrefill: null,
      agentBuilderChatWidth: 520,
      agentBuilderChatWidthRatio: 520 / 1600
    })
  })

  it('uses the real builder composer and starts the first builder turn from intro submit', async () => {
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: /New agent/i }))

    expect(screen.getByRole('button', { name: 'Open commands' })).toBeInTheDocument()
    const voiceButton = screen.getByRole('button', { name: 'Click to dictate or hold' })
    const sendButton = screen.getByRole('button', { name: 'Send message' })
    expect(Boolean(voiceButton.compareDocumentPosition(sendButton) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)

    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'thread/start')).toBe(false)

    fireEvent.click(screen.getByRole('button', { name: /Select model/i }))
    const modelMenu = screen.queryByRole('menu', { name: /Select model/i })
    if (modelMenu) {
      fireEvent.click(within(modelMenu).getByRole('menuitem', { name: /Model/i }))
    }
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
    const draftUpdateCall = appServerSendRequest.mock.calls.find(([method]) => method === 'agent/profiles/builderDraft/update')
    expect((draftUpdateCall?.[1] as { rawContent?: string } | undefined)?.rawContent).toMatch(
      /avatar: \d+/
    )
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

  it('starts a detached builder thread with an inherited personal provider preference', async () => {
    vi.mocked(window.api.workspaceConfig.getCore).mockResolvedValue({
      workspace: {
        providerId: null,
        providerPreferences: {}
      },
      userDefaults: {
        providerId: 'provider-a',
        providerPreferences: {
          'provider-a': {
            model: 'provider-model',
            reasoning: { enabled: false, effort: 'medium', output: 'full' },
            speed: 'standard',
            contextWindow: { mode: 'default' }
          }
        }
      }
    } as unknown as Awaited<ReturnType<typeof window.api.workspaceConfig.getCore>>)
    const defaultSendRequest = appServerSendRequest.getMockImplementation()
    appServerSendRequest.mockImplementation(async (method: string, params?: Record<string, unknown>) => {
      if (method === 'model/list') {
        return {
          success: true,
          providerId: params?.providerId,
          models: [{ id: 'provider-model' }]
        }
      }
      return defaultSendRequest?.(method, params)
    })

    renderView()
    fireEvent.click(await screen.findByRole('button', { name: /New agent/i }))
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Select model/i })).toHaveTextContent('provider-model')
    })

    const textbox = screen.getByRole('textbox')
    textbox.textContent = 'Build a provider-aware helper'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter', code: 'Enter' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/start', expect.objectContaining({
        config: expect.objectContaining({
          providerId: 'provider-a',
          model: 'provider-model'
        })
      }))
    })
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
    expect(screen.getByRole('button', { name: 'Open commands' })).toBeInTheDocument()
    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'thread/start')).toBe(false)
  })

  it('uses composer-style removable chips and neutral catalog icons for selected tools', async () => {
    await openBlankBuilder()

    fireEvent.click(screen.getByRole('combobox', { name: 'Tool access' }))
    fireEvent.click(await screen.findByRole('option', { name: 'Only selected tools' }))
    fireEvent.click(screen.getByRole('button', { name: /Add tool/i }))
    expect(screen.queryByText('🔍')).toBeNull()
    fireEvent.click(await screen.findByRole('option', { name: /WebSearch/i }))
    fireEvent.keyDown(document, { key: 'Escape' })

    fireEvent.click(await screen.findByRole('button', { name: 'Remove WebSearch' }))

    await waitFor(() => {
      expect(screen.queryByRole('button', { name: 'Remove WebSearch' })).not.toBeInTheDocument()
    })
  })

  it('keeps selected chips readable in preview without remove controls', async () => {
    await openBlankBuilder()
    await addToolFromBuilder('WebSearch')

    fireEvent.click(screen.getByRole('button', { name: /Preview/i }))

    expect(screen.getByText('WebSearch')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Remove WebSearch' })).not.toBeInTheDocument()
  })

  it('renders instructions as markdown in preview instead of a readonly textarea', async () => {
    await openBlankBuilder()

    const textarea = screen.getByPlaceholderText(/Give your agent instructions/i)
    fireEvent.change(textarea, { target: { value: '# Runbook\n\n- Keep scope tight' } })

    fireEvent.click(screen.getByRole('button', { name: /Preview/i }))

    expect(screen.queryByPlaceholderText(/Give your agent instructions/i)).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Runbook', level: 1 })).toBeInTheDocument()
    expect(screen.getByText('Keep scope tight')).toBeInTheDocument()
  })

  it('anchors agent editing markers to the active builder field', async () => {
    await startBuilderTurn()

    emitBuilderToolStarted('tools', 'SetAgentToolPolicy')
    await waitFor(() => {
      expect(screen.getByLabelText('Updating tools').closest('[data-builder-field-anchor="tools.policy"]')).not.toBeNull()
    })

    emitBuilderToolStarted('instructions', 'AppendAgentInstructions')
    await waitFor(() => {
      expect(screen.getByLabelText('Updating instructions').closest('[data-builder-field-anchor="instructions"]')).not.toBeNull()
    })

    emitBuilderToolStarted('providerPreference', 'SetAgentProviderPreference')
    await waitFor(() => {
      expect(screen.getByLabelText('Updating model').closest('[data-builder-field-anchor="providerPreference"]')).not.toBeNull()
    })
  })

  it('measures marker position from the active field target when a builder tool starts', async () => {
    await startBuilderTurn()

    emitBuilderToolStarted('instructions', 'AppendAgentInstructions')

    const marker = await screen.findByLabelText('Updating instructions')
    const anchor = marker.closest('[data-builder-field-anchor="instructions"]') as HTMLElement | null
    expect(anchor).not.toBeNull()
    expect(anchor?.querySelector('[data-agent-builder-marker-target]')).not.toBeNull()
    await waitFor(() => {
      expect(anchor?.style.getPropertyValue('--agent-builder-marker-x')).not.toBe('')
      expect(anchor?.style.getPropertyValue('--agent-builder-marker-y')).not.toBe('')
    })
  })

  it('resizes the builder chat pane by dragging the divider', async () => {
    await startBuilderTurn()

    const separator = document.querySelector('.drag-handle--agent-builder-chat') as HTMLElement | null
    const chatPane = document.querySelector('.agent-builder-chatpane') as HTMLElement | null
    expect(separator).not.toBeNull()
    expect(chatPane).not.toBeNull()
    const beforeWidth = Number.parseFloat(chatPane?.style.width ?? '0')

    fireEvent.pointerDown(separator!, { clientX: 1000 })
    fireEvent.pointerMove(document, { clientX: 960 })
    fireEvent.pointerUp(document)

    await waitFor(() => {
      const afterWidth = Number.parseFloat(chatPane?.style.width ?? '0')
      expect(afterWidth).toBeGreaterThan(beforeWidth)
      expect(useUIStore.getState().agentBuilderChatWidth).toBeGreaterThan(beforeWidth)
    })
  })

  it('shows a cursor marker and driving glow while the builder edits a profile field', async () => {
    await startBuilderTurn()

    emitBuilderToolStarted('call-name', 'SetAgentName')

    await waitFor(() => expect(screen.getByLabelText('Updating name')).toBeInTheDocument())
    expect(screen.getByLabelText('Updating name').closest('[data-builder-field-anchor="name"]')).not.toBeNull()
    expect(document.querySelector('.agent-builder-split-main.is-agent-driving')).toBeInTheDocument()
    expect(document.querySelector('.agent-builder-doc.is-agent-driving')).toBeInTheDocument()

    emitNotification({
      method: 'item/completed',
      params: {
        threadId: 'builder-thread',
        item: {
          id: 'tool-result-name',
          type: 'toolResult',
          payload: {
            callId: 'call-name',
            success: true,
            result: JSON.stringify({
              ok: true,
              field: 'name',
              change: { op: 'set', value: 'Slate' }
            })
          }
        }
      }
    })
    emitNotification({
      method: 'turn/completed',
      params: { turn: { threadId: 'builder-thread' } }
    })

    await waitFor(() => expect(screen.getByDisplayValue('Slate')).toBeInTheDocument())
    await waitFor(() => expect(document.querySelector('.agent-builder-doc.is-agent-driving')).not.toBeInTheDocument())
  })

  it('persists the rerolled avatar when creating a profile', async () => {
    await openBlankBuilder()

    fireEvent.change(screen.getByPlaceholderText('agent name'), { target: { value: 'avatar-bot' } })
    fireEvent.click(screen.getByTitle('Re-roll avatar'))
    await waitFor(() => expect(screen.getByRole('button', { name: /Create/i })).not.toBeDisabled())
    fireEvent.click(screen.getByRole('button', { name: /Create/i }))
    const workspaceTitle = await screen.findByText('Workspace')
    const workspaceButton = workspaceTitle.closest('button')
    expect(workspaceButton).not.toBeNull()
    fireEvent.click(workspaceButton!)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('agent/profiles/upsert', expect.objectContaining({
        id: 'avatar-bot',
        source: 'workspace'
      }))
    })
    const upsertCall = appServerSendRequest.mock.calls.find(([method]) => method === 'agent/profiles/upsert')
    expect((upsertCall?.[1] as { rawContent?: string } | undefined)?.rawContent).toMatch(
      /avatar: \d+/
    )
  })

  it('persists a complete custom provider preference and omits it when inheriting', async () => {
    await openBlankBuilder()
    fireEvent.change(screen.getByPlaceholderText('agent name'), { target: { value: 'model-bot' } })

    const customSwitch = await screen.findByRole('switch', { name: 'Custom model settings' })
    expect(customSwitch).not.toBeChecked()
    fireEvent.click(customSwitch)
    expect(customSwitch).toBeChecked()

    fireEvent.click(screen.getByRole('button', { name: /Create/i }))
    fireEvent.click((await screen.findByText('Workspace')).closest('button')!)
    await waitFor(() => expect(appServerSendRequest).toHaveBeenCalledWith(
      'agent/profiles/upsert',
      expect.objectContaining({ id: 'model-bot' })
    ))

    const upsertCall = appServerSendRequest.mock.calls.find(([method]) => method === 'agent/profiles/upsert')
    const rawContent = (upsertCall?.[1] as { rawContent?: string } | undefined)?.rawContent ?? ''
    expect(rawContent).toContain(`providerPreference:
  providerId: openai
  model: gpt-5.5
  reasoning:
    enabled: true
    effort: high
  speed: fast
  contextWindow:
    mode: max`)

    fireEvent.click(customSwitch)
    await waitFor(() => {
      const updates = appServerSendRequest.mock.calls.filter(([method]) => method === 'agent/profiles/upsert')
      expect(updates.length).toBeGreaterThan(1)
      expect((updates.at(-1)?.[1] as { rawContent?: string }).rawContent).not.toContain('providerPreference:')
    })
  })
})
