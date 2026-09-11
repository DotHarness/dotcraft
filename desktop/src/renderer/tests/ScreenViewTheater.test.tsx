import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import type { ScreenStream } from '../components/conversation/screenView/useScreenStream'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ScreenViewTheater } from '../components/conversation/screenView/ScreenViewTheater'
import { useScreenViewStore } from '../stores/screenViewStore'

const tune = vi.fn()
const originalApi = window.api

function renderTheater(aspect: number | null): HTMLElement {
  const stream: ScreenStream = { setCanvas: () => undefined, state: { kind: 'live' }, aspect }
  render(
    <LocaleProvider loadSettings={false}>
      <ScreenViewTheater hostName="Studio PC" stream={stream} />
    </LocaleProvider>
  )
  return screen.getByRole('dialog', { name: 'Studio PC · Live' })
}

beforeEach(() => {
  tune.mockClear()
  // jsdom has no modal dialog; the open attribute is what exposes the role.
  HTMLDialogElement.prototype.showModal = function showModal(this: HTMLDialogElement): void {
    this.setAttribute('open', '')
  }
  HTMLDialogElement.prototype.close = function close(this: HTMLDialogElement): void {
    this.removeAttribute('open')
  }
  window.api = { screenView: { tune } } as unknown as typeof window.api
  useScreenViewStore.setState({ viewId: 'view-1', requestedWidth: null })
})

afterEach(() => {
  window.api = originalApi
})

describe('ScreenViewTheater', () => {
  it('takes its shape from the stream, so a frame of any size cannot resize it', () => {
    const theater = renderTheater(4 / 3)

    expect(theater.style.getPropertyValue('--screen-aspect')).toBe(`${4 / 3}`)
  })

  it('falls back to a wide shape before the first frame arrives', () => {
    const theater = renderTheater(null)

    expect(theater.style.getPropertyValue('--screen-aspect')).toBe('16 / 9')
  })

  it('asks the host for one width when it opens', () => {
    renderTheater(16 / 9)

    expect(tune).toHaveBeenCalledTimes(1)
  })
})
