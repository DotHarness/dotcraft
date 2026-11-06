import { act, fireEvent, render, renderHook, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { useComposerSendScroll, useScrollToLatestOnSend, type ComposerSendScrollRequest } from '../hooks/useComposerSendScroll'
import { useAutoScroll } from '../hooks/useAutoScroll'

const initialScope = { workspaceKey: 'project-a', threadId: 'thread-a', enabled: true }

function useSendScroll(scope: typeof initialScope, scrollToBottom: () => void): ReturnType<typeof useComposerSendScroll> {
  const control = useComposerSendScroll(scope.workspaceKey, scope.threadId, scope.enabled)
  useScrollToLatestOnSend(control.scrollRequest, scrollToBottom)
  return control
}

function Receiver({ request, scroll }: { request: ComposerSendScrollRequest | null; scroll: () => void }): null {
  useScrollToLatestOnSend(request, scroll)
  return null
}

function ToggleTranscript({ visible, scroll }: { visible: boolean; scroll: () => void }): JSX.Element {
  const { scrollRequest, onOptimisticEcho } = useComposerSendScroll('project-a', 'thread-a', true)
  return <>
    <button onClick={onOptimisticEcho}>Send</button>
    {visible && <Receiver request={scrollRequest} scroll={scroll} />}
  </>
}

function FollowingTranscript({ contentLength = 1 }: { contentLength?: number }): JSX.Element {
  const { scrollRequest, onOptimisticEcho } = useComposerSendScroll('project-a', 'thread-a', true)
  const { scrollRef, showScrollButton, scrollToBottom } = useAutoScroll(contentLength)
  useScrollToLatestOnSend(scrollRequest, scrollToBottom)
  return <>
    <div ref={scrollRef} data-testid="transcript"><div>Messages</div></div>
    <button onClick={onOptimisticEcho}>Send</button>
    {showScrollButton && <button onClick={scrollToBottom}>Latest</button>}
  </>
}

describe('composer send positioning', () => {
  it('positions once per send, not on ordinary renders', () => {
    const scroll = vi.fn()
    const { result, rerender } = renderHook(() => useSendScroll(initialScope, scroll))
    expect(scroll).not.toHaveBeenCalled()

    act(() => result.current.onOptimisticEcho())
    rerender()
    expect(scroll).toHaveBeenCalledTimes(1)

    act(() => result.current.onOptimisticEcho())
    expect(scroll).toHaveBeenCalledTimes(2)
  })

  it.each([
    { ...initialScope, threadId: 'thread-b' },
    { ...initialScope, workspaceKey: 'project-b' },
    { ...initialScope, enabled: false }
  ])('discards a departed scope and its late callbacks: %o', (otherScope) => {
    const scroll = vi.fn()
    const { result, rerender } = renderHook((scope) => useSendScroll(scope, scroll), { initialProps: initialScope })
    const originalSend = result.current.onOptimisticEcho
    act(() => originalSend())
    expect(scroll).toHaveBeenCalledTimes(1)

    rerender(otherScope)
    act(() => originalSend())
    expect(scroll).toHaveBeenCalledTimes(1)
    expect(result.current.scrollRequest).toBeNull()

    rerender(initialScope)
    act(() => originalSend())
    expect(scroll).toHaveBeenCalledTimes(1)
    expect(result.current.scrollRequest).toBeNull()
    act(() => result.current.onOptimisticEcho())
    expect(scroll).toHaveBeenCalledTimes(2)
  })

  it('does not replay a send when the transcript mounts or remounts', () => {
    const scroll = vi.fn()
    const { rerender } = render(<ToggleTranscript visible={false} scroll={scroll} />)
    fireEvent.click(screen.getByRole('button', { name: 'Send' }))
    rerender(<ToggleTranscript visible scroll={scroll} />)
    expect(scroll).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: 'Send' }))
    expect(scroll).toHaveBeenCalledTimes(1)
    rerender(<ToggleTranscript visible={false} scroll={scroll} />)
    rerender(<ToggleTranscript visible scroll={scroll} />)
    expect(scroll).toHaveBeenCalledTimes(1)
  })

  it('resumes following on send but lets the very next upward scroll stop following', () => {
    const { rerender } = render(<FollowingTranscript />)
    const transcript = screen.getByTestId('transcript')
    Object.defineProperty(transcript, 'clientHeight', { configurable: true, value: 100 })
    Object.defineProperty(transcript, 'scrollHeight', { configurable: true, value: 500 })
    transcript.scrollTop = 100
    fireEvent.scroll(transcript)
    expect(screen.getByRole('button', { name: 'Latest' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Send' }))
    expect(screen.queryByRole('button', { name: 'Latest' })).toBeNull()

    transcript.scrollTop = 200
    fireEvent.scroll(transcript)
    expect(screen.getByRole('button', { name: 'Latest' })).toBeInTheDocument()

    rerender(<FollowingTranscript contentLength={2} />)
    expect(screen.getByRole('button', { name: 'Latest' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Latest' }))
    expect(screen.queryByRole('button', { name: 'Latest' })).toBeNull()
  })
})
