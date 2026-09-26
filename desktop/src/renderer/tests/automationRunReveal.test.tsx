import { useRef } from 'react'
import { beforeEach, expect, it, vi } from 'vitest'
import { render, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useAutomationRunReveal } from '../hooks/useAutomationRunReveal'
import { useAutomationRunNavigation } from '../stores/automationRunNavigation'
import { useThreadStore } from '../stores/threadStore'
import { useConversationStore } from '../stores/conversationStore'
import { useAutomationsStore } from '../stores/automationsStore'
import { applyThreadHistoryHead, beginThreadHistory } from '../stores/threadHistoryStore'
import type { AutomationRun } from '../types/automation'
import { installDesktopApiMock } from './desktopApiMock'
import { createFakeHistoryServer, loadedTurnIds, openThreadHistory, turnRange } from './threadHistoryFakeServer'

const target: AutomationRun = { id: 'run', automationId: 'a', definitionVersion: 1, status: 'succeeded', threadId: 't', turnId: 'turn', createdAt: '', deliveryStatus: 'sent' }
function Surface({ displayed }: { displayed: boolean }) {
  const ref = useRef<HTMLDivElement>(null)
  useAutomationRunReveal(ref)
  return <div ref={ref}>{displayed && <div data-turn-id="turn">Result</div>}</div>
}
function Transcript() {
  const ref = useRef<HTMLDivElement>(null)
  const turns = useConversationStore(s => s.turns)
  useAutomationRunReveal(ref)
  return <div ref={ref}>{turns.map(turn => <div key={turn.id} data-turn-id={turn.id} />)}</div>
}
beforeEach(() => {
  HTMLElement.prototype.scrollIntoView = vi.fn()
  installDesktopApiMock({ settings: { get: async () => ({ locale: 'en' }) } })
  useAutomationRunNavigation.setState({ target })
  useThreadStore.setState({ activeThreadId: 't' })
  useConversationStore.setState({ turns: [{ id: 'turn' }] as any })
  beginThreadHistory('t')
  applyThreadHistoryHead('t', [{ id: 'turn' }], null)
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
it('pages older history until the run turn is loaded', async () => {
  const server = createFakeHistoryServer('t', 12)
  installDesktopApiMock({ settings: { get: async () => ({ locale: 'en' }) }, appServer: { sendRequest: server.request } })
  useConversationStore.getState().reset()
  await openThreadHistory(server, 't')
  useAutomationRunNavigation.setState({ target: { ...target, turnId: 'turn-2' } })
  const mark = vi.spyOn(useAutomationsStore.getState(), 'markRunsRead').mockResolvedValue()
  render(<LocaleProvider><Transcript /></LocaleProvider>)
  await waitFor(() => expect(mark).toHaveBeenCalledExactlyOnceWith('a', ['run'], true))
  expect(loadedTurnIds()).toEqual(turnRange(1, 12))
  mark.mockRestore()
})
