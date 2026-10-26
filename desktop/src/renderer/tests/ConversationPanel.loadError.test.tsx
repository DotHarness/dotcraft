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
import type { Thread } from '../types/thread'

function makeThread(id: string): Thread {
  return {
    id,
    displayName: `Thread ${id}`,
    status: 'active',
    originChannel: 'dotcraft-desktop',
    createdAt: '2024-01-01T00:00:00Z',
    lastActiveAt: '2024-01-01T12:00:00Z',
    workspacePath: 'X:\\fixtures\\workspace',
    userId: 'local',
    metadata: {},
    turns: []
  }
}

describe('ConversationPanel load error', () => {
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
    useUIStore.setState({ activeMainView: 'conversation', planApprovalDismissed: {} })
  })

  it('shows the selected thread failure while the previous thread object is still retained', () => {
    const threads = useThreadStore.getState()
    threads.setActiveThreadId('thread-a')
    threads.setActiveThread(makeThread('thread-a'))
    threads.setActiveThreadId('thread-b')
    threads.setActiveThreadLoadError('thread-b', 'Thread history is unavailable.')
    expect(useThreadStore.getState().activeThread?.id).toBe('thread-a')

    render(<LocaleProvider><ConversationPanel workspacePath="X:\\fixtures\\workspace" /></LocaleProvider>)

    expect(screen.getByRole('alert')).toBeInTheDocument()
  })
})
