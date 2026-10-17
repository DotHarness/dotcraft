import { beforeEach, describe, expect, it } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ToolCallCard } from '../components/conversation/ToolCallCard'
import { useConversationStore } from '../stores/conversationStore'
import type { ConversationItem } from '../types/conversation'
import { coreToolFields } from './testToolPresentation'
import { installDesktopApiMock } from './desktopApiMock'

function taskStop(taskId: string, result: Record<string, unknown> | string | undefined, extra: Partial<ConversationItem> = {}): ConversationItem {
  return {
    id: `stop-${taskId}`,
    type: 'toolCall',
    status: 'completed',
    toolName: 'TaskStop',
    ...coreToolFields('TaskStop'),
    presentation: { presentationId: 'core.task', options: { operation: 'stop', kind: taskId.startsWith('term_') ? 'shell' : taskId.startsWith('run_') ? 'workflow' : 'agent' } },
    toolCallId: `call-stop-${taskId}`,
    arguments: { taskId },
    result: typeof result === 'string' || result === undefined ? result : JSON.stringify(result),
    success: true,
    createdAt: '2026-10-17T10:00:00.000Z',
    ...extra
  }
}

function renderRow(item: ConversationItem, turnRunning = false) {
  return render(<LocaleProvider loadSettings={false}><ToolCallCard threadId="thread" turnId="turn" item={item} turnRunning={turnRunning} /></LocaleProvider>)
}

describe('TaskStop tool row', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
    installDesktopApiMock({ settings: { get: async () => ({ locale: 'en' }) } })
  })

  it('is a static row when the stopped task left no message', () => {
    renderRow(taskStop('term_1', { taskId: 'term_1', kind: 'shell', label: 'npm run dev', status: 'stopped' }))
    expect(screen.getByTestId('tool-row')).toHaveAttribute('data-expandable', 'false')
    expect(screen.queryByRole('button', { expanded: false })).toBeNull()
  })

  it('expands to the last message a stopped subagent left', () => {
    renderRow(taskStop('/root/reviewer', {
      taskId: '/root/reviewer', kind: 'agent', label: 'reviewer', status: 'stopped', lastAgentMessage: 'Checked two of five files.'
    }))
    const row = screen.getByRole('button', { expanded: false })
    expect(screen.queryByText('Checked two of five files.')).toBeNull()
    fireEvent.click(row)
    expect(screen.getByRole('button', { expanded: true })).toBe(row)
    expect(screen.getByText('Checked two of five files.')).toBeInTheDocument()
  })

  it('keeps a failed stop on the generic row with its error', () => {
    renderRow(taskStop('run_missing', "'run_missing' matches no background task you own.", { success: false }))
    const row = screen.getByRole('button', { expanded: false })
    expect(screen.queryByTestId('tool-expanded-content')).toBeNull()
    fireEvent.click(row)
    expect(screen.getByTestId('tool-expanded-content')).toHaveTextContent('matches no background task you own')
  })

  it('does not expand while the stop is running', () => {
    renderRow(taskStop('run_1', undefined, { status: 'started', success: undefined }), true)
    expect(screen.getByTestId('tool-row')).toHaveAttribute('data-expandable', 'false')
    expect(document.querySelector('[data-running-shimmer]')).toBeInTheDocument()
  })
})
