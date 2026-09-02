import { act, render, screen } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { MessageStream } from '../components/conversation/MessageStream'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConversationStore } from '../stores/conversationStore'
import { useSubAgentStore } from '../stores/subAgentStore'
import { useThreadStore } from '../stores/threadStore'
import { installDesktopApiMock } from './desktopApiMock'
import { makeSubAgent } from './subAgentFixtures'

vi.mock('../hooks/useAutoScroll', () => ({
  useAutoScroll: () => ({ scrollRef: { current: null }, showScrollButton: true, scrollToBottom: vi.fn() })
}))

beforeEach(() => {
  useConversationStore.getState().reset()
  useSubAgentStore.getState().reset()
  useThreadStore.getState().reset()
  useThreadStore.setState({ activeThreadId: 'parent-B' })
  installDesktopApiMock({ settings: { get: async () => ({ locale: 'en' }) } })
})

it('positions the scroll button above queued input independently of subagent history', async () => {
  await act(async () => { render(<LocaleProvider><MessageStream /></LocaleProvider>) })
  const scrollButtonFrame = screen.getByRole('button', { name: 'Scroll to bottom' }).parentElement!
  const baseline = parseFloat(scrollButtonFrame.style.bottom)
  expect(Number.isFinite(baseline)).toBe(true)

  act(() => useSubAgentStore.getState().setChildren('parent-B', [makeSubAgent()]))
  expect(parseFloat(scrollButtonFrame.style.bottom)).toBe(baseline)

  act(() => useConversationStore.getState().setQueuedInputs([{
    id: 'queued-1', threadId: 'parent-B', displayText: 'Continue',
    status: 'queued', createdAt: '2026-09-02T00:00:00Z'
  }]))
  const withQueue = parseFloat(scrollButtonFrame.style.bottom)
  expect(withQueue).toBeGreaterThan(baseline)

  act(() => useSubAgentStore.getState().setChildren('parent-B', [makeSubAgent({ status: 'closed' })]))
  expect(parseFloat(scrollButtonFrame.style.bottom)).toBe(withQueue)
  act(() => useConversationStore.getState().setQueuedInputs([]))
  expect(parseFloat(scrollButtonFrame.style.bottom)).toBe(baseline)
})
