import { act, fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ResponseFeedback } from '../components/conversation/ResponseFeedback'
import { AgentMessage } from '../components/conversation/AgentMessage'
import { useResponseSelectionStore } from '../components/conversation/responseSelectionStore'
import { useComposerContextStore } from '../stores/composerContextStore'
import { useThreadStore } from '../stores/threadStore'
import { useVoiceStore } from '../voice/voiceStore'
import { installDesktopApiMock } from './desktopApiMock'

const showMenu = vi.fn(async () => {})
beforeEach(() => {
  showMenu.mockClear()
  Range.prototype.getBoundingClientRect = vi.fn(() => ({ left: 40, top: 80, right: 240, bottom: 104, width: 200, height: 24 }) as DOMRect)
  useResponseSelectionStore.setState({ active: null, drafts: {} })
  useComposerContextStore.setState({ byThread: {} })
  useThreadStore.setState({ activeThreadId: 'task' })
  useVoiceStore.setState({ initialized: true, recording: null, finalizing: null,
    snapshot: { model: { phase: 'missing', bytesDownloaded: 0, bytesTotal: null }, chatGpt: { signedIn: false, enabled: true }, sessions: [], capacity: 2 } })
  installDesktopApiMock({ settings: { get: async () => ({ locale: 'en' }) }, shell: { showReplyTextMenu: showMenu } })
})

function replies() {
  return render(<LocaleProvider>
    <ResponseFeedback threadId="task" turnId="turn" itemId="first"><p>First reply text.</p></ResponseFeedback>
    <ResponseFeedback threadId="task" turnId="turn" itemId="second"><p>Second reply text.</p></ResponseFeedback>
    <button>Outside</button>
  </LocaleProvider>)
}

function select(text = 'First reply text.') {
  const element = screen.getByText(text)
  fireEvent.pointerDown(element)
  const range = document.createRange()
  range.selectNodeContents(element)
  window.getSelection()!.removeAllRanges()
  window.getSelection()!.addRange(range)
  fireEvent.mouseUp(element, { button: 0 })
}

it('replaces the previous reply selection and annotates the correct source', () => {
  replies()
  select()
  select('Second reply text.')
  expect(screen.getAllByRole('toolbar')).toHaveLength(1)
  fireEvent.click(screen.getByRole('button', { name: 'Add to chat' }))
  expect(useComposerContextStore.getState().getContexts('task')).toEqual([
    expect.objectContaining({ itemId: 'second', selectedText: 'Second reply text.', comment: '' }),
  ])
  expect(screen.queryByRole('toolbar')).toBeNull()
})

it('dismisses on outside left pointer-down, Escape, selection collapse, task switch and unmount', () => {
  const view = replies()
  select()
  fireEvent.pointerDown(screen.getByRole('button', { name: 'Outside' }), { button: 0 })
  expect(screen.queryByRole('toolbar')).toBeNull()
  select()
  fireEvent.keyDown(document, { key: 'Escape' })
  expect(screen.queryByRole('toolbar')).toBeNull()
  select()
  act(() => { window.getSelection()!.removeAllRanges(); document.dispatchEvent(new Event('selectionchange')) })
  expect(screen.queryByRole('toolbar')).toBeNull()
  select()
  act(() => useThreadStore.setState({ activeThreadId: 'other' }))
  expect(screen.queryByRole('toolbar')).toBeNull()
  select()
  view.unmount()
  expect(useResponseSelectionStore.getState().active).toBeNull()
})

it('retains drafts after passive dismissal and preserves the source while the editor has focus', () => {
  replies()
  select()
  fireEvent.click(screen.getByRole('button', { name: 'Comment' }))
  fireEvent.change(screen.getByRole('textbox', { name: 'Your comment' }), { target: { value: 'Keep this' } })
  act(() => { window.getSelection()!.removeAllRanges(); document.dispatchEvent(new Event('selectionchange')) })
  expect(screen.getByRole('textbox')).toHaveValue('Keep this')
  fireEvent.contextMenu(screen.getByRole('textbox'))
  expect(screen.getByRole('textbox')).toHaveValue('Keep this')
  fireEvent.pointerDown(screen.getByRole('button', { name: 'Outside' }))
  select('Second reply text.')
  fireEvent.click(screen.getByRole('button', { name: 'Comment' }))
  expect(screen.getByRole('textbox')).toHaveValue('')
  select()
  fireEvent.click(screen.getByRole('button', { name: 'Comment' }))
  expect(screen.getByRole('textbox')).toHaveValue('Keep this')
  fireEvent.click(screen.getByRole('button', { name: 'Save' }))
  expect(useComposerContextStore.getState().getContexts('task')[0]).toMatchObject({ itemId: 'first', comment: 'Keep this' })
  expect(useResponseSelectionStore.getState().drafts).toEqual({})
})

it('clears a draft on explicit editor cancellation', () => {
  replies()
  select()
  fireEvent.click(screen.getByRole('button', { name: 'Comment' }))
  fireEvent.change(screen.getByRole('textbox'), { target: { value: 'Discard' } })
  fireEvent.keyDown(screen.getByRole('textbox'), { key: 'Escape' })
  select()
  fireEvent.click(screen.getByRole('button', { name: 'Comment' }))
  expect(screen.getByRole('textbox')).toHaveValue('')
})

it('does not create a single-message annotation from a cross-message selection', () => {
  replies()
  const range = document.createRange()
  range.setStart(screen.getByText('First reply text.').firstChild!, 0)
  range.setEnd(screen.getByText('Second reply text.').firstChild!, 6)
  window.getSelection()!.removeAllRanges()
  window.getSelection()!.addRange(range)
  fireEvent.mouseUp(screen.getByText('Second reply text.'))
  expect(screen.queryByRole('toolbar')).toBeNull()
})

it('hands selection to the native menu, dismisses the toolbar and does not reopen it on close', async () => {
  render(<LocaleProvider><AgentMessage text="First reply text." threadId="task" turnId="turn" itemId="first" /></LocaleProvider>)
  select()
  await act(async () => fireEvent.contextMenu(screen.getByText('First reply text.'), { clientX: 12, clientY: 24 }))
  expect(showMenu).toHaveBeenCalledWith({ x: 12, y: 24, selectionText: 'First reply text.' })
  expect(window.getSelection()!.toString()).toBe('First reply text.')
  expect(screen.queryByRole('toolbar')).toBeNull()
  fireEvent.mouseUp(screen.getByText('First reply text.'), { button: 2 })
  expect(screen.queryByRole('toolbar')).toBeNull()
})

it('leaves editable and specialized context menus alone and passes an empty selection explicitly', async () => {
  render(<LocaleProvider><AgentMessage text="Reply text." afterContent={<>
    <textarea aria-label="Editor" />
    <span onContextMenu={(event) => event.preventDefault()}>Specialized reference</span>
  </>} /></LocaleProvider>)
  fireEvent.contextMenu(screen.getByRole('textbox', { name: 'Editor' }))
  fireEvent.contextMenu(screen.getByText('Specialized reference'))
  expect(showMenu).not.toHaveBeenCalled()
  window.getSelection()!.removeAllRanges()
  await act(async () => fireEvent.contextMenu(screen.getByText('Reply text.')))
  expect(showMenu).toHaveBeenCalledWith({ x: 0, y: 0, selectionText: '' })
})
