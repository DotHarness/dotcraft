import './setupPluginRuntime'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { installDesktopApiMock } from './desktopApiMock'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ConversationPanel } from '../components/layout/ConversationPanel'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'

vi.mock('../components/conversation/ConversationWelcome', () => ({
  ConversationWelcome: () => <div data-testid="conversation-welcome" />
}))

const WORKSPACE_B = 'X:\\fixtures\\workspace-b'

describe('ConversationPanel project thread open', () => {
  beforeEach(() => {
    installDesktopApiMock({
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
      appServer: { sendRequest: vi.fn().mockResolvedValue({}) },
      file: { readFile: vi.fn().mockResolvedValue('{}') },
      shell: { listOpenTargets: vi.fn().mockResolvedValue([]) },
      workspace: { saveImageToTemp: vi.fn() }
    })
    useConversationStore.getState().reset()
    useConnectionStore.getState().reset()
    useThreadStore.getState().reset()
    useUIStore.setState({ activeMainView: 'conversation', planApprovalDismissed: {}, pendingProjectThreadOpen: null })
  })

  it('shows the opening state instead of the welcome while a thread from another workspace is pending', () => {
    useUIStore.getState().setPendingProjectThreadOpen({
      projectKey: WORKSPACE_B,
      workspacePath: WORKSPACE_B,
      threadId: 'thread-b'
    })

    render(<LocaleProvider><ConversationPanel workspacePath={WORKSPACE_B} projectKey={WORKSPACE_B} /></LocaleProvider>)

    expect(screen.getByRole('status')).toBeInTheDocument()
    expect(screen.queryByTestId('conversation-welcome')).not.toBeInTheDocument()
  })
})
