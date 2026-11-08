import { act, fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { TurnActivitySummary } from '../components/conversation/TurnActivitySummary'
import { useTurnActivityDisclosureStore } from '../stores/turnActivityDisclosureStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { useToolCallRevealStore } from '../stores/toolCallRevealStore'

function summary(threadId: string, turnId = 'turn') {
  return <LocaleProvider loadSettings={false}>
    <TurnActivitySummary threadId={threadId} turnId={turnId} status="worked" collapsible>
      {() => <button type="button">Process action</button>}
    </TurnActivitySummary>
  </LocaleProvider>
}

beforeEach(() => {
  useTurnActivityDisclosureStore.setState({ expanded: new Map() })
  useWorkspaceProjectsStore.setState({ foregroundProjectId: 'workspace-a' })
  useToolCallRevealStore.setState({ request: null, target: null })
})

describe('turn activity disclosure', () => {
  it('restores a user choice synchronously without mounting another identity’s folded body', () => {
    const first = render(summary('thread-a'))
    expect(screen.queryByRole('button', { name: 'Process action', hidden: true })).toBeNull()
    fireEvent.click(screen.getByRole('button', { expanded: false }))
    expect(screen.getByRole('button', { expanded: true })).toBeInTheDocument()
    first.unmount()

    const restored = render(summary('thread-a'))
    expect(screen.getByRole('button', { expanded: true })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Process action' })).toBeInTheDocument()
    restored.rerender(summary('thread-b'))
    expect(screen.getByRole('button', { expanded: false })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Process action', hidden: true })).toBeNull()
    restored.rerender(summary('thread-a', 'another-turn'))
    expect(screen.getByRole('button', { expanded: false })).toBeInTheDocument()
    restored.rerender(summary('thread-a'))
    expect(screen.getByRole('button', { expanded: true })).toBeInTheDocument()

    act(() => useWorkspaceProjectsStore.setState({ foregroundProjectId: 'workspace-b' }))
    expect(screen.getByRole('button', { expanded: false })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Process action', hidden: true })).toBeNull()
  })
})
