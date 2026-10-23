import './setupPluginRuntime'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { ComponentProps } from 'react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { InputComposer } from '../components/conversation/InputComposer'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import { useProvidersStore } from '../stores/providersStore'
import { useSatellitesStore } from '../stores/satellitesStore'
import { useSubAgentStore } from '../stores/subAgentStore'
import { useThreadStore } from '../stores/threadStore'
import { useToastStore } from '../stores/toastStore'
import { useUIStore } from '../stores/uiStore'
import { normalizeGitPathKey, useGitStore } from '../stores/gitStore'
import { useComposerDraftStore } from '../stores/composerDraftStore'
import { useVoiceStore } from '../voice/voiceStore'
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

function renderComposer(extraProps: Partial<ComponentProps<typeof InputComposer>> = {}): ReturnType<typeof render> {
  return render(
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
        workspace: { saveImageToTemp: vi.fn(), readImageAsDataUrl, getPathForFile: () => 'C:\\sample\\workspace\\.dockerignore' },
        voice: undefined
      })

    useConversationStore.getState().reset()
    useConnectionStore.getState().reset()
    useModelCatalogStore.getState().reset()
    useProvidersStore.getState().reset()
    useSubAgentStore.getState().reset()
    useThreadStore.getState().reset()
    useSatellitesStore.setState({ satellites: [], supported: false, loaded: false, bootstrapped: false })
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
      snapshot: { model: { phase: 'missing', bytesDownloaded: 0, bytesTotal: null }, chatGpt: { signedIn: false, enabled: true }, sessions: [], capacity: 2 },
      recording: null,
      finalizing: null,
      microphonePermission: 'unknown',
      deviceFallback: false,
      localErrors: {}
    })
    useToastStore.setState({ toasts: [] })
    useUIStore.setState({
      activeMainView: 'conversation',
      sidebarCollapsed: false,
      sidebarWidth: 240,
      detailPanelVisible: true,
      detailPanelWidth: 400,
      activeDetailTab: 'changes',
      selectedChangeKey: null,
      composerPrefill: null,
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
    fireEvent.click(screen.getByRole('button', { name: 'Queued message actions' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Edit queued message' }))

    await waitFor(() => {
      expect(useToastStore.getState().toasts.some((toast) =>
        toast.message === 'Failed to edit queued message: stale queue'
      )).toBe(true)
    })
    expect(useConversationStore.getState().queuedInputs).toHaveLength(1)
    expect(textbox.textContent).toBe('keep this draft')
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

  it('preserves an idle draft when turn start races with a regular active turn', async () => {
    appServerSendRequest.mockImplementation((method: string) =>
      method === 'turn/start'
        ? Promise.reject(new Error('Thread already has a running turn'))
        : Promise.resolve({}))
    renderComposer()

    const textbox = screen.getByRole('textbox')
    textbox.textContent = 'keep this racing submission'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter', code: 'Enter' })

    await waitFor(() => expect(textbox.textContent).toBe('keep this racing submission'))
    expect(appServerSendRequest).not.toHaveBeenCalledWith('turn/enqueue', expect.anything())
    expect(appServerSendRequest).not.toHaveBeenCalledWith('turn/steer', expect.anything())
  })
})
