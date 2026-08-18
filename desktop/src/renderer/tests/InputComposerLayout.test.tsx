import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import type { ComponentProps } from 'react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { InputComposer } from '../components/conversation/InputComposer'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import { useProvidersStore } from '../stores/providersStore'
import { useSubAgentStore } from '../stores/subAgentStore'
import { useThreadStore } from '../stores/threadStore'
import { useToastStore } from '../stores/toastStore'
import { useUIStore } from '../stores/uiStore'
import { normalizeGitPathKey, useGitStore } from '../stores/gitStore'
import { useComposerDraftStore } from '../stores/composerDraftStore'
import { useVoiceStore } from '../voice/voiceStore'
import type { ConversationTurn } from '../types/conversation'
import { installDesktopApiMock } from './desktopApiMock'

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

const settingsGet = vi.fn()
const appServerSendRequest = vi.fn()
const gitListBranches = vi.fn()
const readImageAsDataUrl = vi.fn()

function renderComposer(extraProps: Partial<ComponentProps<typeof InputComposer>> = {}): void {
  render(
    <LocaleProvider>
      <InputComposer
        threadId="thread-1"
        workspacePath="C:\\sample\\workspace"
        modelName="gpt-5.4"
        modelOptions={['gpt-5.4', 'gpt-5.4-mini']}
        {...extraProps}
      />
    </LocaleProvider>
  )
}

function setCaretToEnd(element: HTMLElement): void {
  const selection = window.getSelection()
  if (!selection) return
  const range = document.createRange()
  range.selectNodeContents(element)
  range.collapse(false)
  selection.removeAllRanges()
  selection.addRange(range)
}

function userTurn(id: string, text: string): ConversationTurn {
  return {
    id,
    threadId: 'thread-1',
    status: 'completed',
    startedAt: new Date().toISOString(),
    completedAt: new Date().toISOString(),
    items: [
      {
        id: `${id}-user`,
        type: 'userMessage',
        status: 'completed',
        text,
        createdAt: new Date().toISOString(),
        completedAt: new Date().toISOString()
      }
    ]
  }
}

