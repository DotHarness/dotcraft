// @vitest-environment jsdom
import { act, fireEvent, render } from '@testing-library/react'
import { useRef } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { PetCommand, PetEvent, PetSnapshot } from '../../shared/desktopPet'
import { useDesktopPet } from '../components/desktopPet/useDesktopPet'
import { usePetEditorBridge } from '../components/desktopPet/editorBridge'
import { PET_ADOPTION_GRACE_MS, endPetSource } from '../components/desktopPet/desktopPetSource'
import { useConversationStore, type PendingApproval } from '../stores/conversationStore'
import type { ConversationTurn } from '../types/conversation'
import { approvalRequestKey } from '../utils/approvalRequest'
import { submitApprovalDecision } from '../utils/submitApprovalDecision'

vi.mock('../utils/submitApprovalDecision', () => ({ submitApprovalDecision: vi.fn(async () => {}) }))

interface EditorSpy { text: string; enabled: boolean; submit: ReturnType<typeof vi.fn> }
function editorSpy(enabled = true): EditorSpy { return { text: '', enabled, submit: vi.fn() } }
const approval: PendingApproval = {
  bridgeId: 'b1', threadId: 't1', turnId: 'turn-1', requestId: 'r1', locallySubmittedDecision: null,
  itemId: 'i1', approvalType: 'shell', operation: 'run', target: 'npm test', reason: ''
}

function Editor({ spy }: { spy: EditorSpy }): JSX.Element {
  const ref = useRef<HTMLDivElement>(null)
  usePetEditorBridge(ref, { getText: () => spy.text, setText: value => { spy.text = value }, submit: spy.submit, enabled: spy.enabled })
  return <div ref={ref} className="rich-input-area" />
}

interface HarnessProps { surface?: 'chat' | 'approval' | 'decision'; editor?: EditorSpy; name?: string; threadId?: string | null }
function Harness({ surface = 'chat', editor, name = 'robot', threadId = 't1' }: HarnessProps): JSX.Element {
  const root = useRef<HTMLDivElement>(null)
  useDesktopPet(root, true, surface, { threadId, mascotName: name })
  return <div ref={root} data-testid="root">
    <div className="composer-mascot-stage" data-testid="stage"><span className="composer-mascot-jelly" data-testid="mascot" /></div>
    {editor && <Editor spy={editor} />}
  </div>
}
// Distinct component types so a rerender replaces the whole subtree in one commit.
function Welcome(props: HarnessProps): JSX.Element { return <Harness {...props} /> }
function Thread(props: HarnessProps): JSX.Element { return <Harness {...props} /> }

const initialConversation = useConversationStore.getState()
function runningTurn(id = 'turn-1'): ConversationTurn {
  return { id, threadId: 't1', status: 'running', items: [], startedAt: '2026-01-01T00:00:00Z' }
}

