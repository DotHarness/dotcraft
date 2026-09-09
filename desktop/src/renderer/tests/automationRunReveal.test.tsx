import { useRef } from 'react'
import { beforeEach, expect, it, vi } from 'vitest'
import { render, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useAutomationRunReveal } from '../hooks/useAutomationRunReveal'
import { useAutomationRunNavigation } from '../stores/automationRunNavigation'
import { useThreadStore } from '../stores/threadStore'
import { useConversationStore } from '../stores/conversationStore'
import { useAutomationsStore } from '../stores/automationsStore'
import type { AutomationRun } from '../types/automation'
import { installDesktopApiMock } from './desktopApiMock'

const target: AutomationRun = { id: 'run', automationId: 'a', definitionVersion: 1, status: 'succeeded', threadId: 't', turnId: 'turn', createdAt: '', deliveryStatus: 'sent' }
function Surface({ displayed }: { displayed: boolean }) {
  const ref = useRef<HTMLDivElement>(null)
  useAutomationRunReveal(ref)
  return <div ref={ref}>{displayed && <div data-turn-id="turn">Result</div>}</div>
}
beforeEach(() => {
  HTMLElement.prototype.scrollIntoView = vi.fn()
  installDesktopApiMock({ settings: { get: async () => ({ locale: 'en' }) } })
  useAutomationRunNavigation.setState({ target })
  useThreadStore.setState({ activeThreadId: 't', activeHistoryCursors: { threadId: 't', turnCursor: null, itemCursor: null } })
  useConversationStore.setState({ turns: [{ id: 'turn' }] as any })
})
it('waits for the actual turn element before marking read', async () => {
  const mark = vi.spyOn(useAutomationsStore.getState(), 'markRunsRead').mockResolvedValue()
  const view = render(<LocaleProvider><Surface displayed={false} /></LocaleProvider>)
  expect(mark).not.toHaveBeenCalled()
  view.rerender(<LocaleProvider><Surface displayed /></LocaleProvider>)
  await waitFor(() => expect(mark).toHaveBeenCalledExactlyOnceWith('a', ['run'], true))
  mark.mockRestore()
})
it('keeps missing historical results unread', async () => {
  useConversationStore.setState({ turns: [] })
  const mark = vi.spyOn(useAutomationsStore.getState(), 'markRunsRead').mockResolvedValue()
  render(<LocaleProvider><Surface displayed={false} /></LocaleProvider>)
  await waitFor(() => expect(useAutomationRunNavigation.getState().target).toBeNull())
  expect(mark).not.toHaveBeenCalled()
  mark.mockRestore()
})
