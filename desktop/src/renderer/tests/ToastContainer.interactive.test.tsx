import { fireEvent, render } from '@testing-library/react'
import { act } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ToastContainer } from '../components/ui/ToastContainer'
import { showToast, useToastStore } from '../stores/toastStore'
import { installDesktopApiMock } from './desktopApiMock'

class ResizeObserverStub {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}

beforeEach(() => {
  vi.stubGlobal('ResizeObserver', ResizeObserverStub)
  installDesktopApiMock({
    platform: 'win32',
    titleBarOverlayHeight: 0,
    initialLocale: 'en',
    settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) }
  })
  useToastStore.setState({ toasts: [] })
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
  useToastStore.setState({ toasts: [] })
})

function renderToasts() {
  return render(
    <LocaleProvider>
      <ToastContainer />
    </LocaleProvider>
  )
}

describe('interactive toast (action + onExpire)', () => {
  it('runs the action and not onExpire when the action button is clicked', () => {
    vi.useFakeTimers()
    const onClick = vi.fn()
    const onExpire = vi.fn()
    showToast({ message: 'Approved task.', durationMs: 5000, action: { label: 'Undo', icon: 'undo', onClick }, onExpire })

    const { getByRole } = renderToasts()
    act(() => {
      fireEvent.click(getByRole('button', { name: 'Undo' }))
    })

    expect(onClick).toHaveBeenCalledTimes(1)
    expect(onExpire).not.toHaveBeenCalled()

    // The undo window elapsing afterward must not also fire onExpire (already settled).
    act(() => {
      vi.advanceTimersByTime(6000)
    })
    expect(onExpire).not.toHaveBeenCalled()
  })

  it('commits via onExpire (not the action) when the undo window elapses', () => {
    vi.useFakeTimers()
    const onClick = vi.fn()
    const onExpire = vi.fn()
    showToast({ message: 'Approved task.', durationMs: 1000, action: { label: 'Undo', onClick }, onExpire })

    renderToasts()
    act(() => {
      vi.advanceTimersByTime(1100)
    })

    expect(onExpire).toHaveBeenCalledTimes(1)
    expect(onClick).not.toHaveBeenCalled()
  })
})
