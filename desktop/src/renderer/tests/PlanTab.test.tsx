// @vitest-environment jsdom
import { beforeEach, describe, expect, it } from 'vitest'
import { act, render, screen, type RenderResult } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { PlanTab } from '../components/detail/PlanTab'
import { useConversationStore } from '../stores/conversationStore'

function renderPlanTab(): RenderResult {
  return render(
    <LocaleProvider>
      <PlanTab />
    </LocaleProvider>
  )
}

describe('PlanTab', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        }
      }
    })
  })

  it('renders streaming CreatePlan as structured plan summary only', () => {
    const store = useConversationStore.getState()
    store.setTurns([
      {
        id: 'turn-1',
        threadId: 'thread-1',
        status: 'running',
        items: [],
        startedAt: new Date().toISOString()
      }
    ])

    store.onToolCallArgumentsDelta({
      turnId: 'turn-1',
      itemId: 'item-plan-1',
      delta: '{"plan":"# 实时计划\\n\\n## 概览\\n\\n正在写入计划正文。\\n\\n## 验证方案\\n\\n- 运行测试。","todos":[{"id":"verify","content":"Run tests","status":"pending"}]}',
      toolName: 'CreatePlan',
      callId: 'call-1'
    })

    const { container } = renderPlanTab()

    // Partial-streaming state renders arrived content as-is, with no spinner and
    // no visible "drafting" label.
    expect(screen.queryByText('Drafting plan…')).toBeNull()
    expect(screen.getByText('实时计划')).toBeInTheDocument()
    expect(screen.getAllByText('正在写入计划正文。').length).toBeGreaterThan(0)
    expect(screen.queryByText('验证方案')).toBeNull()
    expect(screen.queryByText('运行测试。')).toBeNull()
    expect(screen.getByText('Run tests')).toBeInTheDocument()
    expect(container.querySelector('[data-plan-todo-status="pending"]')).toBeInTheDocument()
  })

  it('keeps the streaming placeholder until overview or todos are available', () => {
    const store = useConversationStore.getState()
    store.setTurns([
      {
        id: 'turn-1',
        threadId: 'thread-1',
        status: 'running',
        items: [],
        startedAt: new Date().toISOString()
      }
    ])

    store.onToolCallArgumentsDelta({
      turnId: 'turn-1',
      itemId: 'item-plan-2',
      delta: '{"plan":"# 实时计划\\n\\n## 概览',
      toolName: 'CreatePlan',
      callId: 'call-2'
    })

    renderPlanTab()

    // Nothing has arrived yet → a shape-matched skeleton stands in for the plan.
    // The "drafting" string survives only as the skeleton's accessible label.
    expect(screen.getByRole('status', { name: 'Drafting plan…' })).toBeInTheDocument()
    expect(screen.queryByText('实时计划')).toBeNull()
  })

  it('renders streaming todos even before overview is available', () => {
    const store = useConversationStore.getState()
    store.setTurns([
      {
        id: 'turn-1',
        threadId: 'thread-1',
        status: 'running',
        items: [],
        startedAt: new Date().toISOString()
      }
    ])

    store.onToolCallArgumentsDelta({
      turnId: 'turn-1',
      itemId: 'item-plan-3',
      delta: '{"plan":"# 实时计划","todos":[{"id":"verify","content":"Run tests","status":"pending"}]}',
      toolName: 'CreatePlan',
      callId: 'call-3'
    })

    const { container } = renderPlanTab()

    // Partial-streaming state: real content, no spinner, no visible label.
    expect(screen.queryByText('Drafting plan…')).toBeNull()
    expect(screen.getByText('实时计划')).toBeInTheDocument()
    expect(screen.getByText('Run tests')).toBeInTheDocument()
    expect(container.querySelector('[data-plan-todo-status="pending"]')).toBeInTheDocument()
  })

  it('renders restored thread plan state and clears it on thread reset', () => {
    const store = useConversationStore.getState()
    store.onPlanUpdated({
      title: 'Restored Plan',
      overview: 'Loaded from thread/read',
      content: '# Restored Plan',
      todos: [
        { id: 'restore', content: 'Restore todos', status: 'in_progress' },
        { id: 'done', content: 'Completed todo', status: 'completed' },
        { id: 'cancel', content: 'Cancelled todo', status: 'cancelled' }
      ]
    })

    const { container } = renderPlanTab()

    expect(screen.getByText('Restored Plan')).toBeInTheDocument()
    expect(screen.getByText('Loaded from thread/read')).toBeInTheDocument()
    expect(screen.getByText('Restore todos')).toBeInTheDocument()
    expect(screen.getByText('Completed todo')).toBeInTheDocument()
    expect(screen.getByText('Cancelled todo')).toBeInTheDocument()
    expect(container.querySelector('[data-plan-todo-status="in_progress"]')).toBeInTheDocument()
    expect(container.querySelector('[data-plan-todo-status="completed"]')).toBeInTheDocument()
    expect(container.querySelector('[data-plan-todo-status="cancelled"]')).toBeInTheDocument()

    act(() => {
      store.reset()
    })

    expect(screen.queryByText('Restored Plan')).toBeNull()
    expect(screen.queryByText('Restore todos')).toBeNull()
    expect(screen.queryByText('Completed todo')).toBeNull()
    expect(screen.queryByText('Cancelled todo')).toBeNull()

    act(() => {
      store.onPlanUpdated({
        title: 'Next Thread Plan',
        overview: 'Hydrated after switching threads',
        content: '# Next Thread Plan',
        todos: [{ id: 'next', content: 'Next thread todo', status: 'pending' }]
      })
    })

    expect(screen.getByText('Next Thread Plan')).toBeInTheDocument()
    expect(screen.getByText('Next thread todo')).toBeInTheDocument()
    expect(container.querySelector('[data-plan-todo-status="pending"]')).toBeInTheDocument()
  })
})
