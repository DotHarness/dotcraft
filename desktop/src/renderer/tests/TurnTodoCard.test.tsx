import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { TurnTodoCard, type TurnTodoPlan, type TurnTodoStatus } from '../components/conversation/TurnTodoCard'
import { useTurnTasksClaimed } from '../components/conversation/turnTaskClaims'
import { useSummaryView, setPopoverOpen } from '../components/conversation/summary/view'

const taskCount = vi.hoisted(() => ({ value: 0 }))
vi.mock('../components/conversation/summary/store', () => ({ useRunningTaskCount: () => taskCount.value }))

const PLAN: TurnTodoPlan = {
  title: '',
  implementsPlan: false,
  updatedTodos: true,
  todos: [
    { id: 'one', content: 'Apply the change', status: 'completed' },
    { id: 'two', content: 'Verify', status: 'in_progress' },
    { id: 'three', content: 'Docs', status: 'pending' }
  ]
}

function Claimed() {
  return <output aria-label="Claimed">{String(useTurnTasksClaimed('turn'))}</output>
}

function Popover() {
  return <output aria-label="Summary">{String(useSummaryView().popoverOpen)}</output>
}

function card(status: TurnTodoStatus, plan: TurnTodoPlan) {
  return (
    <LocaleProvider loadSettings={false}>
      <TurnTodoCard threadId="thread" turnId="turn" status={status} plan={plan} />
      <Claimed />
      <Popover />
    </LocaleProvider>
  )
}

afterEach(() => {
  cleanup()
  taskCount.value = 0
  act(() => setPopoverOpen(false))
})

it.each([
  ['a list this turn wrote', PLAN],
  ['an approved plan without a write', { ...PLAN, implementsPlan: true, updatedTodos: false }]
])('hides %s after success even if work is unfinished', (_, plan) => {
  const { rerender } = render(card('running', plan))
  expect(screen.getByRole('region')).toBeInTheDocument()
  rerender(card('cancelled', plan))
  expect(screen.getByRole('region')).toBeInTheDocument()
  rerender(card('completed', plan))
  expect(screen.queryByRole('region')).not.toBeInTheDocument()
})

it('does not carry unfinished tasks into a turn that has not updated them', () => {
  const { rerender } = render(card('running', { ...PLAN, updatedTodos: false }))
  expect(screen.queryByRole('region')).not.toBeInTheDocument()
  rerender(card('completed', { ...PLAN, updatedTodos: false }))
  expect(screen.queryByRole('region')).not.toBeInTheDocument()
})

it('does not show a proposed plan as execution progress', () => {
  render(card('running', { title: 'Plan', implementsPlan: false, updatedTodos: false,
    todos: [{ id: 'one', content: 'Apply the change', status: 'pending' }] }))
  expect(screen.queryByRole('region')).not.toBeInTheDocument()
})

it('takes the running task count from the working line while it shows it', () => {
  taskCount.value = 2
  const { rerender } = render(card('failed', PLAN))
  expect(screen.getByRole('status', { name: 'Claimed' })).toHaveTextContent('true')
  fireEvent.click(screen.getByRole('button'))
  expect(screen.getByRole('status', { name: 'Summary' })).toHaveTextContent('true')

  taskCount.value = 0
  rerender(card('failed', PLAN))
  expect(screen.getByRole('status', { name: 'Claimed' })).toHaveTextContent('false')
  expect(screen.queryByRole('button')).not.toBeInTheDocument()
})

it('leaves the running task count to the working line while the turn runs', () => {
  taskCount.value = 1
  render(card('running', { ...PLAN, updatedTodos: true }))
  expect(screen.getByRole('status', { name: 'Claimed' })).toHaveTextContent('false')
  expect(screen.queryByRole('button', { name: '1 running task' })).not.toBeInTheDocument()
})
