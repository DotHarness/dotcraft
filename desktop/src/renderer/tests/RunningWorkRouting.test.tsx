import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { TurnWorkingLine } from '../components/conversation/TurnWorkingLine'
import { runningWorkOf, type RunningWork } from '../components/conversation/summary/store'
import { setPopoverOpen, useSummaryView } from '../components/conversation/summary/view'
import { useDetailPanelStore } from '../stores/detailPanelStore'

const EMPTY_WORK: RunningWork = { total: 0, subagents: 0, others: 0, othersKind: 'task' }
const runningWork = vi.hoisted(() => ({ value: { total: 0, subagents: 0, others: 0, othersKind: 'task' } as RunningWork }))

vi.mock('../components/conversation/summary/store', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../components/conversation/summary/store')>()
  return { ...actual, useRunningWork: () => runningWork.value }
})

function SummaryPopoverState() {
  return <output aria-label="Summary">{String(useSummaryView().popoverOpen)}</output>
}

afterEach(() => {
  cleanup()
  runningWork.value = EMPTY_WORK
  act(() => {
    setPopoverOpen(false)
    useDetailPanelStore.setState({ byThread: new Map() })
  })
})

describe('running work navigation', () => {
  it('opens the Subagents detail panel when only subagents are working', () => {
    runningWork.value = runningWorkOf(1, 0, 0)
    render(
      <LocaleProvider loadSettings={false}>
        <TurnWorkingLine turnId="t1" working label={{ kind: 'thinking', title: 'Checking route' }} keepSpace afterTool={false} tasksThreadId="thread-1" />
        <SummaryPopoverState />
      </LocaleProvider>
    )
    fireEvent.click(screen.getByRole('button'))
    expect(useDetailPanelStore.getState().getCurrentPanel().activeDetailTab).toEqual({ kind: 'system', id: 'subagents' })
    expect(screen.getByRole('status', { name: 'Summary' })).toHaveTextContent('false')
  })

  it('opens the Summary when non-subagent or mixed background work is running', () => {
    runningWork.value = runningWorkOf(1, 1, 0)
    render(
      <LocaleProvider loadSettings={false}>
        <TurnWorkingLine turnId="t1" working={false} label={null} keepSpace afterTool={false} tasksThreadId="thread-1" />
        <SummaryPopoverState />
      </LocaleProvider>
    )
    fireEvent.click(screen.getByRole('button'))
    expect(screen.getByRole('status', { name: 'Summary' })).toHaveTextContent('true')
    expect(useDetailPanelStore.getState().getCurrentPanel().activeDetailTab).toEqual({ kind: 'launcher' })
  })
})
