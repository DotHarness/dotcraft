import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { TurnWorkLoader, type TurnItemsExpansionLoader } from '../components/conversation/TurnWorkLoader'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useTurnItemsLoadStore } from '../stores/turnItemsLoadStore'

const THREAD = 'thread-loader'
const TURN = 'turn-loader'
let intersect: IntersectionObserverCallback

function loader(loadItems: TurnItemsExpansionLoader, enabled: boolean, complete = false, cursor?: string) {
  return <LocaleProvider loadSettings={false}>
    <TurnWorkLoader threadId={THREAD} turnId={TURN} enabled={enabled} complete={complete} cursor={cursor} loadItems={loadItems} />
  </LocaleProvider>
}

beforeEach(() => {
  useTurnItemsLoadStore.setState({ statuses: {} })
  vi.stubGlobal('IntersectionObserver', class {
    constructor(callback: IntersectionObserverCallback) { intersect = callback }
    observe() {}
    disconnect() {}
  })
})

afterEach(() => vi.unstubAllGlobals())

describe('turn work loading demand', () => {
  it('uses a bounded child page automatically and complete loading only for explicit expansion', () => {
    const load = vi.fn<TurnItemsExpansionLoader>().mockResolvedValue(undefined)
    const view = render(loader(load, false))
    expect(load).not.toHaveBeenCalled()
    expect(screen.queryByRole('status')).toBeNull()

    view.rerender(loader(load, true))
    expect(load).toHaveBeenCalledWith(THREAD, TURN, expect.any(Function), false)
    expect(load.mock.calls[0][2]()).toBe(true)
    expect(screen.queryByRole('status')).toBeNull()

    view.rerender(loader(load, true, false, 'remaining'))
    expect(load).toHaveBeenCalledTimes(1)
    act(() => intersect([{ isIntersecting: true }] as IntersectionObserverEntry[], {} as IntersectionObserver))
    expect(load).toHaveBeenCalledTimes(2)
    expect(load.mock.calls[1][3]).toBe(false)

    const previousDemand = load.mock.calls[1][2]
    view.rerender(loader(load, false, false, 'remaining'))
    expect(previousDemand()).toBe(false)
    act(() => intersect([{ isIntersecting: true }] as IntersectionObserverEntry[], {} as IntersectionObserver))
    expect(load).toHaveBeenCalledTimes(2)

    view.rerender(loader(load, true, true, 'remaining'))
    expect(load).toHaveBeenCalledTimes(3)
    expect(load.mock.calls[2][3]).toBe(true)
  })

  it('exposes request state and retries with the same loading mode', () => {
    const load = vi.fn<TurnItemsExpansionLoader>().mockImplementation(async () => {
      useTurnItemsLoadStore.setState({ statuses: { [TURN]: 'loading' } })
    })
    render(loader(load, true))
    expect(screen.getByRole('status')).toBeInTheDocument()
    act(() => useTurnItemsLoadStore.setState({ statuses: { [TURN]: 'failed' } }))
    expect(screen.queryByRole('status')).toBeNull()
    expect(screen.getByRole('alert')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button'))
    expect(load).toHaveBeenCalledTimes(2)
    expect(load.mock.calls[1][3]).toBe(false)
    expect(screen.getByRole('status')).toBeInTheDocument()
    act(() => useTurnItemsLoadStore.setState({ statuses: {} }))
    expect(screen.queryByRole('status')).toBeNull()
  })
})
