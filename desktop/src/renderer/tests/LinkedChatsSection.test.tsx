import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { LinkedChatSummary } from '../../shared/linkedChats'
import { LinkedChatsSection } from '../components/conversation/summary/LinkedChatsSection'
import type { SummaryServices } from '../components/conversation/summary/summaryServices'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useLinkedChatsStore } from '../stores/linkedChatsStore'
import { installDesktopApiMock } from './desktopApiMock'

function chat(threadId: string, title: string, followed: boolean): LinkedChatSummary {
  return {
    threadId, projectId: 'project', projectName: 'Project', workspacePath: '/project', title, status: 'running',
    ...(followed ? { followed: true as const } : {}), createdAt: '2026-11-26T09:00:00.000Z'
  }
}

function show(chats: LinkedChatSummary[], result: { ok: true } | { ok: false; error: string }) {
  const unfollowLinkedChat = vi.fn(async () => result)
  installDesktopApiMock({ threadTasks: { linkedChats: async () => chats, unfollowLinkedChat } })
  useLinkedChatsStore.getState().setLinkedChats('parent', chats)
  const showError = vi.fn()
  const services = { ui: { showError } } as unknown as SummaryServices
  render(<LocaleProvider loadSettings={false}><LinkedChatsSection services={services} threadId="parent" /></LocaleProvider>)
  return { unfollowLinkedChat, showError }
}

afterEach(() => {
  cleanup()
  useLinkedChatsStore.setState({ byParent: {} })
})

describe('Linked chats Summary section', () => {
  it('offers Stop following only for followed chats and removes the row once unfollowed', async () => {
    const host = show([chat('started', 'Started here', false), chat('followed', 'Followed chat', true)], { ok: true })
    const buttons = screen.getAllByRole('button', { name: 'Stop following' })
    expect(buttons).toHaveLength(1)
    fireEvent.click(buttons[0])
    await waitFor(() => expect(screen.queryByText('Followed chat')).toBeNull())
    expect(host.unfollowLinkedChat).toHaveBeenCalledWith({ threadId: 'parent', childThreadId: 'followed', projectId: 'project' })
    expect(screen.getByText('Started here')).toBeTruthy()
  })

  it('keeps the row and reports a failed unfollow', async () => {
    const host = show([chat('followed', 'Followed chat', true)], { ok: false, error: 'nope' })
    fireEvent.click(screen.getByRole('button', { name: 'Stop following' }))
    await waitFor(() => expect(host.showError).toHaveBeenCalledWith('Unable to stop following the chat'))
    expect(screen.getByText('Followed chat')).toBeTruthy()
  })
})
