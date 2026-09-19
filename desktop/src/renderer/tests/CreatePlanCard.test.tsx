import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import type { ConversationItem } from '../types/conversation'
import { CreatePlanCard } from '../components/conversation/CreatePlanCard'
import { installDesktopApiMock } from './desktopApiMock'

function renderWithLocale(node: JSX.Element): void {
  render(<LocaleProvider>{node}</LocaleProvider>)
}

describe('CreatePlanCard', () => {
  let writeTextMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    writeTextMock = vi.fn().mockResolvedValue(undefined)
    installDesktopApiMock({
        settings: {
          get: async () => ({ locale: 'en' })
        }
      })
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: {
        writeText: writeTextMock
      }
    })
  })

  it('renders markdown preview while CreatePlan is streaming', () => {
    const item: ConversationItem = {
      id: 'plan-streaming',
      type: 'toolCall',
      status: 'started',
      toolName: 'CreatePlan',
      toolCallId: 'call-1',
      argumentsPreview: '{"plan":"# Streaming Plan\\n\\n## Summary\\n\\nLive draft\\n\\n## Implementation Changes\\n\\n- item one"}',
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<CreatePlanCard item={item} locale="en" />)

    expect(screen.getByText('Planning')).toBeInTheDocument()
    expect(screen.getByText('Streaming Plan')).toBeInTheDocument()
    expect(screen.getAllByText('Live draft').length).toBeGreaterThan(0)
    expect(screen.getByText('item one')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Expand plan' }).length).toBeGreaterThan(0)
  })

  it('streams todo items from the partial preview while CreatePlan runs', () => {
    const slash = String.fromCharCode(92)
    const escapedTodo = `${slash}u7b2c${slash}u4e00${slash}u4e2a${slash}u4efb${slash}u52a1`
    const item: ConversationItem = {
      id: 'plan-streaming-todos',
      type: 'toolCall',
      status: 'started',
      toolName: 'CreatePlan',
      toolCallId: 'call-stream-todos',
      argumentsPreview:
        `{"plan":"# Streaming Plan\\n\\n## Summary\\n\\nDraft","todos":[{"id":"t1","content":"${escapedTodo}","status":"pending"},{"id":"t2","content":"Second task",`,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<CreatePlanCard item={item} locale="en" />)

    fireEvent.click(screen.getAllByRole('button', { name: 'Expand plan' })[0])

    // The closed todo object renders; the still-streaming one waits for its
    // closing brace, matching the Detail Panel's line-by-line behavior.
    expect(screen.getByText('第一个任务')).toBeInTheDocument()
    expect(screen.queryByText(escapedTodo)).toBeNull()
    expect(screen.queryByText('Second task')).toBeNull()
  })

  it('does not duplicate the overview when the plan markdown already shows it', () => {
    const item: ConversationItem = {
      id: 'plan-overview-dedup',
      type: 'toolCall',
      status: 'completed',
      toolName: 'CreatePlan',
      toolCallId: 'call-overview-dedup',
      arguments: {
        plan: '# Dedup Plan\n\n## Summary\n\nThe single summary line\n\n## Implementation Changes\n\n- step a'
      },
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<CreatePlanCard item={item} locale="en" />)

    // The summary appears once (inside the markdown content), not a second time
    // as a standalone overview hint above it.
    expect(screen.getAllByText('The single summary line')).toHaveLength(1)
  })

  it('expands full plan output and collapses back to preview', () => {
    const item: ConversationItem = {
      id: 'plan-complete',
      type: 'toolCall',
      status: 'completed',
      toolName: 'CreatePlan',
      toolCallId: 'call-2',
      arguments: {
        plan: '# Ship Plan\n\n## Summary\n\nTwo stages\n\n## Implementation Changes\n\n- step a',
        todos: [{ id: 'a', content: 'Stage A', status: 'pending' }]
      },
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<CreatePlanCard item={item} locale="en" />)

    expect(screen.getByText('Plan')).toBeInTheDocument()
    expect(screen.queryByText('Stage A')).toBeNull()
    fireEvent.click(screen.getAllByRole('button', { name: 'Expand plan' })[0])
    expect(screen.queryAllByRole('button', { name: 'Expand plan' }).length).toBe(0)
    expect(screen.getByText('Stage A')).toBeInTheDocument()
    expect(screen.queryByText('Overview')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Collapse plan' }))
    expect(screen.queryByText('Stage A')).toBeNull()
    expect(screen.getByText('Ship Plan')).toBeInTheDocument()
  })

  it('toggles using icon expand/collapse buttons', () => {
    const item: ConversationItem = {
      id: 'plan-toggle',
      type: 'toolCall',
      status: 'completed',
      toolName: 'CreatePlan',
      toolCallId: 'call-3',
      arguments: {
        plan: '# Toggle Plan\n\n## Summary\n\nPreview first\n\n- step'
      },
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<CreatePlanCard item={item} locale="en" />)

    fireEvent.click(screen.getAllByRole('button', { name: 'Expand plan' })[0])
    expect(screen.getByRole('button', { name: 'Collapse plan' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Collapse plan' }))
    expect(screen.getAllByRole('button', { name: 'Expand plan' }).length).toBeGreaterThan(0)
  })

  it('copies parsed content then falls back to generated markdown', async () => {
    const withContent: ConversationItem = {
      id: 'plan-copy-content',
      type: 'toolCall',
      status: 'completed',
      toolName: 'CreatePlan',
      toolCallId: 'call-4',
      arguments: {
        plan: '# Copy Plan\n\n## Summary\n\nBody\n\n- one'
      },
      success: true,
      createdAt: new Date().toISOString()
    }

    const fallbackOnly: ConversationItem = {
      id: 'plan-copy-fallback',
      type: 'toolCall',
      status: 'completed',
      toolName: 'CreatePlan',
      toolCallId: 'call-5',
      arguments: {
        title: 'Fallback Title',
        overview: 'Fallback overview',
        todos: [{ id: 'todo-1', content: 'First task', status: 'pending' }]
      },
      success: true,
      createdAt: new Date().toISOString()
    }

    const { rerender } = render(
      <LocaleProvider>
        <CreatePlanCard item={withContent} locale="en" />
      </LocaleProvider>
    )

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Copy plan' }))
      await Promise.resolve()
    })
    expect(writeTextMock).toHaveBeenCalledWith('## Summary\n\nBody\n\n- one')

    rerender(
      <LocaleProvider>
        <CreatePlanCard item={fallbackOnly} locale="en" />
      </LocaleProvider>
    )

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Copy plan' }))
      await Promise.resolve()
    })
    expect(writeTextMock).toHaveBeenLastCalledWith('# Fallback Title\n\nFallback overview\n\n- First task')
  })
})
