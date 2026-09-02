import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AgentResponseBlock } from '../components/conversation/AgentResponseBlock'
import { SubAgentChips } from '../components/conversation/SubAgentChips'
import { SubagentsTab } from '../components/detail/SubagentsTab'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useSubAgentStore } from '../stores/subAgentStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { ToolCallCard } from '../components/conversation/ToolCallCard'
import { deferred, makeSpawn, makeSubAgent } from './subAgentFixtures'
import { installDesktopApiMock } from './desktopApiMock'

const request = vi.fn()
const store = useSubAgentStore.getState

beforeEach(() => {
  request.mockReset().mockResolvedValue({ data: [] })
  installDesktopApiMock({ settings: { get: async () => ({ locale: 'en' }) }, appServer: { sendRequest: request } })
  store().reset()
  useConnectionStore.getState().reset()
  useThreadStore.getState().reset()
  useThreadStore.setState({ activeThreadId: 'parent-A' })
})

function chips(items = [makeSpawn()]) {
  return render(
    <LocaleProvider>
      <SubAgentChips items={items} parentThreadId="parent-B" turnRunning={false} />
    </LocaleProvider>
  )
}

function seedCollidingAgents() {
  store().setChildren('parent-A', [makeSubAgent({ childThreadId: 'child-A', parentThreadId: 'parent-A', nickname: 'Core A', runtime: undefined, isCompleted: true })])
  store().setChildren('parent-B', [makeSubAgent()])
}

describe('source conversation rendering and navigation', () => {
  it.each([1, 2])('uses the rendered parent for %i spawn entries when a different task is selected', (count) => {
    seedCollidingAgents()
    render(<LocaleProvider><AgentResponseBlock turn={{
      id: 'turn-B', threadId: 'parent-B', status: 'completed', startedAt: '',
      items: Array.from({ length: count }, (_, index) => makeSpawn(`spawn-${index}`))
    }} /></LocaleProvider>)
    expect(screen.getByText('started working')).toBeInTheDocument()
    expect(screen.queryByText('Core A')).toBeNull()
    fireEvent.click(screen.getAllByRole('button', { name: /Core B/ })[0])
    expect(useThreadStore.getState().activeThreadId).toBe('child-B')
  })

  it('uses the same scoped identity for a single control row and its click target', () => {
    seedCollidingAgents()
    const item = makeSpawn('followup', { toolName: 'FollowupTask',
      presentation: { presentationId: 'core.subagent', options: { operation: 'followupTask' } },
      arguments: { target: '/root/review_core', message: 'Continue' } })
    render(<LocaleProvider><ToolCallCard threadId="parent-B" turnId="turn-B" item={item} /></LocaleProvider>)
    expect(screen.queryByText('Core A')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: /Updated Core B/ }))
    expect(useThreadStore.getState().activeThreadId).toBe('child-B')
  })

  it('returns an unresolved entry to its source parent and opens the Subagent tab', () => {
    store().setChildren('parent-A', [makeSubAgent({ childThreadId: 'child-A', parentThreadId: 'parent-A' })])
    chips()
    fireEvent.click(screen.getByRole('button', { name: /Core/ }))
    expect(useThreadStore.getState().activeThreadId).toBe('parent-B')
    expect(useUIStore.getState().activeMainView).toBe('conversation')
    expect(useUIStore.getState().activeDetailTab).toEqual({ kind: 'system', id: 'subagents' })
  })

  it('keeps an explicit ID authoritative when a different agent matches the path', () => {
    seedCollidingAgents()
    store().setChildren('parent-B', [makeSubAgent(), makeSubAgent({ childThreadId: 'explicit', nickname: 'Explicit', agentPath: '/root/other', runtime: undefined, isCompleted: true })])
    chips([makeSpawn('spawn', { result: JSON.stringify({ childThreadId: 'explicit', agentPath: '/root/review_core', agentNickname: 'Raw', status: 'running' }) })])
    expect(screen.getByText('finished')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /Explicit/ }))
    expect(useThreadStore.getState().activeThreadId).toBe('explicit')
  })
})

