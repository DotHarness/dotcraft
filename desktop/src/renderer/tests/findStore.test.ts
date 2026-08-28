import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { registerFindSurface } from '../find/registry'
import { useFindStore } from '../stores/findStore'
import type { FindSegment } from '../find/types'

function surface(id: string, priority: number, lines: string[]): () => void {
  return registerFindSurface({
    id,
    domain: 'file',
    priority,
    getSegments: (): FindSegment[] =>
      lines.map((text, index) => ({ key: String(index), rowIndex: index, text })),
    getContainer: () => null
  })
}

describe('find store', () => {
  const disposers: (() => void)[] = []

  beforeEach(() => {
    vi.useFakeTimers()
    useFindStore.setState({
      open: false,
      query: '',
      matches: [],
      totalMatches: 0,
      isCapped: false,
      activeIndex: -1
    })
  })

  afterEach(() => {
    while (disposers.length > 0) disposers.pop()?.()
    vi.useRealTimers()
  })

  it('searches every registered surface, highest priority first', () => {
    disposers.push(surface('file:low', 10, ['alpha beta']))
    disposers.push(surface('file:high', 30, ['beta gamma']))

    useFindStore.getState().openFind()
    useFindStore.getState().setQuery('beta')
    useFindStore.getState().searchNow()

    const { matches, totalMatches } = useFindStore.getState()
    expect(totalMatches).toBe(2)
    expect(matches.map((match) => match.surfaceId)).toEqual(['file:high', 'file:low'])
  })

  it('updates the field immediately but defers the search', () => {
    disposers.push(surface('file:a', 10, ['alpha']))
    useFindStore.getState().openFind()

    useFindStore.getState().setQuery('alpha')
    expect(useFindStore.getState().query).toBe('alpha')
    expect(useFindStore.getState().matches).toHaveLength(0)

    vi.advanceTimersByTime(200)
    expect(useFindStore.getState().matches).toHaveLength(1)
  })

  it('collapses a burst of keystrokes into one search', () => {
    const getSegments = vi.fn((): FindSegment[] => [{ key: '0', text: 'alpha' }])
    disposers.push(registerFindSurface({
      id: 'file:a',
      domain: 'file',
      priority: 10,
      getSegments,
      getContainer: () => null
    }))
    useFindStore.getState().openFind()
    getSegments.mockClear()

    for (const query of ['a', 'al', 'alp', 'alph', 'alpha']) {
      useFindStore.getState().setQuery(query)
      vi.advanceTimersByTime(20)
    }
    vi.advanceTimersByTime(200)

    expect(getSegments).toHaveBeenCalledTimes(1)
    expect(useFindStore.getState().matches).toHaveLength(1)
  })

  it('ignores content refreshes while closed', () => {
    const getSegments = vi.fn((): FindSegment[] => [{ key: '0', text: 'alpha' }])
    disposers.push(registerFindSurface({
      id: 'file:a',
      domain: 'file',
      priority: 10,
      getSegments,
      getContainer: () => null
    }))

    useFindStore.getState().refresh()
    vi.advanceTimersByTime(200)

    expect(getSegments).not.toHaveBeenCalled()
  })

  it('wraps around when stepping past either end', () => {
    disposers.push(surface('file:a', 10, ['x x x']))
    useFindStore.getState().openFind()
    useFindStore.getState().setQuery('x')
    useFindStore.getState().searchNow()

    expect(useFindStore.getState().activeIndex).toBe(0)
    useFindStore.getState().goToPrevious()
    expect(useFindStore.getState().activeIndex).toBe(2)
    useFindStore.getState().goToNext()
    expect(useFindStore.getState().activeIndex).toBe(0)
  })

  it('stays on the same match when the content around it changes', () => {
    disposers.push(surface('file:a', 10, ['alpha', 'beta alpha']))
    useFindStore.getState().openFind()
    useFindStore.getState().setQuery('alpha')
    useFindStore.getState().searchNow()
    useFindStore.getState().goToNext()

    const before = useFindStore.getState().matches[useFindStore.getState().activeIndex]
    useFindStore.getState().refresh()
    vi.advanceTimersByTime(200)

    const after = useFindStore.getState().matches[useFindStore.getState().activeIndex]
    expect(after?.id).toBe(before?.id)
  })

  it('drops results when closed', () => {
    disposers.push(surface('file:a', 10, ['alpha']))
    useFindStore.getState().openFind()
    useFindStore.getState().setQuery('alpha')
    useFindStore.getState().searchNow()
    expect(useFindStore.getState().matches).toHaveLength(1)

    useFindStore.getState().closeFind()
    expect(useFindStore.getState().open).toBe(false)
    expect(useFindStore.getState().matches).toHaveLength(0)
  })
})