describe('desktop pet source handoff', () => {
  const originalApi = window.api
  let emit: (event: PetEvent) => void
  const command = vi.fn(async (_command: PetCommand) => {})
  const sent = (type: PetCommand['type']): PetCommand[] =>
    command.mock.calls.map(([value]) => value).filter(value => value.type === type)
  // The detach command carries the first snapshot; later ones travel as snapshot commands.
  const lastSnapshot = (): PetSnapshot => {
    const carriers = command.mock.calls.map(([value]) => value)
      .filter((value): value is Extract<PetCommand, { type: 'snapshot' | 'detach' }> => value.type === 'snapshot' || value.type === 'detach')
    return carriers[carriers.length - 1].snapshot
  }

  beforeEach(() => {
    vi.useFakeTimers()
    command.mockClear()
    vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: false })))
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 300 })
    Object.defineProperty(window, 'innerHeight', { configurable: true, value: 300 })
    window.api = { desktopPet: { command, onEvent: callback => { emit = callback; return () => {} } } } as typeof window.api
  })

  afterEach(() => {
    endPetSource(false)
    useConversationStore.setState(initialConversation, true)
    vi.unstubAllGlobals()
    window.api = originalApi
    document.documentElement.removeAttribute('data-desktop-pet-detached')
  })

  function detachFrom(view: ReturnType<typeof render>): { root: HTMLElement; mascot: HTMLElement; releasePointerCapture: ReturnType<typeof vi.fn> } {
    const root = view.getByTestId('root')
    const mascot = view.getByTestId('mascot')
    const releasePointerCapture = vi.fn()
    Object.assign(root, { setPointerCapture: vi.fn(), hasPointerCapture: vi.fn(() => true), releasePointerCapture })
    mascot.getBoundingClientRect = () => ({ x: 100, y: 100, width: 44, height: 44, top: 100, right: 144, bottom: 144, left: 100, toJSON: () => ({}) })
    fireEvent.pointerDown(mascot, { button: 0, pointerId: 7, clientX: 122, clientY: 122 })
    fireEvent.pointerMove(root, { pointerId: 7, clientX: 285, clientY: 122 })
    expect(command).toHaveBeenCalledWith(expect.objectContaining({ type: 'detach', pointerHeld: true }))
    expect(root).toHaveAttribute('data-pet-owner')
    expect(mascot.style.translate).not.toBe('')
    expect(view.getByTestId('stage').style.position).toBe('fixed')
    act(() => emit({ type: 'ownership', detached: true }))
    return { root, mascot, releasePointerCapture }
  }

  it('releases the source pointer and visual state when handoff recovery arrives', () => {
    const view = render(<Welcome />)
    const { root, mascot, releasePointerCapture } = detachFrom(view)

    act(() => emit({ type: 'ownership', detached: false }))

    expect(releasePointerCapture).toHaveBeenCalledWith(7)
    expect(root).not.toHaveAttribute('data-pet-owner')
    expect(root).not.toHaveAttribute('data-pet-drag')
    expect(mascot.style.translate).toBe('')
    expect(view.getByTestId('stage').style.position).toBe('')
    expect(document.documentElement).not.toHaveAttribute('data-desktop-pet-detached')
  })

  it('hands the companion to the composer that replaces the source instead of returning it', () => {
    const welcome = editorSpy()
    const thread = editorSpy()
    const view = render(<Welcome editor={welcome} threadId={null} />)
    detachFrom(view)
    command.mockClear()

    act(() => { useConversationStore.setState({ turnStatus: 'running', activeTurnId: 'turn-1', turns: [runningTurn()] }) })
    act(() => { view.rerender(<Thread editor={thread} name="thread-bot" />) })

    expect(view.getByTestId('root')).toHaveAttribute('data-pet-owner')
    expect(document.documentElement).toHaveAttribute('data-desktop-pet-detached')
    act(() => { vi.advanceTimersByTime(PET_ADOPTION_GRACE_MS * 2) })
    expect(sent('return')).toHaveLength(0)

    act(() => emit({ type: 'edit', text: 'hello', submit: true, revision: 3 }))
    act(() => { vi.advanceTimersByTime(0) })
    expect(thread.text).toBe('hello')
    expect(thread.submit).toHaveBeenCalledTimes(1)
    expect(welcome.submit).not.toHaveBeenCalled()

    act(() => { vi.advanceTimersByTime(250) })
    expect(lastSnapshot()).toMatchObject({
      editRevision: 3, name: 'thread-bot', activity: 'working', text: 'hello', canChat: true, busy: false, followUpMode: 'steer',
      status: { status: 'running', line: 'Thinking', turnId: 'turn-1', canStop: true }
    })
  })

  it('publishes status changes from the stores as they happen', () => {
    const view = render(<Welcome editor={editorSpy()} />)
    detachFrom(view)
    expect(lastSnapshot().status).toMatchObject({ status: 'idle' })
    command.mockClear()

    act(() => { useConversationStore.setState({ turnStatus: 'running', activeTurnId: 'turn-1', turns: [runningTurn()] }) })
    expect(sent('snapshot')).toHaveLength(0)
    act(() => { vi.advanceTimersByTime(120) })
    expect(sent('snapshot')).toHaveLength(1)
    expect(lastSnapshot()).toMatchObject({ activity: 'working', status: { status: 'running', canStop: true } })

    act(() => { vi.advanceTimersByTime(1000) })
    expect(sent('snapshot')).toHaveLength(1)

    act(() => { useConversationStore.setState({ interruptingTurnId: 'turn-1' }) })
    act(() => { vi.advanceTimersByTime(120) })
    expect(lastSnapshot().status).toMatchObject({ status: 'running', canStop: false, stopping: true })

    act(() => {
      useConversationStore.setState({
        turnStatus: 'idle', activeTurnId: null,
        turns: [{ ...runningTurn(), status: 'completed', items: [{ id: 'm', type: 'agentMessage', status: 'completed', text: 'All done here.', createdAt: '2026-01-01T00:00:01Z' }] }]
      })
    })
    act(() => { vi.advanceTimersByTime(120) })
    expect(lastSnapshot()).toMatchObject({ activity: 'done', followUpMode: 'queue', status: { status: 'review', line: 'All done here', lineTone: 'success' } })
    expect(lastSnapshot().status).not.toHaveProperty('stopping')
  })

  it('returns the companion when no composer takes the seat', () => {
    const view = render(<Welcome editor={editorSpy()} />)
    detachFrom(view)
    command.mockClear()

    view.unmount()

    expect(sent('return')).toHaveLength(0)
    act(() => { vi.advanceTimersByTime(PET_ADOPTION_GRACE_MS) })
    expect(sent('return')).toHaveLength(1)
    act(() => { vi.advanceTimersByTime(PET_ADOPTION_GRACE_MS * 3) })
    expect(sent('return')).toHaveLength(1)
  })

  it('returns immediately when a decision surface replaces the source', () => {
    const view = render(<Welcome editor={editorSpy()} />)
    detachFrom(view)
    command.mockClear()

    act(() => { view.rerender(<Thread surface="decision" />) })

    expect(sent('return')).toHaveLength(1)
    expect(view.getByTestId('root')).not.toHaveAttribute('data-pet-owner')
  })

  it('lets an approval composer keep the companion and relays its decision to the live approval', () => {
    const view = render(<Welcome editor={editorSpy()} />)
    detachFrom(view)
    command.mockClear()
    act(() => { useConversationStore.setState({ turnStatus: 'waitingApproval', pendingApproval: approval, turns: [{ ...runningTurn(), status: 'waitingApproval' }] }) })

    act(() => { view.rerender(<Thread surface="approval" />) })

    act(() => { vi.advanceTimersByTime(PET_ADOPTION_GRACE_MS * 2) })
    expect(sent('return')).toHaveLength(0)
    expect(view.getByTestId('root')).toHaveAttribute('data-pet-owner')
    expect(lastSnapshot()).toMatchObject({ canChat: false, status: { status: 'waiting', decision: { id: approvalRequestKey(approval) } } })

    act(() => emit({ type: 'decision', id: 'tool:other', value: 'accept' }))
    act(() => emit({ type: 'decision', id: approvalRequestKey(approval), value: 'not-an-option' }))
    expect(submitApprovalDecision).not.toHaveBeenCalled()
    act(() => emit({ type: 'decision', id: approvalRequestKey(approval), value: 'accept' }))
    expect(submitApprovalDecision).toHaveBeenCalledWith(approval, 'accept')
  })

  it('survives a remount of the same composer without returning', () => {
    const view = render(<Welcome key="a" editor={editorSpy()} />)
    detachFrom(view)
    command.mockClear()

    act(() => { view.rerender(<Welcome key="b" editor={editorSpy()} />) })
    act(() => { vi.advanceTimersByTime(PET_ADOPTION_GRACE_MS * 2) })

    expect(sent('return')).toHaveLength(0)
    expect(view.getByTestId('root')).toHaveAttribute('data-pet-owner')
  })

  it('keeps the companion out while the source editor is momentarily disabled', () => {
    const editor = editorSpy(false)
    const view = render(<Welcome editor={editor} />)
    detachFrom(view)
    command.mockClear()

    act(() => emit({ type: 'edit', text: 'typed', submit: true, revision: 1 }))
    act(() => { vi.advanceTimersByTime(250) })

    expect(sent('return')).toHaveLength(0)
    expect(editor.text).toBe('typed')
    expect(editor.submit).not.toHaveBeenCalled()
    expect(lastSnapshot()).toMatchObject({ canChat: true, busy: true, editRevision: 1 })
  })
})