describe('discovery-dependent activity', () => {
  it('hydrates a cold preview only once across multiple rows and converges to runtime state', async () => {
    const response = deferred<unknown>()
    request.mockReturnValue(response.promise)
    useConnectionStore.setState({ capabilities: { subAgentSessions: true } })
    render(<LocaleProvider>{[1, 2].map((index) => <AgentResponseBlock key={index} shellRuntimeScope="review" turn={{
      id: `turn-${index}`, threadId: 'parent-B', status: 'completed', startedAt: '', items: [makeSpawn(`spawn-${index}`)]
    }} />)}</LocaleProvider>)
    expect(screen.getAllByText('started working')).toHaveLength(2)
    expect(request).toHaveBeenCalledExactlyOnceWith('subagent/children/list', {
      parentThreadId: 'parent-B', includeClosed: true, includeThreads: true
    })
    await act(async () => response.resolve({ data: [{ edge: {
      childThreadId: 'child-B', agentPath: '/root/review_core', status: 'open'
    }, thread: { id: 'child-B', displayName: 'Core B', runtime: { running: false } } }] }))
    expect(screen.getAllByText('finished')).toHaveLength(2)
    expect(request).toHaveBeenCalledTimes(1)
  })

  it('finishes historical entries after successful empty discovery, including during refresh', async () => {
    useConnectionStore.setState({ capabilities: { subAgentSessions: true } })
    chips()
    await screen.findByText('finished')
    const response = deferred<unknown>()
    request.mockReturnValue(response.promise)
    let refresh!: Promise<void>
    act(() => { refresh = store().fetchChildren('parent-B') })
    expect(screen.getByText('finished')).toBeInTheDocument()
    await act(async () => { response.resolve({ data: [] }); await refresh })
  })

  it('does not treat an earlier empty discovery as completion for a live turn', () => {
    useSubAgentStore.setState({
      discoveryByParent: new Map([['parent-B', { status: 'ready', discovered: true }]])
    })

    render(<LocaleProvider><AgentResponseBlock isRunning turn={{
      id: 'turn-B', threadId: 'parent-B', status: 'running', startedAt: '', items: [makeSpawn()]
    }} /></LocaleProvider>)

    expect(screen.getByText('started working')).toBeInTheDocument()
    expect(screen.queryByText('finished')).toBeNull()
  })

  it('keeps only names and navigation after an initial load fails', async () => {
    request.mockRejectedValue(new Error('offline'))
    useConnectionStore.setState({ capabilities: { subAgentSessions: true } })
    const { container } = chips()
    await waitFor(() => expect(store().discoveryByParent.get('parent-B')?.status).toBe('error'))
    expect(screen.queryByText('started working')).toBeNull()
    expect(screen.queryByText('finished')).toBeNull()
    expect(container.querySelector('.tool-running-gradient-text')).toBeNull()
    expect(screen.getByRole('button', { name: /Core/ })).toBeEnabled()
  })

  it('preserves a known running child when refresh fails', async () => {
    store().setChildren('parent-B', [makeSubAgent()])
    request.mockRejectedValue(new Error('offline'))
    useConnectionStore.setState({ capabilities: { subAgentSessions: true } })
    chips()
    await waitFor(() => expect(store().discoveryByParent.get('parent-B')?.status).toBe('error'))
    expect(screen.getByText('started working')).toBeInTheDocument()
  })

  it('settles a discovered child without runtime information and keeps closed rows consistent with the panel', async () => {
    useThreadStore.setState({ activeThreadId: 'parent-B' })
    useConnectionStore.setState({ capabilities: { subAgentSessions: true } })
    request.mockImplementation(async (method: string) => method === 'subagent/children/list'
      ? { data: [{ edge: { childThreadId: 'child-B', agentPath: '/root/review_core', agentNickname: 'Core', status: 'open' } }] }
      : { thread: { id: 'child-B', turns: [] }, data: [] })
    render(<LocaleProvider><div data-testid="transcript"><SubAgentChips items={[makeSpawn()]} parentThreadId="parent-B" turnRunning={false} /></div><SubagentsTab /></LocaleProvider>)
    await screen.findByText('finished')
    expect(screen.getByText('Done · 1')).toBeInTheDocument()
    act(() => store().setChildren('parent-B', [makeSubAgent({ nickname: 'Core', status: 'closed', isCompleted: true })]))
    act(() => store().updateChildRuntime('child-B', { running: true, waitingOnApproval: false, waitingOnPlanConfirmation: false }))
    expect(within(screen.getByTestId('transcript')).getByText('finished')).toBeInTheDocument()
    expect(screen.getByText('Active · 0')).toBeInTheDocument()
    expect(screen.getByText('Closed · 1')).toBeInTheDocument()
  })

  it('keeps a spawn without a returned result working even after discovery', () => {
    useSubAgentStore.setState({ discoveryByParent: new Map([['parent-B', { status: 'ready', discovered: true }]]) })
    chips([makeSpawn('pending', { result: undefined })])
    expect(screen.getByText('started working')).toBeInTheDocument()
  })

  it('does not report a mixed group finished when discovery failed for an unresolved child', () => {
    store().setChildren('parent-B', [makeSubAgent({ runtime: undefined, isCompleted: true })])
    useSubAgentStore.setState({ discoveryByParent: new Map([['parent-B', { status: 'error', discovered: false }]]) })
    const { container } = chips([makeSpawn(), makeSpawn('unknown', {
      result: JSON.stringify({ agentPath: '/root/missing', agentNickname: 'Missing', status: 'running' })
    })])
    expect(screen.queryByText('finished')).toBeNull()
    expect(screen.queryByText('started working')).toBeNull()
    expect(container.querySelector('.tool-running-gradient-text')).toBeNull()
    expect(screen.getByRole('button', { name: /Missing/ })).toBeEnabled()
  })

  it('shows an explicit child failure as interrupted', () => {
    store().setChildren('parent-B', [makeSubAgent({ status: 'failed', runtime: undefined, isCompleted: true })])
    chips()
    expect(screen.getByText('interrupted')).toBeInTheDocument()
  })

  it('prioritizes failure over running entries', () => {
    store().setChildren('parent-B', [makeSubAgent()])
    chips([makeSpawn(), makeSpawn('failed', { success: false })])
    expect(screen.getByText('interrupted')).toBeInTheDocument()
    expect(screen.queryByText('started working')).toBeNull()
  })
})
