import './setupPluginRuntime'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ConversationWelcome } from '../components/conversation/ConversationWelcome'
import { useConnectionStore } from '../stores/connectionStore'
import { useConfigStore } from '../stores/configStore'
import { normalizeGitPathKey, useGitStore } from '../stores/gitStore'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import { useProvidersStore } from '../stores/providersStore'
import { useSatellitesStore } from '../stores/satellitesStore'
import { useThreadRouteStore } from '../stores/threadRouteStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useSkillsStore } from '../stores/skillsStore'
import { useToastStore } from '../stores/toastStore'
import { useAppBindingStore } from '../stores/appBindingStore'
import { useConversationStore } from '../stores/conversationStore'
import { useComposerDraftStore } from '../stores/composerDraftStore'
import { useVoiceStore } from '../voice/voiceStore'
import { installDesktopApiMock } from './desktopApiMock'
import {
  clearDesktopPluginRegistry,
} from '../plugins/desktopPluginRegistry'

const fileReadFile = vi.fn()
const appServerSendRequest = vi.fn()
let serverConfig: Record<string, unknown> = {}
const saveImageToTemp = vi.fn()
const getPathForFile = vi.fn((file: File) => file.name === 'notes.txt' ? 'C:\\temp\\notes.txt' : '')
const settingsGet = vi.fn()
const shellOpenExternal = vi.fn()
const shellOpenAppHandoff = vi.fn()
const shellGetProtocolHandlerName = vi.fn()
const gitListBranches = vi.fn()
const gitCheckoutBranch = vi.fn()
const gitCreateAndCheckoutBranch = vi.fn()

class ResizeObserverMock {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}

Object.defineProperty(window, 'ResizeObserver', {
  configurable: true,
  writable: true,
  value: ResizeObserverMock
})
Object.defineProperty(globalThis, 'ResizeObserver', {
  configurable: true,
  writable: true,
  value: ResizeObserverMock
})

function renderWelcome({
  remoteWorkspace = false,
  projectKey
}: {
  remoteWorkspace?: boolean
  projectKey?: string
} = {}) {
  return render(
    <LocaleProvider>
      <ConversationWelcome
        workspacePath={'X:\\fixtures\\workspace'}
        projectKey={projectKey}
        remoteWorkspace={remoteWorkspace}
      />
    </LocaleProvider>
  )
}

