import { EventEmitter } from 'events'
import { describe, expect, it, vi } from 'vitest'
import { guardMainWindowNavigation } from '../mainWindowNavigation'

function guardedContents(url: string): { contents: EventEmitter & { getURL: () => string; setWindowOpenHandler: ReturnType<typeof vi.fn> }; navigate: (target: string) => boolean } {
  const contents = Object.assign(new EventEmitter(), { getURL: () => url, setWindowOpenHandler: vi.fn() })
  guardMainWindowNavigation(contents as never)
  return {
    contents,
    navigate: (target) => {
      const event = { preventDefault: vi.fn() }
      contents.emit('will-navigate', event, target)
      return event.preventDefault.mock.calls.length === 0
    }
  }
}

describe('guardMainWindowNavigation', () => {
  it('denies every window the app content requests', () => {
    const { contents } = guardedContents('file:///app/index.html')
    const handler = contents.setWindowOpenHandler.mock.calls[0][0] as (details: { url: string }) => unknown
    expect(handler({ url: 'https://example.com/' })).toEqual({ action: 'deny' })
  })

  it('refuses navigation away from the loaded document but allows fragment changes', () => {
    const { navigate } = guardedContents('file:///app/index.html?workspace=1')
    expect(navigate('https://example.com/')).toBe(false)
    expect(navigate('file:///tmp/page.html')).toBe(false)
    expect(navigate('file:///app/index.html?workspace=1#settings')).toBe(true)
  })
})
