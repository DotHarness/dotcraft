// @vitest-environment jsdom
import { act, renderHook } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { useVisualizationDesignState } from '../components/conversation/useVisualizationDesignState'

const PATH = '/workspace/.craft/visualizations/thread/timer.html'
const SNAPSHOT = {
  variant: null,
  groups: [{ id: 'tweak-1', label: 'Timer', variant: null, visible: true, controls: [
    { id: 'tweak-1.0', label: 'Digit size', kind: 'slider', min: 32, max: 64, step: 1, initial: 44 },
    { id: 'tweak-1.1', label: 'Dense', kind: 'toggle', initial: false }
  ] }]
}

function view(viewId: string, threadId = 'thread') {
  const post = vi.fn()
  const hook = renderHook(() => useVisualizationDesignState({ threadId, path: PATH, viewId, post }))
  return { post, hook }
}

describe('visualization design values across copies of a view', () => {
  it('applies edits from the inline copy to a fullscreen copy that opens later and back', () => {
    const inline = view('inline')
    act(() => inline.hook.result.current.receive(SNAPSHOT))
    act(() => inline.hook.result.current.change('tweak-1.0', 52))

    const fullscreen = view('fullscreen')
    act(() => fullscreen.hook.result.current.receive(SNAPSHOT))
    expect(fullscreen.post).toHaveBeenCalledWith({ 'tweak-1.0': 52 })
    expect(fullscreen.hook.result.current.values).toEqual({ 'tweak-1.0': 52, 'tweak-1.1': false })

    act(() => fullscreen.hook.result.current.change('tweak-1.1', true))
    expect(inline.post).toHaveBeenLastCalledWith({ 'tweak-1.1': true })
    expect(inline.hook.result.current.values).toEqual({ 'tweak-1.0': 52, 'tweak-1.1': true })

    act(() => fullscreen.hook.result.current.toggleOriginal())
    expect(inline.post).toHaveBeenCalledTimes(2)
    inline.hook.unmount()
    fullscreen.hook.unmount()
  })

  it('keeps values per thread and forgets them once every copy is gone', () => {
    const first = view('first')
    act(() => first.hook.result.current.receive(SNAPSHOT))
    act(() => first.hook.result.current.change('tweak-1.0', 60))
    const other = view('other', 'other-thread')
    act(() => other.hook.result.current.receive(SNAPSHOT))
    expect(other.post).not.toHaveBeenCalled()

    first.hook.unmount()
    const later = view('later')
    act(() => later.hook.result.current.receive(SNAPSHOT))
    expect(later.post).not.toHaveBeenCalled()
  })
})
