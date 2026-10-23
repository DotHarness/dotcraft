import { expect, it } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { AutomationToolCard, parseAutomationResult } from '../components/conversation/AutomationToolCard'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import { automationScheduleSummary } from '../utils/automationScheduleSummary'
import type { ConversationItem } from '../types/conversation'
const automation = { id: 'a', name: 'CI review', prompt: 'Review the build', status: 'active', executionMode: 'independent', notificationPolicy: 'important', schedule: { kind: 'daily', hour: 9, minute: 0, timeZone: 'UTC' } }
const item = (result: unknown) => ({ id: 'tool', result } as ConversationItem)
it('opens a created automation as a detail panel tab without leaving the conversation', () => {
  useThreadStore.setState({ activeThreadId: 'thread-1' })
  render(<AutomationToolCard item={item({ operation: 'create', automation })} locale="en" />)
  const card = screen.getByRole('button', { name: /CI review/ })
  fireEvent.click(card)
  fireEvent.click(card)
  const tabs = useViewerTabStore.getState().getThreadState('thread-1').tabs.filter(tab => tab.kind === 'automation')
  expect(tabs).toEqual([expect.objectContaining({ automationId: 'a', label: 'CI review' })])
  expect(useUIStore.getState().activeMainView).toBe('conversation')
  expect(useUIStore.getState().activeDetailTab).toEqual({ kind: 'viewer', id: tabs[0]!.id })
})
it('shows list count and expands entries', () => {
  render(<AutomationToolCard item={item({ operation: 'list', automations: [automation] })} locale="en" />)
  expect(screen.getByTestId('tool-row')).toHaveTextContent('1')
  fireEvent.click(screen.getByTestId('tool-row'))
  expect(screen.getByRole('button', { name: 'CI review' })).toBeInTheDocument()
})
it('falls back for unknown operations and invalid payloads', () => {
  expect(parseAutomationResult({ operation: 'future-operation' })).toBeNull()
  expect(parseAutomationResult({ operation: 'list', automations: [{}] })).toBeNull()
})
it('keeps a deleted definition as a non-interactive snapshot', () => {
  render(<AutomationToolCard item={item({ operation: 'delete', automation })} locale="en" />)
  expect(screen.getByRole('button', { name: 'CI review' })).toBeDisabled()
})
it('reports an accepted manual run as queued', () => {
  const run = { id: 'run-a', automationId: 'a', status: 'queued' }
  render(<AutomationToolCard item={item({ operation: 'run', automation, run })} locale="en" />)
  expect(screen.getByTestId('tool-row')).toHaveTextContent('Run queued CI review')
  fireEvent.click(screen.getByTestId('tool-row'))
  expect(screen.getByText('Queued')).toBeInTheDocument()
})
it('formats one-time schedules in their explicit time zone with an honest fallback', () => {
  const at = '2026-09-08T01:00:00Z'
  expect(automationScheduleSummary({ kind: 'at', at, timeZone: 'Etc/GMT-8' }, 'en')).toContain('9:00:00 AM (Etc/GMT-8)')
  expect(automationScheduleSummary({ kind: 'at', at, timeZone: 'invalid-zone' }, 'en')).toContain('1:00:00 AM (UTC)')
})
