import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ConversationPanel } from '../components/layout/ConversationPanel'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'

const appServerSendRequest = vi.fn()

function renderWithLocale(node: JSX.Element): void {
  render(<LocaleProvider>{node}</LocaleProvider>)
}

describe('Agent Builder conversation panel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    appServerSendRequest.mockResolvedValue({})

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
        appServer: { sendRequest: appServerSendRequest },
        file: { readFile: vi.fn().mockResolvedValue('{}') },
        workspace: { saveImageToTemp: vi.fn() }
      }
    })

    useConversationStore.getState().reset()
    useConversationStore.setState({
      turns: [],
      turnStatus: 'idle',
      threadMode: 'agent'
    })
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
      modelOptions: ['Default', 'gpt-5'],
      modelListUnsupportedEndpoint: false
    })
    useThreadStore.getState().reset()
    useThreadStore.setState({
      activeThreadId: 'builder-thread',
      activeThread: {
        id: 'builder-thread',
        userId: 'local',
        workspacePath: 'X:\\fixtures\\workspace',
        displayName: 'Hidden builder',
        status: 'active',
        originChannel: 'dotcraft-desktop',
        metadata: {},
        createdAt: new Date().toISOString(),
        lastActiveAt: new Date().toISOString(),
        turns: []
      },
      loading: false
    })
    useUIStore.setState({
      activeMainView: 'conversation',
      composerPrefill: null,
      planApprovalDismissed: {}
    })
  })

  it('shows quick prompts for an empty builder chat and injects without sending', async () => {
    renderWithLocale(
      <ConversationPanel
        workspacePath="X:\\fixtures\\workspace"
        variant="agentBuilder"
        minimalComposer
      />
    )

    expect(screen.queryByText('Hidden builder')).toBeNull()
    expect(screen.getByText('How should we improve this agent?')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Test this agent' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add advanced logic' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Optimize this agent' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Optimize this agent' }))

    await waitFor(() => {
      expect(screen.getByRole('textbox')).toHaveTextContent('Optimize this agent for reliability and focus.')
    })
    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'turn/start')).toBe(false)
    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'turn/enqueue')).toBe(false)
  })
})
