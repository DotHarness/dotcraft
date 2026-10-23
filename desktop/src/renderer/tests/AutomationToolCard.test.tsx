import { expect, it } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { AutomationToolCard, parseAutomationResult } from '../components/conversation/AutomationToolCard'
import { automationScheduleSummary } from '../utils/automationScheduleSummary'
import type { ConversationItem } from '../types/conversation'
const automation = { id: 'a', name: 'CI review', prompt: 'Review the build', status: 'active', executionMode: 'independent', notificationPolicy: 'important', schedule: { kind: 'daily', hour: 9, minute: 0, timeZone: 'UTC' } }
const item = (result: unknown) => ({ id: 'tool', result } as ConversationItem)
it('uses the scheduled-work reference treatment and reveals execution details', () => {
  render(<AutomationToolCard item={item({ operation: 'create', automation })} locale="en" />)
  expect(screen.getByTestId('tool-row')).toHaveTextContent('Created automation CI review')
  expect(screen.getByRole('button', { name: 'CI review' }).querySelector('svg')).toBeInTheDocument()
  fireEvent.click(screen.getByTestId('tool-row'))
  expect(screen.getByText('Daily · 09:00 (UTC)')).toBeInTheDocument()
  expect(screen.getByText(/New conversation each run/)).toBeInTheDocument()
  expect(screen.getByText(/Important updates/)).toBeInTheDocument()
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
