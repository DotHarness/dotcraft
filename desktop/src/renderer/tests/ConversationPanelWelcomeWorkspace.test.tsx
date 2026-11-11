import './setupPluginRuntime'
import type { ReactNode } from 'react'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { ConversationPanel } from '../components/layout/ConversationPanel'
import { LocaleProvider } from '../contexts/LocaleContext'
import { clearDesktopPluginRegistry } from '../plugins/desktopPluginRegistry'
import { useComposerDraftStore } from '../stores/composerDraftStore'
import { useConfigStore } from '../stores/configStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useGitStore } from '../stores/gitStore'
import { activateWorkspaceResources } from '../stores/providerConnectionScope'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import type { WorkspaceResourceScope } from '../stores/workspaceResourceScope'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { useVoiceStore } from '../voice/voiceStore'
import { installDesktopApiMock } from './desktopApiMock'
import { activateTestWorkspace } from './workspaceResourceFixtures'

vi.mock('../components/conversation/welcomeBackdrop/WelcomeBackdrop', () => ({
  ConversationBackdrop: ({ children }: { children: ReactNode }) => children
}))
vi.mock('../components/conversation/InputComposer', () => ({ InputComposer: () => null }))
vi.mock('../components/conversation/MessageStream', () => ({ MessageStream: () => null }))
vi.mock('../components/conversation/ThreadHeader', () => ({ ThreadHeader: () => null }))
vi.mock('../components/conversation/ThreadCreatingContent', () => ({ ThreadCreatingContent: () => null }))

const first: WorkspaceResourceScope = {
  projectKey: 'C:\\workspace-a', connectionKey: 'C:\\workspace-a', connectionId: 'a-1'
}
const second: WorkspaceResourceScope = {
  projectKey: 'C:\\workspace-b', connectionKey: 'C:\\workspace-b', connectionId: 'b-1'
}
const sendRequest = vi.fn()
const configuration = { ProviderId: '', ProviderPreferences: {}, Permissions: { DefaultApprovalPolicy: 'prompt' } }

function panel(scope: WorkspaceResourceScope) {
  return <LocaleProvider><ConversationPanel
    workspacePath={scope.projectKey}
    projectKey={scope.projectKey}
    workspaceReady
  /></LocaleProvider>
}

async function activate(scope: WorkspaceResourceScope): Promise<void> {
  useConnectionStore.setState({
    status: 'connected', connectionKey: scope.connectionKey, connectionId: scope.connectionId,
    capabilities: { workspaceConfigManagement: true }
  })
  activateWorkspaceResources(scope)
  await useConfigStore.getState().refresh()
}

beforeEach(async () => {
  localStorage.clear()
  clearDesktopPluginRegistry()
  useConnectionStore.getState().reset()
  useThreadStore.getState().reset()
  useConversationStore.getState().reset()
  useWorkspaceProjectsStore.getState().reset()
  useGitStore.getState().reset()
  useComposerDraftStore.setState({ draftsByThread: {} })
  useVoiceStore.setState({
    initialized: false, recording: null, finalizing: null,
    snapshot: { model: { phase: 'missing', bytesDownloaded: 0, bytesTotal: null },
      chatGpt: { signedIn: false, enabled: true }, sessions: [], capacity: 2 }
  })
  useUIStore.setState({
    activeMainView: 'conversation', composerPrefill: null, pendingWelcomeTurn: null,
    pendingThreadCreation: null, pendingProjectThreadOpen: null,
    welcomeDraft: null, welcomeDraftsByWorkspace: {}, welcomeDraftWorkspacePath: null
  })
  sendRequest.mockReset().mockImplementation(async (method: string) => {
    if (method === 'config/read') return { config: configuration }
    if (method === 'thread/start') return new Promise(() => {})
    return {}
  })
  installDesktopApiMock({
    appServer: { sendRequest, onNotification: () => () => {} },
    settings: { get: async () => ({ locale: 'en' }), set: async () => {} },
    git: { listBranches: async () => ({ current: 'main', detachedHead: null, branches: [] }) },
    shell: { listOpenTargets: async () => [] },
    voice: undefined
  })
  activateTestWorkspace(first)
  await activate(first)
})

it('keeps Welcome drafts and explicit approval choices isolated when switching projects', async () => {
  const ui = useUIStore.getState()
  ui.setWelcomeDraft({
    text: 'Draft for workspace A', images: [], mode: 'agent', model: 'Default', approvalPolicy: 'autoApprove'
  }, first.projectKey)
  ui.setWelcomeDraft({
    text: 'Draft for workspace B', images: [], mode: 'agent', model: 'Default'
  }, second.projectKey)
  const view = render(panel(first))
  await waitFor(() => expect(screen.getByRole('textbox')).toHaveTextContent('Draft for workspace A'))
  const firstInput = screen.getByRole('textbox')
  firstInput.textContent = 'Edited draft for workspace A'
  fireEvent.input(firstInput)

  await act(async () => {
    await activate(second)
    view.rerender(panel(second))
  })
  await waitFor(() => expect(screen.getByRole('textbox')).toHaveTextContent('Draft for workspace B'))

  await act(async () => {
    await activate(first)
    view.rerender(panel(first))
  })
  await waitFor(() => expect(screen.getByRole('textbox')).toHaveTextContent('Edited draft for workspace A'))
  expect(ui.getWelcomeDraftForWorkspace(first.projectKey)?.approvalPolicy).toBe('autoApprove')
  fireEvent.click(screen.getByTestId('approval-policy-trigger'))
  expect(screen.getByTestId('approval-policy-option-autoApprove')).toHaveAttribute('aria-selected', 'true')
  fireEvent.click(screen.getByTestId('approval-policy-trigger'))

  await act(async () => {
    await activate(second)
    view.rerender(panel(second))
  })
  await waitFor(() => expect(screen.getByRole('textbox')).toHaveTextContent('Draft for workspace B'))
  fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

  await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('thread/start', expect.objectContaining({
    identity: expect.objectContaining({ workspacePath: second.projectKey })
  }), undefined, { attachmentOwner: 'warm' }))
  const start = sendRequest.mock.calls.find(([method]) => method === 'thread/start')
  expect((start?.[1] as { config: Record<string, unknown> }).config).not.toHaveProperty('approvalPolicy')
  expect(useUIStore.getState().pendingThreadCreation?.text).toBe('Draft for workspace B')
  expect(ui.getWelcomeDraftForWorkspace(first.projectKey)?.text).toBe('Edited draft for workspace A')
})
