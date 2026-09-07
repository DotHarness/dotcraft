// @vitest-environment jsdom
import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useComposerAvatarBehavior } from '../components/conversation/useComposerAvatarBehavior'

const defaults = {
  semanticPose: 'idle' as const,
  baseExpression: 'neutral' as const,
  focused: false,
  dragOver: false,
  sleeping: false,
  waving: false,
  activeIdle: false,
  bounceSignal: 0,
  reducedMotion: false
}

describe('Composer avatar behavior', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.spyOn(Math, 'random').mockReturnValue(0)
    Object.defineProperty(document, 'hidden', { configurable: true, value: false })
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('keeps focus and drag as expressions without changing idle task state', () => {
    const { result, rerender } = renderHook((props) => useComposerAvatarBehavior(props), {
      initialProps: { ...defaults, focused: true }
    })
    expect(result.current.pose).toBe('idle')
    expect(result.current.expression).toBe('happy')
    rerender({ ...defaults, dragOver: true })
    expect(result.current.pose).toBe('idle')
    expect(result.current.expression).toBe('operator')
  })

  it('replays identical gestures and ignores stale completion callbacks', () => {
    const { result } = renderHook(() => useComposerAvatarBehavior(defaults))
    act(() => vi.advanceTimersByTime(2600))
    expect(result.current.gesture).toBe('blink')
    expect(result.current.gestureSequence).toBe(1)
    act(() => result.current.completeGesture(0))
    expect(result.current.gesture).toBe('blink')
    act(() => result.current.completeGesture(1))
    expect(result.current.gesture).toBeUndefined()
    act(() => vi.advanceTimersByTime(2600))
    expect(result.current.gesture).toBe('blink')
    expect(result.current.gestureSequence).toBe(2)
  })

  it('bounds receipt acknowledgment and never overrides semantic work', () => {
    const { result, rerender } = renderHook((props) => useComposerAvatarBehavior(props), {
      initialProps: defaults
    })
    rerender({ ...defaults, bounceSignal: 1 })
    expect(result.current.pose).toBe('acknowledge')
    act(() => vi.advanceTimersByTime(350))
    expect(result.current.pose).toBe('idle')
    rerender({ ...defaults, semanticPose: 'working', bounceSignal: 2 })
    expect(result.current.pose).toBe('working')
  })

  it('clears an interrupted receipt before returning to idle', () => {
    const { result, rerender } = renderHook((props) => useComposerAvatarBehavior(props), {
      initialProps: defaults
    })
    rerender({ ...defaults, bounceSignal: 1 })
    expect(result.current.pose).toBe('acknowledge')
    rerender({ ...defaults, semanticPose: 'working', bounceSignal: 1 })
    expect(result.current.pose).toBe('working')
    rerender({ ...defaults, bounceSignal: 1 })
    expect(result.current.pose).toBe('idle')
  })

  it.each(['waiting', 'working', 'blocked', 'done'] as const)(
    'leaves the %s expression to the package while focused',
    (semanticPose) => {
      const { result } = renderHook(() => useComposerAvatarBehavior({
        ...defaults,
        semanticPose,
        focused: true,
        dragOver: true
      }))
      expect(result.current.pose).toBe(semanticPose)
      expect(result.current.expression).toBeUndefined()
    }
  )
})
