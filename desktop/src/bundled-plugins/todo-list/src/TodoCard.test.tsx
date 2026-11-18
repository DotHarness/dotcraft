import '../../../renderer/tests/setupPluginRuntime'
import { render, screen } from '@testing-library/react'
import type { DesktopPluginHost, DesktopPluginTurnPlan, DesktopPluginTurnStatusContext } from '@dotcraft/plugin'
import { expect, it } from 'vitest'
import { TodoCard } from './TodoCard'

const host = { environment: { locale: 'en' } } as DesktopPluginHost

function card(status: DesktopPluginTurnStatusContext['status'], plan: DesktopPluginTurnPlan) {
  return <TodoCard host={host} context={{ workspacePath: null, threadId: 'thread', turnId: 'turn', status, plan }} />
}

it('keeps the last active step visible after cancellation but hides it after success', () => {
  const plan: DesktopPluginTurnPlan = {
    title: '',
    implementsPlan: false,
    updatedTodos: true,
    todos: [{ id: 'one', content: 'Apply the change', status: 'in_progress' }]
  }
  const { rerender } = render(card('cancelled', plan))
  expect(screen.getByRole('region')).toBeInTheDocument()

  rerender(card('completed', plan))
  expect(screen.queryByRole('region')).not.toBeInTheDocument()

  rerender(card('completed', {
    ...plan,
    todos: [...plan.todos, { id: 'two', content: 'Update docs', status: 'pending' }]
  }))
  expect(screen.getByRole('region')).toBeInTheDocument()
})

it('does not show a proposed plan as execution progress', () => {
  render(card('running', {
    title: 'Plan',
    implementsPlan: false,
    updatedTodos: false,
    todos: [{ id: 'one', content: 'Apply the change', status: 'pending' }]
  }))
  expect(screen.queryByRole('region')).not.toBeInTheDocument()
})
