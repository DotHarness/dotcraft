// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { FindOverlay } from '../find/FindOverlay'
import { getFindSurface, registerFindSurface } from '../find/registry'
import { useFindSurface } from '../find/useFindSurface'
import { useFindStore } from '../stores/findStore'
import { installDesktopApiMock } from './desktopApiMock'

describe('FindOverlay', () => {
  let unregister: (() => void) | undefined

  beforeEach(() => {
    installDesktopApiMock({
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) }
    })
    useFindStore.setState({
      open: false,
      query: 'target',
      matches: [],
      totalMatches: 0,
      isCapped: false,
      activeIndex: -1,
      revision: 0
    })
    unregister = registerFindSurface({
      id: 'conversation:test',
      domain: 'conversation',
      priority: 10,
      getSegments: () => [{ key: '0', text: 'target before target after' }],
      getContainer: () => null
    })
    useFindStore.getState().openFind()
  })

  afterEach(() => {
    cleanup()
    unregister?.()
    useFindStore.getState().closeFind()
  })

  it('uses the shared bare field inside the framed search overlay', () => {
    render(<LocaleProvider><FindOverlay /></LocaleProvider>)

    const input = screen.getByRole('search').querySelector('input')
    expect(input).not.toBeNull()
    expect(input).toHaveClass('dc-field')
    expect(input).toHaveAttribute('data-bare')
  })

  it('keeps Enter and Shift+Enter result navigation', () => {
    render(<LocaleProvider><FindOverlay /></LocaleProvider>)
    const input = screen.getByRole('search').querySelector('input')
    expect(input).not.toBeNull()

    fireEvent.keyDown(input!, { key: 'Enter' })
    expect(useFindStore.getState().activeIndex).toBe(1)

    fireEvent.keyDown(input!, { key: 'Enter', shiftKey: true })
    expect(useFindStore.getState().activeIndex).toBe(0)
  })

  it('does not register a no-op reveal callback for non-virtualized surfaces', () => {
    const { rerender } = render(<FindSurfaceFixture />)

    expect(getFindSurface('conversation:fixture')?.reveal).toBeUndefined()

    rerender(<FindSurfaceFixture reveal={() => undefined} />)
    expect(getFindSurface('conversation:fixture')?.reveal).toBeTypeOf('function')
  })
})

function FindSurfaceFixture({ reveal }: { reveal?: () => void }): null {
  useFindSurface({
    id: 'conversation:fixture',
    domain: 'conversation',
    priority: 10,
    getSegments: () => [],
    getContainer: () => null,
    reveal
  })
  return null
}
