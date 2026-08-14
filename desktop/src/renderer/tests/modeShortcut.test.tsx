import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { InputComposer } from '../components/conversation/InputComposer'
import { ConversationWelcome } from '../components/conversation/ConversationWelcome'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'

const settingsGet = vi.fn()
const appServerSendRequest = vi.fn()
const workspaceSaveImageToTemp = vi.fn()
const fileReadFile = vi.fn()

function renderWithLocale(node: JSX.Element): void {
  render(<LocaleProvider>{node}</LocaleProvider>)
}

describe('mode shortcut', () => {
  beforeEach(() => {
    vi.clearAllMocks()

    settingsGet.mockResolvedValue({ locale: 'en' })
    appServerSendRequest.mockResolvedValue({})
    workspaceSaveImageToTemp.mockResolvedValue({ path: 'temp/image.png' })
    fileReadFile.mockResolvedValue('{}')

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet },
        appServer: { sendRequest: appServerSendRequest },
        workspace: { saveImageToTemp: workspaceSaveImageToTemp },
        file: { readFile: fileReadFile }
      }
    })

    useConversationStore.getState().reset()
    useConversationStore.setState({ threadMode: 'agent' })

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
    useUIStore.setState({
      activeMainView: 'conversation',
      automationsTab: 'tasks',
      sidebarCollapsed: false,
      sidebarWidth: 240,
      detailPanelVisible: true,
      detailPanelWidth: 400,
      activeDetailTab: 'changes',
      selectedChangedFile: null,
      autoShowTriggeredForTurn: null,
      composerPrefill: null,
      pendingWelcomeTurn: null,
      _pendingWelcomeTimer: null
    })
  })

  it('toggles InputComposer mode with Shift+Tab and syncs to thread/mode/set', async () => {
    renderWithLocale(
      <InputComposer
        threadId="thread-1"
        workspacePath="X:\\fixtures\\workspace"
        modelOptions={['Default', 'gpt-5']}
      />
    )

    const textbox = screen.getByRole('textbox')
    expect(screen.queryByRole('button', { name: 'Agent' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Disable plan mode' })).toBeNull()

    fireEvent.keyDown(textbox, { key: 'Tab', shiftKey: true })

    await waitFor(() => {
      expect(useConversationStore.getState().threadMode).toBe('plan')
      expect(screen.getByRole('button', { name: 'Disable plan mode' })).toBeInTheDocument()
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/mode/set', {
        threadId: 'thread-1',
        mode: 'plan'
      })
    })

    fireEvent.keyDown(textbox, { key: 'Tab', shiftKey: true })

    await waitFor(() => {
      expect(useConversationStore.getState().threadMode).toBe('agent')
      expect(screen.queryByRole('button', { name: 'Disable plan mode' })).toBeNull()
      expect(appServerSendRequest).toHaveBeenLastCalledWith('thread/mode/set', {
        threadId: 'thread-1',
        mode: 'agent'
      })
    })
  })

  it('does not toggle InputComposer mode on plain Tab', () => {
    renderWithLocale(
      <InputComposer
        threadId="thread-1"
        workspacePath="X:\\fixtures\\workspace"
        modelOptions={['Default', 'gpt-5']}
      />
    )

    const textbox = screen.getByRole('textbox')

    fireEvent.keyDown(textbox, { key: 'Tab' })

    expect(useConversationStore.getState().threadMode).toBe('agent')
    // Plain Tab must not trigger thread/mode/set; ignore harmless mount-time bootstrap calls.
    const modeSetCalls = appServerSendRequest.mock.calls.filter(
      ([method]: [string, ...unknown[]]) => method === 'thread/mode/set'
    )
    expect(modeSetCalls).toHaveLength(0)
  })

  it('toggles ConversationWelcome mode with Shift+Tab without calling thread/mode/set', async () => {
    renderWithLocale(<ConversationWelcome workspacePath="X:\\fixtures\\workspace" />)

    const textbox = screen.getByRole('textbox')
    expect(screen.queryByRole('button', { name: 'Agent' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Disable plan mode' })).toBeNull()

    fireEvent.keyDown(textbox, { key: 'Tab', shiftKey: true })

    const planLabel = await screen.findByRole('button', { name: 'Disable plan mode' })
    fireEvent.mouseEnter(planLabel.parentElement as HTMLElement)

    expect(await screen.findByRole('tooltip')).toHaveTextContent('Create plan')
    expect(screen.getByRole('tooltip')).toHaveTextContent('Shift')
    expect(screen.getByRole('tooltip')).toHaveTextContent('Tab')

    fireEvent.keyDown(textbox, { key: 'Tab', shiftKey: true })

    await waitFor(() => {
      expect(screen.queryByRole('button', { name: 'Disable plan mode' })).toBeNull()
    })

    expect(appServerSendRequest).not.toHaveBeenCalledWith('thread/mode/set', expect.anything())
  })
})