describe('InputComposer layout', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    appServerSendRequest.mockResolvedValue({})
    readImageAsDataUrl.mockResolvedValue({ dataUrl: 'data:image/png;base64,AA==' })
    gitListBranches.mockResolvedValue({
      current: 'main',
      detachedHead: null,
      branches: [{ name: 'main', current: true }]
    })

    installDesktopApiMock({
        settings: { get: settingsGet },
        appServer: { sendRequest: appServerSendRequest, onNotification: undefined },
        git: { listBranches: gitListBranches },
        workspace: { saveImageToTemp: vi.fn(), readImageAsDataUrl },
        voice: undefined
      })

    useConversationStore.getState().reset()
    useConnectionStore.getState().reset()
    useModelCatalogStore.getState().reset()
    useProvidersStore.getState().reset()
    useSubAgentStore.getState().reset()
    useThreadStore.getState().reset()
    useGitStore.getState().reset()
    useGitStore.setState({
      branchesByPath: {
        [normalizeGitPathKey('C:\\sample\\workspace')]: {
          path: 'C:\\sample\\workspace',
          status: 'available',
          snapshot: {
            current: 'main',
            detachedHead: null,
            branches: [{ name: 'main', current: true }]
          },
          refreshing: false,
          errorMessage: null,
          updatedAt: Date.now(),
          requestId: 1
        }
      }
    })
    useComposerDraftStore.setState({ draftsByThread: {} })
    useVoiceStore.setState({
      initialized: false,
      snapshot: { model: { phase: 'missing', bytesDownloaded: 0, bytesTotal: null }, sessions: [], capacity: 2 },
      recording: null,
      finalizing: null,
      microphonePermission: 'unknown',
      deviceFallback: false,
      localErrors: {}
    })
    useToastStore.setState({ toasts: [] })
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
      composerFileAttachmentRequest: null,
      pendingWelcomeTurn: null,
      _pendingWelcomeTimer: null
    })
    useThreadStore.setState({
      threadList: [
        {
          id: 'thread-1',
          displayName: 'Layout test',
          status: 'active',
          originChannel: 'dotcraft-desktop',
          createdAt: new Date().toISOString(),
          lastActiveAt: new Date().toISOString()
        }
      ]
    })
  })

  it('adds a file requested by another surface to the active composer', async () => {
    renderComposer()

    act(() => {
      useUIStore.getState().requestComposerFileAttachment({
        path: 'C:\\sample\\workspace\\.dockerignore',
        fileName: '.dockerignore'
      })
    })

    expect(await screen.findByText('.dockerignore')).toBeInTheDocument()
    expect(useUIStore.getState().composerFileAttachmentRequest).toBeNull()
    await waitFor(() => {
      expect(screen.getByRole('textbox')).toHaveFocus()
    })
  })

  it('renders plan mode as an active-only label and exposes the mode switch from the command picker', async () => {
    renderComposer()

    screen.getByRole('textbox')
    expect(screen.queryByRole('button', { name: 'Agent' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Plan' })).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Open commands' }))
    fireEvent.click(screen.getByRole('option', { name: /Plan mode/ }))

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Disable plan mode' })).toBeInTheDocument()
      expect(screen.getByText('Plan')).toBeInTheDocument()
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/mode/set', {
        threadId: 'thread-1',
        mode: 'plan'
      })
    })
    expect(Boolean(
      screen.getByRole('button', { name: 'Open commands' })
        .compareDocumentPosition(screen.getByTestId('approval-policy-trigger')) & Node.DOCUMENT_POSITION_FOLLOWING
    )).toBe(true)
    expect(Boolean(
      screen.getByTestId('approval-policy-trigger')
        .compareDocumentPosition(screen.getByRole('button', { name: 'Disable plan mode' })) & Node.DOCUMENT_POSITION_FOLLOWING
    )).toBe(true)

    fireEvent.click(screen.getByRole('button', { name: 'Disable plan mode' }))
    await waitFor(() => {
      expect(screen.queryByRole('button', { name: 'Disable plan mode' })).not.toBeInTheDocument()
      expect(appServerSendRequest).toHaveBeenLastCalledWith('thread/mode/set', {
        threadId: 'thread-1',
        mode: 'agent'
      })
    })

    const modelButton = screen.getByRole('button', { name: 'Select model' })
    fireEvent.focus(modelButton)
    const tooltip = screen.getByRole('tooltip')
    expect(within(tooltip).getByText('Select model')).toBeInTheDocument()
    expect(within(tooltip).getByText('Ctrl')).toBeInTheDocument()
    expect(within(tooltip).getByText('Shift')).toBeInTheDocument()
    expect(within(tooltip).getByText('M')).toBeInTheDocument()

    fireEvent.keyDown(window, { key: 'M', ctrlKey: true, shiftKey: true })
    const menu = screen.getByRole('menu', { name: 'Select model' })
    fireEvent.click(within(menu).getByRole('menuitem', { name: /Model/ }))
    const listbox = screen.getByRole('listbox', { name: 'Model' })

    expect(listbox).toBeInTheDocument()
    expect(within(listbox).getByRole('option', { name: 'gpt-5.4-mini' })).toBeInTheDocument()
  })

  it('keeps send button available alongside the inline toolbar', () => {
    renderComposer()

    const modelButton = screen.getByRole('button', { name: 'Select model' })
    const voiceButton = screen.getByRole('button', { name: 'Click to dictate or hold' })
    const sendButton = screen.getByRole('button', { name: 'Send message' })

    expect(sendButton).toBeInTheDocument()
    expect(Boolean(modelButton.compareDocumentPosition(voiceButton) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
    expect(Boolean(voiceButton.compareDocumentPosition(sendButton) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
  })

  it('uses the compact voice footer while recording', () => {
    useVoiceStore.setState({
      initialized: true,
      snapshot: { model: { phase: 'installed', bytesDownloaded: 1, bytesTotal: 1 }, sessions: [], capacity: 2 },
      recording: { threadId: 'thread-1', startedAt: 0, elapsedMs: 1_000, level: 0.5 }
    })

    renderComposer()

    expect(screen.getByRole('button', { name: 'Open commands' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Stop dictation' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Send message' })).toBeInTheDocument()
    expect(screen.queryByTestId('approval-policy-trigger')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Select model' })).toBeNull()
    expect(screen.getByText('Local')).toBeInTheDocument()
  })

  it('keeps the compact footer and disables send while captured audio is finalizing', () => {
    useComposerDraftStore.getState().saveDraft('thread-1', {
      text: 'Keep this draft',
      segments: [{ type: 'text', value: 'Keep this draft' }],
      images: [],
      files: []
    })
    useVoiceStore.setState({
      initialized: true,
      snapshot: { model: { phase: 'installed', bytesDownloaded: 1, bytesTotal: 1 }, sessions: [], capacity: 2 },
      recording: null,
      finalizing: { threadId: 'thread-1', intent: 'insert', durationMs: 1_000 }
    })

    renderComposer()

    expect(screen.getByRole('button', { name: 'Processing voice input' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Send message' })).toBeDisabled()
    expect(screen.getByText('0:01')).toBeInTheDocument()
    expect(screen.queryByTestId('approval-policy-trigger')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Select model' })).toBeNull()
    expect(document.querySelector('canvas')).toBeNull()
    expect(screen.getByText('Local')).toBeInTheDocument()
  })

  it.each(['queued', 'transcribing'] as const)('uses the compact voice footer while voice input is %s', (phase) => {
    useVoiceStore.setState({
      initialized: true,
      snapshot: {
        model: { phase: 'installed', bytesDownloaded: 1, bytesTotal: 1 },
        sessions: [{
          sessionId: 'voice-session',
          threadId: 'thread-1',
          intent: 'insert',
          phase,
          durationMs: 1_000
        }],
        capacity: 2
      },
      recording: null
    })

    renderComposer()

    expect(screen.getByRole('button', { name: 'Processing voice input' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Open commands' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Send message' })).toBeDisabled()
    expect(screen.getByText('0:01')).toBeInTheDocument()
    expect(screen.queryByTestId('approval-policy-trigger')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Select model' })).toBeNull()
    expect(screen.getByText('Local')).toBeInTheDocument()
  })

  it('restores the normal footer when voice processing completes or becomes retryable', () => {
    useVoiceStore.setState({
      initialized: true,
      snapshot: {
        model: { phase: 'installed', bytesDownloaded: 1, bytesTotal: 1 },
        sessions: [{
          sessionId: 'voice-session',
          threadId: 'thread-1',
          intent: 'insert',
          phase: 'transcribing',
          durationMs: 1_000
        }],
        capacity: 2
      },
      recording: null
    })

    renderComposer()

    act(() => {
      useVoiceStore.setState((state) => ({
        snapshot: { ...state.snapshot, sessions: [] }
      }))
    })
    expect(screen.getByTestId('approval-policy-trigger')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Select model' })).toBeInTheDocument()

    act(() => {
      useVoiceStore.setState((state) => ({
        snapshot: {
          ...state.snapshot,
          sessions: [{
            sessionId: 'voice-session',
            threadId: 'thread-1',
            intent: 'insert',
            phase: 'retryable',
            durationMs: 1_000,
            errorCode: 'transcription-failed'
          }]
        }
      }))
    })
    expect(screen.getByRole('button', { name: 'Retry voice input' })).toBeInTheDocument()
    expect(screen.getByTestId('approval-policy-trigger')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Select model' })).toBeInTheDocument()
  })

  it('keeps this composer normal while another thread is transcribing', () => {
    useVoiceStore.setState({
      initialized: true,
      snapshot: {
        model: { phase: 'installed', bytesDownloaded: 1, bytesTotal: 1 },
        sessions: [{
          sessionId: 'other-voice-session',
          threadId: 'thread-2',
          intent: 'insert',
          phase: 'transcribing',
          durationMs: 1_000
        }],
        capacity: 2
      },
      recording: null
    })

    renderComposer()

    expect(screen.getByTestId('approval-policy-trigger')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Select model' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Processing voice input' })).toBeNull()
    expect(screen.getByText('Local')).toBeInTheDocument()
  })

  it('keeps this composer normal while another thread is finalizing audio', () => {
    useVoiceStore.setState({
      initialized: true,
      snapshot: { model: { phase: 'installed', bytesDownloaded: 1, bytesTotal: 1 }, sessions: [], capacity: 2 },
      recording: null,
      finalizing: { threadId: 'thread-2', intent: 'insert', durationMs: 1_000 }
    })

    renderComposer()

    expect(screen.getByTestId('approval-policy-trigger')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Select model' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Processing voice input' })).toBeNull()
    expect(screen.getByText('Local')).toBeInTheDocument()
  })

  it('keeps the normal footer while the voice model is downloading', () => {
    useVoiceStore.setState({
      initialized: true,
      snapshot: {
        model: { phase: 'downloading', bytesDownloaded: 50, bytesTotal: 100 },
        sessions: [],
        capacity: 2
      },
      recording: null
    })

    renderComposer()

    expect(screen.getByRole('button', { name: /Downloading Whisper/ })).toBeInTheDocument()
    expect(screen.getByTestId('approval-policy-trigger')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Select model' })).toBeInTheDocument()
    expect(screen.getByText('Local')).toBeInTheDocument()
  })

  it('uses the compact voice footer in the agent builder composer', () => {
    useVoiceStore.setState({
      initialized: true,
      snapshot: { model: { phase: 'installed', bytesDownloaded: 1, bytesTotal: 1 }, sessions: [], capacity: 2 },
      recording: { threadId: 'agent-builder-intro', startedAt: 0, elapsedMs: 1_000, level: 0.5 }
    })

    renderComposer({
      threadId: 'agent-builder-intro',
      variant: 'agentBuilder',
      minimalChrome: true,
      submitOverride: vi.fn()
    })

    expect(screen.getByRole('button', { name: 'Open commands' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Stop dictation' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Select model' })).toBeNull()
  })

  it.each(['finalizing', 'transcribing'] as const)('keeps the agent builder composer compact while %s', (phase) => {
    useVoiceStore.setState({
      initialized: true,
      snapshot: {
        model: { phase: 'installed', bytesDownloaded: 1, bytesTotal: 1 },
        sessions: phase === 'transcribing' ? [{
          sessionId: 'agent-builder-voice-session',
          threadId: 'agent-builder-intro',
          intent: 'insert',
          phase: 'transcribing',
          durationMs: 1_000
        }] : [],
        capacity: 2
      },
      recording: null,
      finalizing: phase === 'finalizing'
        ? { threadId: 'agent-builder-intro', intent: 'insert', durationMs: 1_000 }
        : null
    })

    renderComposer({
      threadId: 'agent-builder-intro',
      variant: 'agentBuilder',
      minimalChrome: true,
      submitOverride: vi.fn()
    })

    expect(screen.getByRole('button', { name: 'Open commands' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Processing voice input' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Send message' })).toBeDisabled()
    expect(screen.queryByRole('button', { name: 'Select model' })).toBeNull()
  })

  it('keeps a missing-device microphone available for retry', () => {
    useVoiceStore.setState({
      initialized: true,
      snapshot: { model: { phase: 'installed', bytesDownloaded: 1, bytesTotal: 1 }, sessions: [], capacity: 2 },
      localErrors: { 'thread-1': 'device-missing' }
    })

    renderComposer()

    expect(screen.getByRole('button', { name: 'No microphone is available' })).toBeEnabled()
  })

  it('starts the Whisper download without requesting microphone access', async () => {
    const installModel = vi.fn().mockResolvedValue(undefined)
    const getUserMedia = vi.fn()
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: { getUserMedia }
    })
    installDesktopApiMock({
        ...window.api,
        voice: {
          getSnapshot: vi.fn().mockResolvedValue({
            model: { phase: 'missing', bytesDownloaded: 0, bytesTotal: null },
            sessions: [],
            capacity: 2
          }),
          getMicrophonePermissionStatus: vi.fn().mockResolvedValue('not-determined'),
          requestMicrophonePermission: vi.fn().mockResolvedValue('granted'),
          installModel,
          onSnapshot: vi.fn(() => () => {}),
          onSessionEvent: vi.fn(() => () => {})
        }
      })

    renderComposer()
    fireEvent.click(screen.getByRole('button', { name: 'Click to dictate or hold' }))
    fireEvent.click(screen.getByRole('button', { name: 'Set up' }))

    await waitFor(() => expect(installModel).toHaveBeenCalledTimes(1))
    expect(getUserMedia).not.toHaveBeenCalled()
    expect(screen.queryByText('Use your microphone')).not.toBeInTheDocument()
  })

  it('disables plan and agent mode controls in the agent builder variant', async () => {
    const onBeforeSend = vi.fn().mockResolvedValue(undefined)
    useConnectionStore.setState({ capabilities: { commandManagement: true } })

    renderComposer({ variant: 'agentBuilder', minimalChrome: true, onBeforeSend })

    expect(screen.getByRole('button', { name: 'Click to dictate or hold' })).toBeInTheDocument()
    const commandTrigger = screen.getByRole('button', { name: 'Open commands' })
    fireEvent.click(commandTrigger)
    expect(screen.queryByRole('option', { name: /Plan mode/ })).toBeNull()

    const textbox = screen.getByRole('textbox')
    expect(textbox).toHaveTextContent('')
    fireEvent.click(commandTrigger)
    fireEvent.keyDown(textbox, { key: 'Tab', shiftKey: true })
    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'thread/mode/set')).toBe(false)

    textbox.textContent = '/plan'
    const selection = window.getSelection()
    const range = document.createRange()
    range.selectNodeContents(textbox)
    range.collapse(false)
    selection?.removeAllRanges()
    selection?.addRange(range)
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter', code: 'Enter' })

    await waitFor(() => {
      expect(onBeforeSend).toHaveBeenCalledTimes(1)
      expect(appServerSendRequest).toHaveBeenCalledWith('turn/start', expect.objectContaining({
        threadId: 'thread-1'
      }))
    })
    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'thread/mode/set')).toBe(false)
  })

  it('submits detached agent builder intro input through the override with serialized input parts', async () => {
    const submitOverride = vi.fn().mockResolvedValue(undefined)

    renderComposer({
      threadId: 'agent-builder-intro',
      variant: 'agentBuilder',
      minimalChrome: true,
      placeholder: 'Describe the agent you want...',
      submitOverride
    })

    const textbox = screen.getByRole('textbox')
    expect(textbox).toHaveAttribute('aria-placeholder', 'Describe the agent you want...')

    textbox.textContent = 'Build a support triage agent'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter', code: 'Enter' })

    await waitFor(() => {
      expect(submitOverride).toHaveBeenCalledWith(expect.objectContaining({
        text: 'Build a support triage agent',
        inputParts: [{ type: 'text', text: 'Build a support triage agent' }]
      }))
    })
    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'turn/start')).toBe(false)
    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'turn/enqueue')).toBe(false)
    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'thread/mode/set')).toBe(false)
  })

  it('localizes the plan mode label and command trigger', async () => {
    settingsGet.mockResolvedValue({ locale: 'zh-Hans' })
    useConversationStore.setState({ threadMode: 'plan' })

    renderComposer()

    expect(await screen.findByRole('button', { name: '关闭计划模式' })).toBeInTheDocument()
    expect(screen.getByText('计划')).toBeInTheDocument()

    fireEvent.click(await screen.findByRole('button', { name: '打开命令' }))
    expect(screen.getByRole('option', { name: /计划模式/ })).toBeInTheDocument()
  })

  it('renders the SubAgent dock as a responsive attached accessory above the composer surface', () => {
    useConnectionStore.setState({ capabilities: { subAgentSessions: true } })
    useSubAgentStore.getState().setChildren('thread-1', [
      {
        childThreadId: 'child-1',
        parentThreadId: 'thread-1',
        nickname: 'Lovelace',
        agentRole: null,
        profileName: 'native',
        runtimeType: 'native',
        supportsSendInput: true,
        supportsResume: true,
        supportsClose: true,
        status: 'open',
        lastToolDisplay: 'Reading sprite atlas',
        currentTool: 'ReadFile',
        inputTokens: 12,
        outputTokens: 34,
        isCompleted: false,
        runtime: {
          running: true,
          waitingOnApproval: false,
          waitingOnPlanConfirmation: false
        }
      }
    ])
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'subagent/children/list') {
        return {
          data: [
            {
              edge: {
                parentThreadId: 'thread-1',
                childThreadId: 'child-1',
                agentNickname: 'Lovelace',
                profileName: 'native',
                runtimeType: 'native',
                supportsSendInput: true,
                supportsResume: true,
                supportsClose: true,
                status: 'open'
              },
              thread: {
                id: 'child-1',
                displayName: 'Lovelace',
                status: 'active',
                originChannel: 'subagent',
                createdAt: new Date().toISOString(),
                lastActiveAt: new Date().toISOString(),
                runtime: {
                  running: true,
                  waitingOnApproval: false,
                  waitingOnPlanConfirmation: false
                }
              }
            }
          ]
        }
      }
      return {}
    })

    renderComposer()

    const dock = screen.getByTestId('subagent-dock')
    const overlay = screen.getByTestId('composer-top-accessory-overlay')
    const shell = overlay.parentElement
    const composerLayer = overlay.nextElementSibling

    expect(overlay).toContainElement(dock)
    expect(composerLayer).toContainElement(screen.getByRole('textbox'))
    expect(shell).toContainElement(overlay)
    expect(dock.parentElement).toBe(overlay)
  })

  it('hides the dock when the only subagents are completed', () => {
    useConnectionStore.setState({ capabilities: { subAgentSessions: true } })
    useSubAgentStore.getState().setChildren('thread-1', [
      {
        childThreadId: 'child-1',
        parentThreadId: 'thread-1',
        nickname: 'Lovelace',
        agentRole: null,
        profileName: 'native',
        runtimeType: 'native',
        supportsSendInput: true,
        supportsResume: true,
        supportsClose: true,
        status: 'completed',
        lastToolDisplay: null,
        currentTool: null,
        inputTokens: 12,
        outputTokens: 34,
        isCompleted: true,
        runtime: {
          running: false,
          waitingOnApproval: false,
          waitingOnPlanConfirmation: false
        }
      }
    ])

    renderComposer()

    expect(screen.queryByTestId('subagent-dock')).not.toBeInTheDocument()
  })

  it('shows only running subagents and a View done link for completed ones', () => {
    useConnectionStore.setState({ capabilities: { subAgentSessions: true } })
    useSubAgentStore.getState().setChildren('thread-1', [
      {
        childThreadId: 'child-running',
        parentThreadId: 'thread-1',
        nickname: 'Lovelace',
        agentRole: null,
        profileName: 'native',
        runtimeType: 'native',
        supportsSendInput: true,
        supportsResume: true,
        supportsClose: true,
        status: 'open',
        lastToolDisplay: 'Reading sprite atlas',
        currentTool: 'ReadFile',
        inputTokens: 12,
        outputTokens: 34,
        isCompleted: false,
        runtime: {
          running: true,
          waitingOnApproval: false,
          waitingOnPlanConfirmation: false
        }
      },
      {
        childThreadId: 'child-done',
        parentThreadId: 'thread-1',
        nickname: 'Babbage',
        agentRole: null,
        profileName: 'native',
        runtimeType: 'native',
        supportsSendInput: true,
        supportsResume: true,
        supportsClose: true,
        status: 'completed',
        lastToolDisplay: null,
        currentTool: null,
        inputTokens: 5,
        outputTokens: 9,
        isCompleted: true,
        runtime: {
          running: false,
          waitingOnApproval: false,
          waitingOnPlanConfirmation: false
        }
      },
      {
        // A closed child (the Subagents tab requests these into shared state).
        childThreadId: 'child-closed',
        parentThreadId: 'thread-1',
        nickname: 'Retired',
        agentRole: null,
        profileName: 'native',
        runtimeType: 'native',
        supportsSendInput: true,
        supportsResume: true,
        supportsClose: true,
        status: 'closed',
        lastToolDisplay: null,
        currentTool: null,
        inputTokens: 1,
        outputTokens: 2,
        isCompleted: true,
        runtime: {
          running: false,
          waitingOnApproval: false,
          waitingOnPlanConfirmation: false
        }
      }
    ])

    renderComposer()

    const dock = screen.getByTestId('subagent-dock')
    expect(within(dock).getByText('1 background agents')).toBeInTheDocument()
    expect(within(dock).queryByText(/Done ·/)).not.toBeInTheDocument()
  })

  it('renders queued messages inside the background activity dock with neutral drag handles', async () => {
    useConversationStore.setState({
      queuedInputs: [
        {
          id: 'queued-1',
          threadId: 'thread-1',
          displayText: 'first queued follow-up',
          status: 'queued',
          createdAt: new Date().toISOString()
        },
        {
          id: 'queued-2',
          threadId: 'thread-1',
          displayText: 'second queued follow-up',
          status: 'guidancePending',
          createdAt: new Date().toISOString()
        }
      ]
    })

    renderComposer()

    const dock = screen.getByTestId('subagent-dock')
    expect(screen.getByText('2 queued messages')).toBeInTheDocument()
    expect(within(dock).getByText('first queued follow-up')).toBeInTheDocument()
    expect(within(dock).getByText('second queued follow-up')).toBeInTheDocument()
    expect(within(dock).getAllByRole('button', { name: 'Reorder queued message' })).toHaveLength(2)
    expect(within(dock).getByRole('button', { name: 'Steering' })).not.toBeDisabled()
    expect(within(dock).getByRole('button', { name: 'Steering' })).toHaveAttribute('aria-pressed', 'true')
    expect(within(dock).getAllByRole('button', { name: 'Reorder queued message' })[1]).toBeDisabled()
    expect(within(dock).getAllByRole('button', { name: 'Remove' })[0]).toHaveAttribute('data-tone', 'neutral')
    const firstEditButton = within(dock).getAllByRole('button', { name: 'Edit queued message' })[0]
    const firstQueueRow = firstEditButton.parentElement?.parentElement
    expect(firstQueueRow).toHaveStyle({
      gridTemplateColumns: '18px minmax(0, 1fr) auto 24px 24px'
    })
  })

  it('separates queued messages from background agents inside one dock', () => {
    useConnectionStore.setState({ capabilities: { subAgentSessions: true } })
    useConversationStore.setState({
      queuedInputs: [
        {
          id: 'queued-1',
          threadId: 'thread-1',
          displayText: 'queued follow-up',
          status: 'queued',
          createdAt: new Date().toISOString()
        }
      ]
    })
    useSubAgentStore.getState().setChildren('thread-1', [
      {
        childThreadId: 'child-1',
        parentThreadId: 'thread-1',
        nickname: 'Lovelace',
        agentRole: null,
        profileName: 'native',
        runtimeType: 'native',
        supportsSendInput: true,
        supportsResume: true,
        supportsClose: true,
        status: 'open',
        lastToolDisplay: 'Reading sprite atlas',
        currentTool: 'ReadFile',
        inputTokens: 12,
        outputTokens: 34,
        isCompleted: false,
        runtime: {
          running: true,
          waitingOnApproval: false,
          waitingOnPlanConfirmation: false
        }
      }
    ])

    renderComposer()

    const dock = screen.getByTestId('subagent-dock')
    expect(within(dock).getByText('1 background agents')).toBeInTheDocument()
    expect(within(dock).getByText('queued follow-up')).toBeInTheDocument()
    expect(within(dock).getByRole('button', { name: 'Expand background agents' })).toBeInTheDocument()

    fireEvent.click(within(dock).getByRole('button', { name: 'Expand background agents' }))

    expect(within(dock).getByText('queued follow-up')).toBeInTheDocument()
    expect(within(dock).getByRole('button', { name: 'Collapse background agents' })).toBeInTheDocument()

    fireEvent.click(within(dock).getByRole('button', { name: 'Collapse background agents' }))

    expect(within(dock).getByText('queued follow-up')).toBeInTheDocument()
    expect(within(dock).getByRole('button', { name: 'Expand background agents' })).toBeInTheDocument()
  })

  it('removes queued messages through the dock action', async () => {
    useConversationStore.setState({
      queuedInputs: [
        {
          id: 'queued-1',
          threadId: 'thread-1',
          displayText: 'remove this follow-up',
          status: 'queued',
          createdAt: new Date().toISOString()
        }
      ]
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'turn/queue/remove') {
        return { queuedInputs: [] }
      }
      return {}
    })

    renderComposer()

    fireEvent.click(screen.getByRole('button', { name: 'Remove' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('turn/queue/remove', {
        threadId: 'thread-1',
        queuedInputId: 'queued-1'
      })
      expect(screen.queryByText('remove this follow-up')).not.toBeInTheDocument()
    })
  })

  it('edits a queued message after the remove action and restores rich composer content', async () => {
    useConversationStore.setState({
      queuedInputs: [
        {
          id: 'queued-1',
          threadId: 'thread-1',
          displayText: 'queued follow-up',
          status: 'queued',
          createdAt: new Date().toISOString(),
          nativeInputParts: [
            { type: 'text', text: 'queued ' },
            { type: 'fileRef', path: 'docs/a.md', displayPath: 'docs/a.md' },
            { type: 'text', text: ' ' },
            { type: 'commandRef', name: 'review', rawText: '/review src' },
            { type: 'text', text: ' ' },
            { type: 'skillRef', name: 'browser' },
            { type: 'localImage', path: 'C:\\temp\\diagram.png', fileName: 'diagram.png', mimeType: 'image/png' }
          ]
        }
      ]
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'turn/queue/remove') return { queuedInputs: [] }
      return {}
    })

    renderComposer()

    const textbox = screen.getByRole('textbox')
    textbox.textContent = 'replace this draft'
    fireEvent.input(textbox)
    const removeButton = screen.getByRole('button', { name: 'Remove' })
    const editButton = screen.getByRole('button', { name: 'Edit queued message' })
    expect(removeButton.compareDocumentPosition(editButton) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()

    fireEvent.click(editButton)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('turn/queue/remove', {
        threadId: 'thread-1',
        queuedInputId: 'queued-1'
      })
      expect(useConversationStore.getState().queuedInputs).toEqual([])
      expect(textbox).toHaveFocus()
    })
    expect(textbox.textContent).not.toContain('replace this draft')
    expect(textbox.querySelector('[data-ref-type="file"]')).toHaveAttribute('data-relative-path', 'docs/a.md')
    expect(textbox.querySelector('[data-ref-type="command"]')).toHaveAttribute('data-command', '/review')
    expect(textbox.querySelector('[data-ref-type="skill"]')).toHaveAttribute('data-skill', 'browser')
    expect(screen.getByRole('button', { name: 'Preview image diagram.png' })).toBeInTheDocument()
    expect(readImageAsDataUrl).toHaveBeenCalledWith({ path: 'C:\\temp\\diagram.png' })
    expect(readImageAsDataUrl.mock.invocationCallOrder[0]).toBeLessThan(
      appServerSendRequest.mock.invocationCallOrder.find((_, index) =>
        appServerSendRequest.mock.calls[index]?.[0] === 'turn/queue/remove'
      ) ?? Number.MAX_SAFE_INTEGER
    )
    expect(useComposerDraftStore.getState().getDraft('thread-1')?.images[0]).toMatchObject({
      tempPath: 'C:\\temp\\diagram.png',
      fileName: 'diagram.png',
      mimeType: 'image/png'
    })
  })

  it('keeps queued edit available for guidance but disables system-triggered inputs', () => {
    useConversationStore.setState({
      queuedInputs: [
        {
          id: 'queued-guidance',
          threadId: 'thread-1',
          displayText: 'pending guidance',
          status: 'guidancePending',
          createdAt: new Date().toISOString()
        },
        {
          id: 'queued-automation',
          threadId: 'thread-1',
          displayText: 'automated follow-up',
          status: 'queued',
          triggerKind: 'automation',
          createdAt: new Date().toISOString()
        }
      ]
    })

    renderComposer()

    const editButtons = screen.getAllByRole('button', { name: 'Edit queued message' })
    expect(editButtons).toHaveLength(2)
    expect(editButtons[0]).not.toBeDisabled()
    expect(editButtons[1]).toBeDisabled()
  })

  it('cancels Steering after the active turn ends through turn/queue/update', async () => {
    useConversationStore.setState({
      activeTurnId: null,
      queuedInputs: [{
        id: 'queued-guidance',
        threadId: 'thread-1',
        displayText: 'pending guidance',
        status: 'guidancePending',
        readyAfterTurnId: 'turn-active',
        createdAt: new Date().toISOString()
      }]
    })
    appServerSendRequest.mockResolvedValueOnce({
      queuedInputs: [{
        id: 'queued-guidance',
        threadId: 'thread-1',
        displayText: 'pending guidance',
        status: 'queued',
        createdAt: new Date().toISOString()
      }]
    })

    renderComposer()
    fireEvent.click(screen.getByRole('button', { name: 'Steering' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('turn/queue/update', {
        threadId: 'thread-1',
        expectedTurnId: 'turn-active',
        queuedInputId: 'queued-guidance',
        status: 'queued'
      })
    })
    expect(useConversationStore.getState().queuedInputs[0]?.status).toBe('queued')
  })

  it('keeps the queue and current composer when a queued image cannot be restored', async () => {
    readImageAsDataUrl.mockRejectedValue(new Error('missing image'))
    useConversationStore.setState({
      queuedInputs: [
        {
          id: 'queued-1',
          threadId: 'thread-1',
          displayText: 'image follow-up',
          status: 'queued',
          createdAt: new Date().toISOString(),
          nativeInputParts: [{ type: 'localImage', path: 'C:\\temp\\missing.png' }]
        }
      ]
    })

    renderComposer()
    const textbox = screen.getByRole('textbox')
    textbox.textContent = 'keep this draft'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Edit queued message' }))

    await waitFor(() => {
      expect(useToastStore.getState().toasts.some((toast) =>
        toast.message === 'Failed to edit queued message: missing image'
      )).toBe(true)
    })
    expect(appServerSendRequest).not.toHaveBeenCalledWith('turn/queue/remove', expect.anything())
    expect(useConversationStore.getState().queuedInputs).toHaveLength(1)
    expect(textbox.textContent).toBe('keep this draft')
  })

  it('keeps the queue and current composer when removing an edited input fails', async () => {
    useConversationStore.setState({
      queuedInputs: [
        {
          id: 'queued-1',
          threadId: 'thread-1',
          displayText: 'queued replacement',
          status: 'queued',
          createdAt: new Date().toISOString(),
          nativeInputParts: [{ type: 'text', text: 'queued replacement' }]
        }
      ]
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'turn/queue/remove') throw new Error('stale queue')
      return {}
    })

    renderComposer()
    const textbox = screen.getByRole('textbox')
    textbox.textContent = 'keep this draft'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Edit queued message' }))

    await waitFor(() => {
      expect(useToastStore.getState().toasts.some((toast) =>
        toast.message === 'Failed to edit queued message: stale queue'
      )).toBe(true)
    })
    expect(useConversationStore.getState().queuedInputs).toHaveLength(1)
    expect(textbox.textContent).toBe('keep this draft')
  })

  it('calls turn queue reorder when queued message order changes', async () => {
    const queued = [
      {
        id: 'queued-1',
        threadId: 'thread-1',
        displayText: 'first',
        status: 'queued',
        createdAt: new Date().toISOString()
      },
      {
        id: 'queued-2',
        threadId: 'thread-1',
        displayText: 'second',
        status: 'queued',
        createdAt: new Date().toISOString()
      }
    ]
    useConversationStore.setState({ queuedInputs: queued })
    appServerSendRequest.mockImplementation(async (method: string, params?: Record<string, unknown>) => {
      if (method === 'turn/queue/reorder') {
        const ids = params?.orderedQueuedInputIds as string[]
        return {
          queuedInputs: ids.map((id) => queued.find((item) => item.id === id))
        }
      }
      return {}
    })

    renderComposer()

    const handles = screen.getAllByRole('button', { name: 'Reorder queued message' })
    handles[1].focus()
    fireEvent.keyDown(handles[1], { key: 'ArrowUp', code: 'ArrowUp' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('turn/queue/reorder', {
        threadId: 'thread-1',
        orderedQueuedInputIds: ['queued-2', 'queued-1']
      })
    })
  })

  it('rolls back queued message order when reorder fails', async () => {
    useConversationStore.setState({
      queuedInputs: [
        {
          id: 'queued-1',
          threadId: 'thread-1',
          displayText: 'first',
          status: 'queued',
          createdAt: new Date().toISOString()
        },
        {
          id: 'queued-2',
          threadId: 'thread-1',
          displayText: 'second',
          status: 'queued',
          createdAt: new Date().toISOString()
        }
      ]
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'turn/queue/reorder') {
        throw new Error('stale queue')
      }
      return {}
    })

    renderComposer()

    const handles = screen.getAllByRole('button', { name: 'Reorder queued message' })
    fireEvent.keyDown(handles[1], { key: 'ArrowUp', code: 'ArrowUp' })

    await waitFor(() => {
      expect(useToastStore.getState().toasts.some((toast) =>
        toast.message === 'Failed to reorder queued messages: stale queue'
      )).toBe(true)
    })
    expect(useConversationStore.getState().queuedInputs.map((item) => item.id)).toEqual(['queued-1', 'queued-2'])
  })

  it('keeps the context usage ring aligned to the model picker height with a smaller donut', () => {
    useConversationStore.getState().setContextUsage({
      tokens: 2500,
      contextWindow: 10000,
      autoCompactThreshold: 8000,
      warningThreshold: 7000,
      errorThreshold: 9000,
      percentLeft: 0.75
    })

    renderComposer()

    const ring = screen.getByRole('img', { name: 'Context usage: 25% used' })

    expect(ring).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Select model' })).toBeInTheDocument()
  })

  it('shows ChatGPT subscription usage when the default model catalog resolves to an OAuth provider', async () => {
    useModelCatalogStore.setState({
      status: 'ready',
      providerId: 'openai',
      requestedProviderId: null,
      modelOptions: ['gpt-5.5'],
      models: [{ id: 'gpt-5.5' }],
      modelListUnsupportedEndpoint: false,
      errorCode: null,
      errorMessage: null
    })
    useConnectionStore.setState({
      capabilities: {
        providerManagement: true
      }
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'provider/list') {
        return {
          providers: [
            {
              id: 'openai',
              displayName: 'OpenAI (ChatGPT)',
              protocol: 'openai-responses',
              authMethod: 'chatgptOAuth',
              chatGptAccountId: 'acct_1234567890',
              chatGptPlanType: 'plus'
            }
          ]
        }
      }
      if (method === 'auth/openai/usage') {
        return {
          available: true,
          planType: 'plus',
          primary: {
            usedPercent: 4,
            windowSeconds: 18_000,
            resetAt: '2099-01-01T00:00:00.000Z'
          },
          secondary: {
            usedPercent: 24,
            windowSeconds: 604_800,
            resetAt: '2099-01-07T00:00:00.000Z'
          },
          fetchedAt: '2026-05-25T12:00:00.000Z'
        }
      }
      return {}
    })

    renderComposer()

    const badge = await screen.findByRole('button', { name: /ChatGPT.*96% left in the 5h window.*76% left this week/i })
    const branch = await screen.findByRole('button', { name: 'main' })
    expect(Boolean(branch.compareDocumentPosition(badge) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
    expect(badge).not.toHaveAttribute('title')
    expect(badge.querySelector('img')).toBeNull()
    expect(badge.querySelector('svg[data-provider-mark="openai"]')).toBeInTheDocument()
    expect(screen.queryByText('96% 5h')).toBeNull()
    expect(screen.queryByText('76% wk')).toBeNull()

    fireEvent.mouseEnter(badge.parentElement as HTMLElement)
    expect(await screen.findByRole('tooltip')).toHaveTextContent('96% 5h, 76% wk')

    fireEvent.click(badge)

    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'ChatGPT' })).toBeInTheDocument()
    expect(screen.getByText('96% left')).toBeInTheDocument()
    expect(screen.getByText('76% left')).toBeInTheDocument()
  })

  it('matches the running stop button to the enabled send button style and shows Esc as a shortcut keycap', async () => {
    useConversationStore.setState({
      turnStatus: 'running',
      activeTurnId: 'turn-123'
    })

    renderComposer()

    const stopButton = screen.getByRole('button', { name: 'Stop turn' })

    expect(stopButton).toBeInTheDocument()

    fireEvent.mouseEnter(stopButton.parentElement as HTMLElement)
    const tooltip = await screen.findByRole('tooltip')

    expect(within(tooltip).getByText('Stop')).toBeInTheDocument()
    expect(within(tooltip).getByText('Esc')).toBeInTheDocument()
    expect(tooltip).not.toHaveTextContent('Stop (Esc)')
  })

  it('shows a single pending interruption until the terminal turn event arrives', async () => {
    let acceptInterrupt: (() => void) | undefined
    appServerSendRequest.mockImplementationOnce(() => new Promise<void>((resolve) => {
      acceptInterrupt = resolve
    }))
    useConversationStore.setState({
      turnStatus: 'running',
      activeTurnId: 'turn-123'
    })
    renderComposer()

    fireEvent.click(screen.getByRole('button', { name: 'Stop turn' }))

    const stoppingButton = await screen.findByRole('button', { name: 'Stopping turn' })
    expect(stoppingButton).toBeDisabled()
    expect(stoppingButton).toHaveAttribute('aria-busy', 'true')
    expect(useConversationStore.getState().interruptingTurnId).toBe('turn-123')
    expect(appServerSendRequest).toHaveBeenCalledTimes(1)

    fireEvent.click(stoppingButton)
    expect(appServerSendRequest).toHaveBeenCalledTimes(1)

    await act(async () => acceptInterrupt?.())
    expect(screen.getByRole('button', { name: 'Stopping turn' })).toBeDisabled()

    act(() => {
      useConversationStore.getState().onTurnCancelled({
        id: 'turn-123',
        threadId: 'thread-1',
        status: 'cancelled',
        startedAt: new Date().toISOString(),
        completedAt: new Date().toISOString(),
        items: []
      }, 'Cancelled by request')
    })

    expect(useConversationStore.getState().interruptingTurnId).toBeNull()
    expect(screen.queryByRole('button', { name: 'Stopping turn' })).toBeNull()
  })

  it('restores the stop control and reports an interruption request failure', async () => {
    appServerSendRequest.mockRejectedValueOnce(new Error('connection lost'))
    useConversationStore.setState({
      turnStatus: 'running',
      activeTurnId: 'turn-123'
    })
    renderComposer()

    fireEvent.click(screen.getByRole('button', { name: 'Stop turn' }))

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Stop turn' })).toBeEnabled()
    })
    expect(useConversationStore.getState().interruptingTurnId).toBeNull()
    expect(useToastStore.getState().toasts).toEqual(expect.arrayContaining([
      expect.objectContaining({
        message: 'Failed to stop turn: connection lost',
        type: 'error'
      })
    ]))
  })

  it('keeps the model picker available while a turn is running', () => {
    useConversationStore.setState({
      turnStatus: 'running',
      activeTurnId: 'turn-123'
    })

    renderComposer()

    const modelButton = screen.getByRole('button', { name: 'Select model' })
    expect(modelButton).toBeEnabled()

    fireEvent.click(modelButton)
    expect(screen.getByRole('menu', { name: 'Select model' })).toBeInTheDocument()
  })

  it.each(['compacting', 'consolidating'] as const)(
    'keeps the model picker available while thread maintenance is %s',
    (maintenanceKind) => {
      useConversationStore.setState({
        turnStatus: 'idle',
        activeTurnId: null,
        maintenanceKind
      })

      renderComposer()

      const modelButton = screen.getByRole('button', { name: 'Select model' })
      expect(modelButton).toBeEnabled()

      fireEvent.click(modelButton)
      expect(screen.getByRole('menu', { name: 'Select model' })).toBeInTheDocument()
    }
  )

  it.each(['waitingApproval', 'waitingInput'] as const)(
    'keeps the model picker disabled while the turn is %s',
    (turnStatus) => {
      useConversationStore.setState({ turnStatus })

      renderComposer()

      expect(screen.getByRole('button', { name: 'Select model' })).toBeDisabled()
    }
  )

  it('keeps the model picker disabled while model controls are unavailable', () => {
    renderComposer({ modelDisabled: true })

    expect(screen.getByRole('button', { name: 'Select model' })).toBeDisabled()
  })

  it('shows the queued send action instead of stop while running with draft text', () => {
    useConversationStore.setState({
      turnStatus: 'running',
      activeTurnId: 'turn-123'
    })

    renderComposer()

    const textbox = screen.getByRole('textbox')
    textbox.textContent = 'follow up'
    fireEvent.input(textbox)

    expect(screen.getByRole('button', { name: 'Queue message' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Stop turn' })).toBeNull()
  })

  it('queues Enter submissions while thread maintenance is active', async () => {
    useConversationStore.setState({
      turnStatus: 'idle',
      activeTurnId: null,
      maintenanceKind: 'consolidating'
    })

    renderComposer()

    const textbox = screen.getByRole('textbox')
    textbox.textContent = 'next while memory is consolidating'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter', code: 'Enter' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('turn/enqueue', expect.objectContaining({
        threadId: 'thread-1'
      }))
    })
    expect(appServerSendRequest).not.toHaveBeenCalledWith('turn/start', expect.anything())
  })

  it('sends directly while background memory consolidation is active', async () => {
    useConversationStore.setState({
      turnStatus: 'idle',
      activeTurnId: null,
      maintenanceKind: null,
      backgroundMemoryStatus: 'consolidating'
    })

    renderComposer()

    const textbox = screen.getByRole('textbox')
    textbox.textContent = 'next while memory is consolidating'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter', code: 'Enter' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('turn/start', expect.objectContaining({
        threadId: 'thread-1'
      }))
    })
    expect(appServerSendRequest).not.toHaveBeenCalledWith('turn/enqueue', expect.anything())
  })

  it('cycles recent user messages with ArrowUp and ArrowDown while preserving the current draft', () => {
    useConversationStore.setState({
      turns: [
        userTurn('turn-old', 'older request'),
        userTurn('turn-new', 'newer request')
      ]
    })
    renderComposer()

    const textbox = screen.getByRole('textbox')
    textbox.textContent = 'current draft'
    fireEvent.input(textbox)
    setCaretToEnd(textbox)

    fireEvent.keyDown(textbox, { key: 'ArrowUp', code: 'ArrowUp' })
    expect(textbox.textContent).toBe('newer request')

    fireEvent.keyDown(textbox, { key: 'ArrowUp', code: 'ArrowUp' })
    expect(textbox.textContent).toBe('older request')

    fireEvent.keyDown(textbox, { key: 'ArrowDown', code: 'ArrowDown' })
    expect(textbox.textContent).toBe('newer request')

    fireEvent.keyDown(textbox, { key: 'ArrowDown', code: 'ArrowDown' })
    expect(textbox.textContent).toBe('current draft')
  })

  it('uses maintenance interrupt for an empty busy composer without an active turn', async () => {
    useConversationStore.setState({
      turnStatus: 'idle',
      activeTurnId: null,
      maintenanceKind: 'consolidating'
    })

    renderComposer()

    fireEvent.click(screen.getByRole('button', { name: 'Stop turn' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/maintenance/interrupt', {
        threadId: 'thread-1'
      })
    })
  })

  it('summarizes queued non-text inputs with localized labels', async () => {
    settingsGet.mockResolvedValue({ locale: 'zh-Hans' })
    useConversationStore.setState({
      queuedInputs: [
        {
          id: 'queued-1',
          threadId: 'thread-1',
          displayText: '',
          status: 'queued',
          createdAt: new Date().toISOString(),
          nativeInputParts: [
            { type: 'fileRef', path: 'docs/a.md' },
            { type: 'fileRef', path: 'docs/b.md' },
            { type: 'localImage', path: 'C:\\temp\\diagram.png' }
          ]
        },
        {
          id: 'queued-2',
          threadId: 'thread-1',
          displayText: '',
          status: 'queued',
          createdAt: new Date().toISOString(),
          nativeInputParts: []
        }
      ]
    })

    renderComposer()

    await waitFor(() => {
      expect(screen.getByText('2 个文件, 1 张图片')).toBeInTheDocument()
    })
    expect(screen.getByText('已排队消息')).toBeInTheDocument()
  })

  it('does not enter edit mode or expose an edit cancel action from composer', () => {
    renderComposer()

    expect((window as Window & { __inputComposerEditLastMessage?: unknown }).__inputComposerEditLastMessage).toBeUndefined()
    expect(screen.queryByRole('button', { name: 'Cancel' })).toBeNull()
  })
})
