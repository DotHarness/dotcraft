import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { todoWindow, TurnTodoCard, type TurnTodo, type TurnTodoPlan, type TurnTodoStatus } from '../components/conversation/TurnTodoCard'
import { useTurnTasksClaimed } from '../components/conversation/turnTaskClaims'
import { useSummaryView, setPopoverOpen } from '../components/conversation/summary/view'
import type { RunningWork } from '../components/conversation/summary/store'
import { useDetailPanelStore } from '../stores/detailPanelStore'

const EMPTY_WORK: RunningWork = { total: 0, subagents: 0, others: 0, othersKind: 'task' }
const runningWork = vi.hoisted(() => ({ value: { total: 0, subagents: 0, others: 0, othersKind: 'task' } as RunningWork }))
vi.mock('../components/conversation/summary/store', () => ({ useRunningWork: () => runningWork.value }))

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

function card(status: TurnTodoStatus, plan: TurnTodoPlan, waitingOnTasks = false) {
  return (
    <LocaleProvider loadSettings={false}>
      <TurnTodoCard threadId="thread" turnId="turn" status={status} waitingOnTasks={waitingOnTasks} plan={plan} />
      <Claimed />
      <Popover />
    </LocaleProvider>
  )
}

afterEach(() => {
  cleanup()
  runningWork.value = EMPTY_WORK
  act(() => {
    setPopoverOpen(false)
    useDetailPanelStore.setState({ byThread: new Map() })
  })
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
  runningWork.value = { total: 2, subagents: 1, others: 1, othersKind: 'workflow' }
  const { rerender } = render(card('failed', PLAN))
  expect(screen.getByRole('status', { name: 'Claimed' })).toHaveTextContent('true')
  fireEvent.click(screen.getByRole('button'))
  expect(screen.getByRole('status', { name: 'Summary' })).toHaveTextContent('true')

  runningWork.value = EMPTY_WORK
  rerender(card('failed', PLAN))
  expect(screen.getByRole('status', { name: 'Claimed' })).toHaveTextContent('false')
  expect(screen.queryByRole('button')).not.toBeInTheDocument()
})

it('opens the Subagents detail panel when only subagents are working', () => {
  runningWork.value = { total: 1, subagents: 1, others: 0, othersKind: 'task' }
  render(card('completed', PLAN, true))
  fireEvent.click(screen.getByRole('button'))
  expect(useDetailPanelStore.getState().getCurrentPanel().activeDetailTab).toEqual({ kind: 'system', id: 'subagents' })
  expect(screen.getByRole('status', { name: 'Summary' })).toHaveTextContent('false')
})

it('keeps the background tasks button reachable when every step of a long list is complete', () => {
  runningWork.value = { total: 1, subagents: 0, others: 1, othersKind: 'workflow' }
  const { rerender } = render(card('completed', { ...PLAN, todos: Array.from({ length: 14 }, (_, i) => ({
    id: String(i), content: `Step ${i + 1}`, status: 'completed'
  })) }, true))
  fireEvent.click(screen.getByRole('button'))
  expect(screen.getByRole('status', { name: 'Summary' })).toHaveTextContent('true')
  rerender(card('completed', PLAN))
  expect(screen.getByRole('status', { name: 'Claimed' })).toHaveTextContent('false')
})

it('leaves the running task count to the working line while the turn runs', () => {
  runningWork.value = { total: 1, subagents: 1, others: 0, othersKind: 'task' }
  render(card('running', { ...PLAN, updatedTodos: true }))
  expect(screen.getByRole('status', { name: 'Claimed' })).toHaveTextContent('false')
  expect(screen.queryByRole('button')).not.toBeInTheDocument()
})

function steps(done: number, total: number): TurnTodo[] {
  return Array.from({ length: total }, (_, i) => ({
    id: String(i),
    content: `Step ${i + 1}`,
    status: i < done ? 'completed' : i === done ? 'in_progress' : 'pending'
  }))
}

it('shows every step of a list that fits the window', () => {
  expect(todoWindow(steps(5, 7))).toMatchObject({ hiddenBefore: 0, openAfter: 0 })
  expect(todoWindow(steps(5, 7)).visible).toHaveLength(7)
})

it('slides a long list so one finished step stays above the current one', () => {
  const early = todoWindow(steps(3, 16))
  expect(early).toMatchObject({ hiddenBefore: 2, openAfter: 2 })
  expect(early.visible[0]?.content).toBe('Step 3')

  expect(todoWindow(steps(10, 16))).toMatchObject({ hiddenBefore: 4, openAfter: 0 })
  expect(todoWindow(steps(16, 16))).toMatchObject({ hiddenBefore: 4, openAfter: 0 })
})

it('counts the steps outside the window as plain text', () => {
  render(card('running', { ...PLAN, todos: steps(3, 16) }))
  expect(screen.getByText('2 earlier steps')).toBeInTheDocument()
  expect(screen.getByText('2 more to go')).toBeInTheDocument()
  expect(screen.queryByRole('button')).not.toBeInTheDocument()
})
