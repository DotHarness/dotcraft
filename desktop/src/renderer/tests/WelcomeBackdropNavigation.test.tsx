import './setupPluginRuntime'
import type { Ref } from 'react'
import { act, render } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { startWelcomeBackdrop } from '../components/conversation/welcomeBackdrop/backdropEngine'
import { ConversationPanel } from '../components/layout/ConversationPanel'
import { MainViewOutlet, RETAIN_HIDDEN_VIEW_MS } from '../components/layout/MainViewOutlet'
import { LocaleProvider } from '../contexts/LocaleContext'
import { installDesktopApiMock } from './desktopApiMock'

vi.mock('../components/conversation/ConversationWelcome', () => ({
  ConversationWelcome: ({ welcomeContentRef }: { welcomeContentRef?: Ref<HTMLDivElement> }) => (
    <div ref={welcomeContentRef} />
  )
}))
vi.mock('../components/conversation/welcomeBackdrop/backdropEngine', () => ({
  startWelcomeBackdrop: vi.fn()
}))

afterEach(() => vi.unstubAllGlobals())

it('pauses and reuses the Welcome canvas until the hidden conversation view expires', () => {
  vi.useFakeTimers()
  vi.stubGlobal('WebGL2RenderingContext', class {})
  vi.stubGlobal('matchMedia', () => ({
    matches: false, addEventListener: vi.fn(), removeEventListener: vi.fn()
  }))
  installDesktopApiMock({
    settings: { get: async () => ({ locale: 'en' }) },
    appServer: { sendRequest: async () => ({}) }
  })

  const setActive = vi.fn()
  vi.mocked(startWelcomeBackdrop).mockImplementation((host) => {
    const canvas = document.createElement('canvas')
    host.append(canvas)
    return { setActive, destroy: () => canvas.remove() }
  })

  const app = (activeView: 'conversation' | 'agents') => (
    <LocaleProvider loadSettings={false}>
      <MainViewOutlet
        activeView={activeView}
        retains={(view) => view === 'conversation'}
        renderView={(view) => view === 'conversation' ? <ConversationPanel /> : null}
      />
    </LocaleProvider>
  )

  const session = render(app('conversation'))
  const canvas = session.container.querySelector('canvas')
  expect(canvas).toBeInTheDocument()

  session.rerender(app('agents'))
  expect(canvas).toBeInTheDocument()
  expect(setActive).toHaveBeenLastCalledWith(false)

  session.rerender(app('conversation'))
  expect(session.container.querySelector('canvas')).toBe(canvas)
  expect(setActive).toHaveBeenLastCalledWith(true)

  session.rerender(app('agents'))
  act(() => vi.advanceTimersByTime(RETAIN_HIDDEN_VIEW_MS))
  expect(canvas).not.toBeInTheDocument()
})
