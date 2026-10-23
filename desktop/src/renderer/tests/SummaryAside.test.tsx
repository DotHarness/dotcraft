import type { DesktopPluginConversationAsideContext } from '@dotcraft/plugin'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { ReactElement } from 'react'
import { SummaryAside } from '../components/conversation/summary/SummaryAside'
import { hydrateSummaryPinned, resetView } from '../components/conversation/summary/view'
import { LocaleProvider } from '../contexts/LocaleContext'

const renderWithLocale = (ui: ReactElement) => render(ui, {
  wrapper: ({ children }) => <LocaleProvider loadSettings={false}>{children}</LocaleProvider>
})

vi.mock('../components/conversation/summary/SummaryPanel', () => ({
  SummaryPanel: ({ threadId }: { threadId: string }) => <button data-testid="summary-content" data-thread-id={threadId} />
}))

beforeEach(() => {
  vi.useFakeTimers()
  resetView()
  hydrateSummaryPinned(true)
})

function setup(layout: DesktopPluginConversationAsideContext['layout'] = 'shift') {
  const release = vi.fn()
  const pin = vi.fn(() => release)
  const context: DesktopPluginConversationAsideContext = {
    threadId: 'thread-1', workspacePath: null, busy: false, layout, width: 353, pin
  }
  const view = renderWithLocale(<SummaryAside context={context} />)
  const change = (patch: Partial<DesktopPluginConversationAsideContext>) => {
    Object.assign(context, patch)
    view.rerender(<SummaryAside context={{ ...context }} />)
  }
  return { ...view, pin, release, change }
}

function finishExit(element: HTMLElement): void {
  fireEvent.transitionEnd(element, { propertyName: 'opacity' })
}

describe('Summary aside presence', () => {
  it('releases its layout pin immediately and keeps an inaccessible exit until completion', () => {
    const { release, change } = setup()
    const aside = screen.getByRole('complementary')
    change({ layout: 'overlay' })

    expect(release).toHaveBeenCalledTimes(1)
    expect(screen.queryByRole('complementary')).toBeNull()
    expect(aside).toHaveAttribute('inert')
    expect(aside).toBeInTheDocument()

    finishExit(aside)
    expect(aside).not.toBeInTheDocument()
  })

  it('reverses an interrupted exit without remounting the card or letting its old timer remove it', () => {
    const { change, pin } = setup()
    const content = screen.getByTestId('summary-content')
    change({ layout: 'overlay' })
    act(() => vi.advanceTimersByTime(100))
    change({ layout: 'shift' })
    act(() => vi.advanceTimersByTime(1000))

    expect(screen.getByTestId('summary-content')).toBe(content)
    expect(screen.getByRole('complementary')).not.toHaveAttribute('inert')
    expect(pin).toHaveBeenCalledTimes(2)
  })

  it('does not let child transition events end the card exit', () => {
    const { change } = setup()
    change({ layout: 'overlay' })
    finishExit(screen.getByTestId('summary-content'))
    expect(screen.getByTestId('summary-content')).toBeInTheDocument()
    act(() => vi.advanceTimersByTime(240))
    expect(screen.queryByTestId('summary-content')).toBeNull()
  })

  it('does not render a pinned card when mounted in overlay mode', () => {
    const { pin } = setup('overlay')
    expect(screen.queryByTestId('summary-content')).toBeNull()
    expect(pin).not.toHaveBeenCalled()
  })

  it('never shows an outgoing card in another thread', () => {
    const { change } = setup()
    change({ layout: 'overlay' })
    change({ threadId: 'thread-2' })
    expect(screen.queryByTestId('summary-content')).toBeNull()
    change({ layout: 'shift' })
    expect(screen.getByTestId('summary-content')).toHaveAttribute('data-thread-id', 'thread-2')
  })
})