describe('ConversationWelcome composer', () => {
  beforeEach(() => {
    clearDesktopPluginRegistry()
    vi.clearAllMocks()
    delete (window as Window & { __confirmDialog?: unknown }).__confirmDialog

    useConnectionStore.getState().reset()
    useConfigStore.getState().reset()
    serverConfig = {
      ProviderId: '',
      ProviderPreferences: {},
      Permissions: { DefaultApprovalPolicy: 'default' }
    }
    useComposerDraftStore.setState({ draftsByThread: {} })
    useVoiceStore.setState({
      initialized: false,
      snapshot: { model: { phase: 'missing', bytesDownloaded: 0, bytesTotal: null }, chatGpt: { signedIn: false, enabled: true }, sessions: [], capacity: 2 },
      recording: null,
      finalizing: null,
      localErrors: {}
    })
    useGitStore.getState().reset()
    useGitStore.setState({
      branchesByPath: {
        [normalizeGitPathKey('X:\\fixtures\\workspace')]: {
          path: 'X:\\fixtures\\workspace',
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
    useThreadStore.getState().reset()
    useSatellitesStore.setState({ satellites: [], supported: false, loaded: false, bootstrapped: false })
    useThreadRouteStore.setState({
      supported: false,
      hosts: [],
      routes: {},
      pendingRoute: null,
      deferredRoutes: {},
      connecting: null,
      attempted: new Set<string>(),
      generation: 0
    })
    useConversationStore.getState().reset()
    useConversationStore.setState({ remoteWorkspaceActive: false })
    useModelCatalogStore.getState().reset()
    useProvidersStore.getState().reset()
    useAppBindingStore.getState().reset()
    useToastStore.setState({ toasts: [] })
    useSkillsStore.setState({
      skills: [],
      loading: false,
      error: null,
      selectedSkillName: null,
      skillContent: null,
      contentLoading: false
    })
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
      pendingThreadCreation: null,
      welcomeDraft: null,
      welcomeDraftsByWorkspace: {},
      _pendingWelcomeTimer: null
    })

    useConnectionStore.setState({
      status: 'connected',
      serverInfo: null,
      dashboardUrl: null,
      errorMessage: null,
      errorType: null,
      binarySource: null,
      capabilities: {
        commandManagement: true,
        skillsManagement: true,
        modelCatalogManagement: true,
        workspaceConfigManagement: true
      }
    })
    useModelCatalogStore.setState({
      status: 'ready',
      providerId: 'openai',
      requestedProviderId: 'openai',
      modelOptions: ['gpt-5.4', 'gpt-5.4-mini'],
      modelListUnsupportedEndpoint: false
    })

    fileReadFile.mockResolvedValue('{}')
    settingsGet.mockResolvedValue({ locale: 'en' })
    shellOpenExternal.mockResolvedValue(undefined)
    shellOpenAppHandoff.mockResolvedValue(undefined)
    shellGetProtocolHandlerName.mockResolvedValue('Workflow App')
    gitListBranches.mockResolvedValue({
      current: 'main',
      detachedHead: null,
      branches: [{ name: 'main', current: true }]
    })
    gitCheckoutBranch.mockResolvedValue(undefined)
    gitCreateAndCheckoutBranch.mockResolvedValue(undefined)
    getPathForFile.mockImplementation((file: File) => file.name === 'notes.txt' ? 'C:\\temp\\notes.txt' : '')
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'command/list') {
        return {
          commands: [
            {
              name: '/code-review',
              aliases: ['/cr'],
              description: 'Review files',
              category: 'custom',
              requiresAdmin: false
            }
          ]
        }
      }
      if (method === 'skills/list') {
        return {
          skills: [
            {
              name: 'memory',
              description: 'Recall project context',
              source: 'builtin',
              enabled: true,
              path: '/skills/memory/SKILL.md'
            }
          ]
        }
      }
      if (method === 'thread/start') {
        return {
          thread: {
            id: 'thread-welcome',
            displayName: 'Welcome thread',
            status: 'active',
            originChannel: 'dotcraft-desktop',
            createdAt: '2026-04-16T08:00:00.000Z',
            lastActiveAt: '2026-04-16T08:00:00.000Z'
          }
        }
      }
      if (method === 'command/execute') {
        return { handled: true, expandedPrompt: 'Generate AGENTS.md' }
      }
      return {}
    })

    installDesktopApiMock({
        settings: {
          get: settingsGet
        },
        appServer: {
          sendRequest: (method: string, ...rest: unknown[]) => method === 'config/read'
            ? Promise.resolve({ config: structuredClone(serverConfig), origins: {} })
            : appServerSendRequest(method, ...rest),
          onNotification: undefined
        },
        file: {
          readFile: fileReadFile
        },
        git: {
          listBranches: gitListBranches,
          checkoutBranch: gitCheckoutBranch,
          createAndCheckoutBranch: gitCreateAndCheckoutBranch
        },
        workspace: {
          saveImageToTemp,
          getPathForFile
        },
        shell: {
          openExternal: shellOpenExternal,
          openAppHandoff: shellOpenAppHandoff,
          getProtocolHandlerName: shellGetProtocolHandlerName,
          listOpenTargets: async () => []
        },
        voice: undefined
      })
  })

  afterEach(() => {
    act(() => clearDesktopPluginRegistry())
  })

  it('keeps the full-access workspace default inherited on the created thread', async () => {
    serverConfig = { ...serverConfig, ...{
      Permissions: {
        DefaultApprovalPolicy: 'autoApprove'
      }
    } }

    renderWelcome()

    const approvalTrigger = await screen.findByTestId('approval-policy-trigger')
    await waitFor(() => {
      expect(approvalTrigger).toHaveTextContent('Full access')
    })

    const textbox = await screen.findByRole('textbox')
    textbox.textContent = 'Use the configured workspace default'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => {
      expect(useUIStore.getState().pendingWelcomeTurn).toMatchObject({
        threadId: 'thread-welcome',
        text: 'Use the configured workspace default'
      })
    })
    const start = appServerSendRequest.mock.calls.find(([method]) => method === 'thread/start')
    expect((start?.[1] as { config?: Record<string, unknown> })?.config).not.toHaveProperty('approvalPolicy')
  })

  it('writes an explicit approval override in thread/start', async () => {
    serverConfig = { ...serverConfig, ...{
      Permissions: {
        DefaultApprovalPolicy: 'autoApprove'
      }
    } }

    renderWelcome()

    const approvalTrigger = await screen.findByTestId('approval-policy-trigger')
    await waitFor(() => expect(approvalTrigger).toHaveTextContent('Full access'))
    fireEvent.click(approvalTrigger)
    fireEvent.click(screen.getByTestId('approval-policy-option-prompt'))

    const textbox = await screen.findByRole('textbox')
    textbox.textContent = 'Require approval for this thread'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => expect(useUIStore.getState().pendingWelcomeTurn?.threadId).toBe('thread-welcome'))
    const start = appServerSendRequest.mock.calls.find(([method]) => method === 'thread/start')
    expect(start?.[1]).toEqual(expect.objectContaining({
      config: expect.objectContaining({ approvalPolicy: 'prompt' })
    }))
  })

  it('writes an explicit full-access override in thread/start', async () => {
    ;(window as Window & { __confirmDialog?: unknown }).__confirmDialog = vi.fn().mockResolvedValue(true)
    renderWelcome()

    const approvalTrigger = await screen.findByTestId('approval-policy-trigger')
    await waitFor(() => expect(approvalTrigger).toHaveTextContent('Ask for approval'))
    fireEvent.click(approvalTrigger)
    fireEvent.click(screen.getByTestId('approval-policy-option-autoApprove'))

    const textbox = await screen.findByRole('textbox')
    textbox.textContent = 'Allow this thread to proceed automatically'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => expect(useUIStore.getState().pendingWelcomeTurn?.threadId).toBe('thread-welcome'))
    const start = appServerSendRequest.mock.calls.find(([method]) => method === 'thread/start')
    expect(start?.[1]).toEqual(expect.objectContaining({
      config: expect.objectContaining({ approvalPolicy: 'autoApprove' })
    }))
  })

  it('resolves a legacy default welcome draft approval policy to the current workspace default', async () => {
    useUIStore.getState().setWelcomeDraft({
      text: 'legacy default policy draft',
      images: [],
      mode: 'agent',
      model: 'Default',
      approvalPolicy: 'default'
    })
    serverConfig = { ...serverConfig, ...{
      Permissions: {
        DefaultApprovalPolicy: 'autoApprove'
      }
    } }

    renderWelcome()

    const textbox = await screen.findByRole('textbox')
    await waitFor(() => {
      expect(textbox.textContent).toContain('legacy default policy draft')
      expect(screen.getByTestId('approval-policy-trigger')).toHaveTextContent('Full access')
    })

    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => {
      expect(useUIStore.getState().pendingWelcomeTurn).toMatchObject({
        threadId: 'thread-welcome',
        text: 'legacy default policy draft'
      })
    })
    const start = appServerSendRequest.mock.calls.find(([method]) => method === 'thread/start')
    expect((start?.[1] as { config?: Record<string, unknown> })?.config).not.toHaveProperty('approvalPolicy')
  })

  it('returns the submission to the welcome composer when thread creation fails', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'command/list') return { commands: [] }
      if (method === 'skills/list') return { skills: [] }
      if (method === 'thread/start') throw new Error('workspace is offline')
      return {}
    })

    renderWelcome()
    const textbox = await screen.findByRole('textbox')
    textbox.textContent = 'Keep this after creation fails'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => expect(useToastStore.getState().toasts.length).toBe(1))
    expect(useUIStore.getState().pendingThreadCreation).toBeNull()
    expect(useThreadStore.getState().activeThreadId).toBeNull()
    expect(useUIStore.getState().getWelcomeDraftForWorkspace('X:\\fixtures\\workspace')?.text)
      .toBe('Keep this after creation fails')
    expect(appServerSendRequest).not.toHaveBeenCalledWith('thread/delete', expect.anything())
  })
})
