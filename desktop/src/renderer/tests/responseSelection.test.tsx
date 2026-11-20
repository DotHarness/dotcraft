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
      <ResponseFeedback threadId="thread" turnId="turn" itemId="finished">
        <p>A completed reply with selectable text.</p>
      </ResponseFeedback>
      <button>Outside the replies</button>
      <textarea aria-label="Chat composer" />
    </LocaleProvider>
  )
  return {
    reply: screen.getByText(/A completed reply/),
    outside: screen.getByRole('button', { name: 'Outside the replies' }),
  }
}

async function nextFrame(): Promise<void> {
  await act(async () => { await new Promise(window.requestAnimationFrame) })
}

beforeEach(() => {
  Range.prototype.getBoundingClientRect = () => new DOMRect(20, 100, 80, 20)
})

afterEach(() => {
  window.getSelection()?.removeAllRanges()
  useComposerContextStore.getState().clearContexts('thread')
  useThreadStore.setState({ activeThreadId: null })
  vi.unstubAllGlobals()
})

describe('reply text actions', () => {
  it('opens for a backward selection released outside the reply', async () => {
    const { reply, outside } = renderReplies()
    const text = reply.firstChild!
    fireEvent.pointerDown(reply, pointer)
    window.getSelection()!.setBaseAndExtent(text, 17, text, 2)
    fireEvent.pointerUp(outside, pointer)
    await nextFrame()
    expect(screen.getByRole('toolbar')).toBeVisible()
  })

  it('does not remount the menu when a click releases before the old selection clears', async () => {
    const { reply } = renderReplies()
    fireEvent.pointerDown(reply, pointer)
    window.getSelection()!.setBaseAndExtent(reply.firstChild!, 2, reply.firstChild!, 17)
    fireEvent.pointerUp(reply, pointer)
    await nextFrame()
    expect(screen.getByRole('toolbar')).toBeVisible()

    fireEvent.pointerDown(reply, pointer)
    expect(screen.queryByRole('toolbar')).not.toBeInTheDocument()
    fireEvent.pointerUp(reply, pointer)
    await nextFrame()
    expect(screen.queryByRole('toolbar')).not.toBeInTheDocument()
  })

  it('adds a reference and focuses the active chat composer', async () => {
    useThreadStore.setState({ activeThreadId: 'thread' })
    const { reply } = renderReplies()
    const composer = screen.getByRole('textbox', { name: 'Chat composer' })
    vi.stubGlobal('__inputComposerFocus', () => composer.focus())

    fireEvent.pointerDown(reply, pointer)
    window.getSelection()!.setBaseAndExtent(reply.firstChild!, 2, reply.firstChild!, 17)
    fireEvent.pointerUp(reply, pointer)
    await nextFrame()
    fireEvent.click(screen.getByRole('button', { name: 'Add to chat' }))

    expect(useComposerContextStore.getState().getContexts('thread')).toMatchObject([
      { kind: 'responseAnnotation', selectedText: 'completed reply', comment: '' },
    ])
    expect(composer).toHaveFocus()
    expect(screen.queryByRole('toolbar')).not.toBeInTheDocument()
  })
})
