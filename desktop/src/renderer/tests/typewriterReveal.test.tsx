import { act, cleanup, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useTypewriterReveal } from '../hooks/useTypewriterReveal'

let frames: Map<number, FrameRequestCallback>

function publishFrame(): void {
  act(() => {
    const pending = [...frames.values()]
    frames.clear()
    for (const frame of pending) frame(performance.now())
  })
}

describe('streaming text reveal', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'performance'] })
    vi.spyOn(document, 'hidden', 'get').mockReturnValue(false)
    frames = new Map()
    let frameId = 0
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
      frames.set(++frameId, callback)
      return frameId
    })
    vi.stubGlobal('cancelAnimationFrame', (id: number) => frames.delete(id))
  })

  afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('publishes the latest backlog on the first frame after animation frames resume', () => {
    const { result, rerender } = renderHook(
      ({ text }) => useTypewriterReveal(text, true),
      { initialProps: { text: '' } }
    )
    let text = ''
    for (let batch = 0; batch < 60; batch++) {
      text += 'a'.repeat(1000)
      rerender({ text })
      act(() => vi.advanceTimersByTime(1000))
    }

    expect(result.current).toBe('')
    publishFrame()
    expect(result.current.length).toBeGreaterThan(text.length - 1000)

    rerender({ text: text + 'more' })
    act(() => vi.runAllTimers())
    publishFrame()
    expect(result.current).toBe(text + 'more')
  })

  it('accounts for elapsed time when both timers and animation frames were suspended', () => {
    let now = 0
    vi.spyOn(performance, 'now').mockImplementation(() => now)
    const { result, rerender } = renderHook(
      ({ text }) => useTypewriterReveal(text, true),
      { initialProps: { text: '' } }
    )
    const text = 'a'.repeat(60_000)
    rerender({ text })
    now = 60_000

    act(() => vi.advanceTimersByTime(50))
    publishFrame()
    expect(result.current).toBe(text)
  })

  it('shows completed text immediately and discards pending animation work', () => {
    const { result, rerender, unmount } = renderHook(
      ({ text, streaming }) => useTypewriterReveal(text, streaming),
      { initialProps: { text: '', streaming: true } }
    )
    const text = 'a'.repeat(60_000)
    rerender({ text, streaming: true })
    act(() => vi.advanceTimersByTime(50))
    rerender({ text, streaming: false })

    expect(result.current).toBe(text)
    expect(frames.size).toBe(0)
    expect(vi.getTimerCount()).toBe(0)

    rerender({ text, streaming: true })
    rerender({ text: text + 'more', streaming: true })
    act(() => vi.advanceTimersByTime(50))
    unmount()
    expect(frames.size).toBe(0)
    expect(vi.getTimerCount()).toBe(0)
  })

  it('replaces a stream without stale text and never reveals half a surrogate pair', () => {
    const { result, rerender } = renderHook(
      ({ text }) => useTypewriterReveal(text, true),
      { initialProps: { text: 'old' } }
    )
    rerender({ text: 'old pending' })
    act(() => vi.advanceTimersByTime(50))
    rerender({ text: 'new' })
    expect(result.current).toBe('new')
    publishFrame()
    expect(result.current).toBe('new')

    const text = 'new' + '😀'.repeat(100)
    rerender({ text })
    for (let tick = 0; tick < 30; tick++) {
      act(() => vi.advanceTimersByTime(50))
      publishFrame()
      expect(result.current).toMatch(/^new(?:😀)*$/u)
    }
    act(() => vi.runAllTimers())
    publishFrame()
    expect(result.current).toBe(text)
  })
})
