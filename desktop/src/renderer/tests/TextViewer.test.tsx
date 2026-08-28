// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { TextViewer } from '../components/detail/viewers/TextViewer'
import { navigationRowIndex } from '../components/detail/viewers/useNavigationLine'
import type { FileNavigationHint } from '../../shared/viewer/types'
import { installDesktopApiMock } from './desktopApiMock'

const readTextMock = vi.fn()

/** Height the viewer assumes for one unwrapped row when tokens are unavailable. */
const ROW_HEIGHT = 12 * 1.55

describe('navigationRowIndex', () => {
  it('converts a one-based hint line to a row index', () => {
    expect(navigationRowIndex({ line: 2 }, 3)).toBe(1)
  })

  it('clamps a hint past the end of the file', () => {
    expect(navigationRowIndex({ line: 200 }, 2)).toBe(1)
  })

  it('ignores a hint with no usable line', () => {
    expect(navigationRowIndex({ line: 0 }, 3)).toBeUndefined()
    expect(navigationRowIndex(undefined, 3)).toBeUndefined()
    expect(navigationRowIndex({ line: 1 }, 0)).toBeUndefined()
  })
})

describe('TextViewer', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    readTextMock.mockResolvedValue({ text: 'one\ntwo\nthree\nfour', truncated: false })
    installDesktopApiMock({
      settings: { get: () => Promise.resolve({ locale: 'en' }) },
      workspace: { viewer: { readText: readTextMock } }
    })
  })

  function renderViewer(navigationHint?: FileNavigationHint): ReturnType<typeof render> {
    return render(
      <LocaleProvider>
        <TextViewer absolutePath="C:/repo/src/Foo.cs" navigationHint={navigationHint} />
      </LocaleProvider>
    )
  }

  it('renders the file with a gutter number per line', async () => {
    const { container } = renderViewer()

    await waitFor(() => {
      expect(container.querySelectorAll('[data-line]')).toHaveLength(4)
    })
    expect(container.textContent).toContain('three')
    const gutters = [...container.querySelectorAll('[data-line-num]')].map((node) => node.textContent)
    expect(gutters).toEqual(['1', '2', '3', '4'])
  })

  it('scrolls to the requested line', async () => {
    renderViewer({ line: 3, column: 99 })

    await waitFor(() => {
      expect(screen.getByTestId('text-viewer-lines').scrollTop).toBeCloseTo(ROW_HEIGHT * 2, 1)
    })
  })

  it('shows the truncation notice for a partially read file', async () => {
    readTextMock.mockResolvedValue({ text: 'one\ntwo', truncated: true })
    renderViewer()

    expect(await screen.findByRole('status')).toHaveTextContent('File is large')
  })

  it('reports a read failure instead of rendering an empty file', async () => {
    readTextMock.mockRejectedValue(new Error('EACCES'))
    const { container } = renderViewer()

    await waitFor(() => {
      expect(container.textContent).toContain('EACCES')
    })
    expect(container.querySelectorAll('[data-line]')).toHaveLength(0)
  })
})
