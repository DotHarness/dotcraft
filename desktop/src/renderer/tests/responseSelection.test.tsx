import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ResponseFeedback } from '../components/conversation/ResponseFeedback'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useComposerContextStore } from '../stores/composerContextStore'
import { useThreadStore } from '../stores/threadStore'

const pointer = { pointerId: 1, button: 0, isPrimary: true, pointerType: 'mouse' }

function renderReplies() {
  render(
    <LocaleProvider loadSettings={false}>
      <div data-testid="message-wrapper">
        <ResponseFeedback threadId="thread" turnId="turn" itemId="finished">
          <p>First line of a completed reply.</p>
          <p>Second line of the same reply.</p>
        </ResponseFeedback>
        <div data-testid="reply-footer">Footer outside reply</div>
      </div>
      <ResponseFeedback threadId="thread" turnId="turn" itemId="second">
        <p>Another completed reply.</p>
      </ResponseFeedback>
      <button>Outside the replies</button>
      <textarea aria-label="Chat composer" />
    </LocaleProvider>,
  )
  return {
    firstLine: screen.getByText(/First line of a completed reply/),
    secondLine: screen.getByText(/Second line of the same reply/),
    footer: screen.getByTestId('reply-footer'),
    secondReply: screen.getByText(/Another completed reply/),
    outside: screen.getByRole('button', { name: 'Outside the replies' }),
  }
}

async function nextFrame(): Promise<void> {
  await act(async () => { await new Promise(window.requestAnimationFrame) })
}

beforeEach(() => {
  Range.prototype.getBoundingClientRect = () => new DOMRect(20, 100, 80, 20)
  Range.prototype.getClientRects = () => [new DOMRect(20, 100, 80, 20)] as unknown as DOMRectList
})

afterEach(() => {
  window.getSelection()?.removeAllRanges()
  useComposerContextStore.getState().clearContexts('thread')
  useThreadStore.setState({ activeThreadId: null })
  vi.unstubAllGlobals()
})

describe('reply text actions', () => {
  it('opens for a backward selection released outside the reply', async () => {
    const { firstLine, outside } = renderReplies()
    const text = firstLine.firstChild!
    fireEvent.pointerDown(firstLine, pointer)
    window.getSelection()!.setBaseAndExtent(text, 17, text, 2)
    fireEvent.pointerUp(outside, pointer)
    await nextFrame()
    expect(screen.getByRole('toolbar')).toBeVisible()
  })

  it('does not remount the menu when a click releases before the old selection clears', async () => {
    const { firstLine } = renderReplies()
    fireEvent.pointerDown(firstLine, pointer)
    window.getSelection()!.setBaseAndExtent(firstLine.firstChild!, 2, firstLine.firstChild!, 17)
    fireEvent.pointerUp(firstLine, pointer)
    await nextFrame()
    expect(screen.getByRole('toolbar')).toBeVisible()

    fireEvent.pointerDown(firstLine, pointer)
    expect(screen.queryByRole('toolbar')).not.toBeInTheDocument()
    fireEvent.pointerUp(firstLine, pointer)
    await nextFrame()
    expect(screen.queryByRole('toolbar')).not.toBeInTheDocument()
  })

  it('adds a reference and focuses the active chat composer', async () => {
    useThreadStore.setState({ activeThreadId: 'thread' })
    const { firstLine } = renderReplies()
    const composer = screen.getByRole('textbox', { name: 'Chat composer' })
    vi.stubGlobal('__inputComposerFocus', () => composer.focus())

    fireEvent.pointerDown(firstLine, pointer)
    window.getSelection()!.setBaseAndExtent(firstLine.firstChild!, 0, firstLine.firstChild!, 10)
    fireEvent.pointerUp(firstLine, pointer)
    await nextFrame()
    fireEvent.click(screen.getByRole('button', { name: 'Add to chat' }))

    expect(useComposerContextStore.getState().getContexts('thread')).toMatchObject([
      { kind: 'responseAnnotation', selectedText: 'First line', comment: '' },
    ])
    expect(composer).toHaveFocus()
    expect(screen.queryByRole('toolbar')).not.toBeInTheDocument()
  })

  it('clamps a multi-line selection when dragging past the reply boundary into the footer', async () => {
    const { firstLine, footer } = renderReplies()
    fireEvent.pointerDown(firstLine, pointer)
    window.getSelection()!.setBaseAndExtent(firstLine.firstChild!, 6, footer.firstChild!, 6)
    fireEvent.pointerUp(footer, pointer)
    await nextFrame()
    expect(screen.getByRole('toolbar')).toBeVisible()

    fireEvent.click(screen.getByRole('button', { name: 'Add to chat' }))
    expect(useComposerContextStore.getState().getContexts('thread')).toMatchObject([
      {
        kind: 'responseAnnotation',
        itemId: 'finished',
        selectedText: 'line of a completed reply.Second line of the same reply.',
      },
    ])
  })

  it('resolves a selection whose end lands on a parent element boundary', async () => {
    const { secondLine } = renderReplies()
    const wrapper = screen.getByTestId('message-wrapper')
    fireEvent.pointerDown(secondLine, pointer)
    window.getSelection()!.setBaseAndExtent(secondLine.firstChild!, 0, wrapper, 1)
    fireEvent.pointerUp(wrapper, pointer)
    await nextFrame()
    expect(screen.getByRole('toolbar')).toBeVisible()

    fireEvent.click(screen.getByRole('button', { name: 'Add to chat' }))
    expect(useComposerContextStore.getState().getContexts('thread')).toMatchObject([
      {
        kind: 'responseAnnotation',
        itemId: 'finished',
        selectedText: 'Second line of the same reply.',
      },
    ])
  })

  it('does not open when a selection spans two different replies', async () => {
    const { secondLine, secondReply } = renderReplies()
    fireEvent.pointerDown(secondLine, pointer)
    window.getSelection()!.setBaseAndExtent(secondLine.firstChild!, 0, secondReply.firstChild!, 7)
    fireEvent.pointerUp(secondReply, pointer)
    await nextFrame()
    expect(screen.queryByRole('toolbar')).not.toBeInTheDocument()
  })
})
