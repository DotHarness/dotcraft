import { useFindStore } from '../stores/findStore'
import { useUIStore } from '../stores/uiStore'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import {
  BrowserFeedbackControls,
  BrowserFindBar,
  type BrowserControlsState,
} from '../components/detail/viewers/BrowserFeedbackControls'
import { useBrowserFeedback } from '../components/detail/viewers/useBrowserFeedback'
import { useTransientOverlayStore } from '../stores/transientOverlayStore'
import type { BrowserFeedbackEvent } from '../../shared/viewer/browserFeedback'
import { installDesktopApiMock } from './desktopApiMock'

const api = {
  host: { onEvent: vi.fn().mockReturnValue(() => {}) },
  find: vi.fn().mockResolvedValue(undefined),
  closeFind: vi.fn().mockResolvedValue(undefined),
  setActive: vi.fn().mockResolvedValue(undefined),
  zoom: vi.fn().mockResolvedValue(110),
  inspect: vi.fn().mockResolvedValue(undefined),
  cancelDownload: vi.fn().mockResolvedValue(undefined),
  openDownload: vi.fn().mockResolvedValue(undefined),
}
const state: BrowserControlsState = {
  find: { query: 'hello', open: true, current: 1, total: 2 },
  zoom: 100,
  error: '',
  downloads: [
    {
      id: 'a',
      tabId: 'tab',
      filename: 'a.txt',
      path: '/downloads/a.txt',
      url: 'https://example.com/a',
      receivedBytes: 1,
      totalBytes: 2,
      state: 'progressing',
      startedAt: 1,
    },
    {
      id: 'b',
      tabId: 'tab',
      filename: 'b.txt',
      path: '/downloads/b.txt',
      url: 'https://example.com/b',
      receivedBytes: 2,
      totalBytes: 2,
      state: 'completed',
      startedAt: 0,
    },
  ],
}
beforeEach(() => {
  vi.clearAllMocks()
  vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} })
  useTransientOverlayStore.setState({
    openDepths: [],
    topDepth: 0,
    nativeViewBlockerCount: 0,
  })
  installDesktopApiMock({
    settings: { get: async () => ({ locale: 'en' }) },
    workspace: { viewer: { browser: api } },
  })
})

it('keeps Downloads out of the toolbar and opens download history from the menu without blocking the page', () => {
  render(<LocaleProvider><BrowserFeedbackControls tabId="tab" state={state} run={operation => { void operation() }} /></LocaleProvider>)
  expect(screen.queryByRole('button', { name: 'Downloads' })).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'More options' }))
  expect(screen.getAllByRole('menuitem').map(item => item.textContent)).toEqual(['Find in page', 'Downloads', 'Inspect', 'Browser settings'])
  fireEvent.click(screen.getByRole('menuitem', { name: 'Downloads' }))
  expect(useUIStore.getState()).toMatchObject({ activeMainView: 'settings', activeSettingsTab: 'browserUse', browserDownloadHistoryOpen: true })
  expect(useTransientOverlayStore.getState().nativeViewBlockerCount).toBe(0)
  expect(screen.queryByRole('dialog')).toBeNull()
  expect(screen.queryByRole('menu')).toBeNull()
})

it('opens the Browser settings tab without download history', () => {
  useUIStore.setState({ activeMainView: 'conversation', browserDownloadHistoryOpen: true })
  render(<LocaleProvider><BrowserFeedbackControls tabId="tab" state={state} run={operation => { void operation() }} /></LocaleProvider>)
  fireEvent.click(screen.getByRole('button', { name: 'More options' }))
  fireEvent.click(screen.getByRole('menuitem', { name: 'Browser settings' }))
  expect(useUIStore.getState()).toMatchObject({ activeMainView: 'settings', activeSettingsTab: 'browserUse', browserDownloadHistoryOpen: false })
})

it('routes keyboard find direction and closes the page search', async () => {
  render(
    <LocaleProvider>
      <BrowserFindBar
        tabId="tab"
        state={state.find}
        run={(operation) => {
          void operation()
        }}
      />
    </LocaleProvider>,
  )
  const field = screen.getByRole('textbox', { name: 'Find in page' })
  fireEvent.keyDown(field, { key: 'Enter', shiftKey: true })
  expect(api.find).toHaveBeenCalledWith({
    tabId: 'tab',
    query: 'hello',
    direction: 'previous',
  })
  await act(async () => fireEvent.keyDown(field, { key: 'Escape' }))
  expect(api.closeFind).toHaveBeenCalledWith({ tabId: 'tab' })
})

it('hands search back to the application without stealing focus into the page', () => {
  render(<LocaleProvider><BrowserFindBar tabId="tab" state={state.find} run={operation => { void operation() }} /></LocaleProvider>)
  act(() => useFindStore.getState().openFind())
  expect(api.closeFind).toHaveBeenCalledWith({ tabId: 'tab', restoreFocus: false })
})

it('keeps another tab’s find and zoom events out of the current controls', async () => {
  let listener: (event: BrowserFeedbackEvent) => void = () => {}
  const unsubscribe = vi.fn()
  installDesktopApiMock({
    workspace: {
      viewer: {
        browser: {
          downloads: async () => [],
          onFeedback: (callback) => {
            listener = callback
            return unsubscribe
          },
        },
      },
    },
  })
  function Host() {
    const { state } = useBrowserFeedback('a')
    return (
      <output>
        {state.zoom}:{state.find.query}
      </output>
    )
  }
  const view = render(<Host />)
  await act(async () => listener({ type: 'zoom', tabId: 'b', percent: 130 }))
  expect(screen.getByText('100:')).toBeTruthy()
  act(() =>
    listener({
      type: 'find',
      tabId: 'a',
      state: { query: 'current', open: true, current: 1, total: 1 },
    }),
  )
  expect(screen.getByText('100:current')).toBeTruthy()
  view.unmount()
  expect(unsubscribe).toHaveBeenCalledOnce()
})

it('keeps zoom operations open and restores the trigger on Escape', () => {
  render(<LocaleProvider><BrowserFeedbackControls tabId="tab" state={state} run={operation => { void operation() }} /></LocaleProvider>)
  const trigger = screen.getByRole('button', { name: 'More options' })
  fireEvent.click(trigger)
  expect(screen.getByRole('button', { name: 'Reset zoom' })).toBeDisabled()
  fireEvent.click(screen.getByRole('button', { name: 'Zoom in' }))
  expect(api.zoom).toHaveBeenCalledWith({ tabId: 'tab', action: 'in' })
  expect(screen.getByRole('menu')).toBeTruthy()
  fireEvent.keyDown(document, { key: 'Escape' })
  expect(screen.queryByRole('menu')).toBeNull()
  expect(trigger).toHaveFocus()
})

it('dismisses when the guest reports a page click', () => {
  render(<LocaleProvider><BrowserFeedbackControls tabId="tab" state={state} run={operation => { void operation() }} /></LocaleProvider>)
  fireEvent.click(screen.getByRole('button', { name: 'More options' }))
  const listener = api.host.onEvent.mock.calls.at(-1)?.[0] as unknown as (event: unknown) => void
  act(() => listener({ type: 'pointer-down', tabId: 'tab' }))
  expect(screen.queryByRole('menu')).toBeNull()
})
