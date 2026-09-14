import { useRef } from 'react'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import type { BrowserFeedbackEvent } from '../../shared/viewer/browserFeedback'
import { BrowserPageFeedback } from '../components/detail/viewers/BrowserPageFeedback'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useComposerContextStore } from '../stores/composerContextStore'
import { useVoiceStore } from '../voice/voiceStore'
import { installDesktopApiMock } from './desktopApiMock'

let listener: (event: BrowserFeedbackEvent) => void
const saveImage = vi.fn(async () => ({ path: '/attachments/page.png' }))
const select = vi.fn(() => new Promise<null>(() => {}))
const cancelSelection = vi.fn(async () => {})
beforeEach(() => {
  useComposerContextStore.setState({ byThread: {} })
  useVoiceStore.setState({
    initialized: true,
    recording: null,
    finalizing: null,
    snapshot: {
      model: { phase: 'missing', bytesDownloaded: 0, bytesTotal: null },
      sessions: [],
      capacity: 2,
    },
  })
  saveImage.mockClear()
  select.mockClear()
  cancelSelection.mockClear()
  installDesktopApiMock({
    settings: { get: async () => ({ locale: 'en' }) },
    workspace: {
      saveImageToTemp: saveImage,
      viewer: {
        browser: {
          onFeedback: (callback) => {
            listener = callback
            return () => {}
          },
          select,
          cancelSelection,
        },
      },
    },
  })
})
function Host() {
  const body = useRef<HTMLDivElement>(null)
  return (
    <LocaleProvider>
      <BrowserPageFeedback
        tabId="tab"
        threadId="task"
        body={body}
        disabled={false}
        run={(operation) => {
          void operation()
        }}
      />
      <div data-page ref={body} />
    </LocaleProvider>
  )
}
it('saves the selected image as a real attachment before adding the fixed page source', async () => {
  render(<Host />)
  act(() =>
    listener({
      type: 'selection',
      reference: {
        id: 'selection',
        tabId: 'tab',
        threadId: 'task',
        kind: 'region',
        url: 'https://example.com/original',
        title: 'Original',
        text: '',
        imageDataUrl: 'data:image/png;base64,AA==',
        previewDataUrl: 'data:image/png;base64,BB==',
        bounds: { x: 10, y: 20, width: 80, height: 40 },
      },
    }),
  )
  fireEvent.change(screen.getByRole('textbox', { name: 'Your comment' }), {
    target: { value: 'Align this area' },
  })
  fireEvent.click(screen.getByRole('button', { name: 'Save' }))
  await waitFor(() =>
    expect(useComposerContextStore.getState().getContexts('task')).toHaveLength(
      1,
    ),
  )
  expect(saveImage).toHaveBeenCalledWith({
    dataUrl: 'data:image/png;base64,AA==',
    fileName: 'page-selection.png',
  })
  expect(useComposerContextStore.getState().getContexts('task')[0]).toEqual({
    kind: 'pageReference',
    id: 'selection',
    url: 'https://example.com/original',
    title: 'Original',
    selectionKind: 'region',
    text: '',
    comment: 'Align this area',
    image: {
      tempPath: '/attachments/page.png',
      dataUrl: 'data:image/png;base64,AA==',
      fileName: 'page-selection.png',
      mimeType: 'image/png',
    },
  })
})
it('ignores a selection delivered for a different task', () => {
  render(<Host />)
  act(() =>
    listener({
      type: 'selection',
      reference: {
        id: 'selection',
        tabId: 'tab',
        threadId: 'other-task',
        kind: 'text',
        url: 'https://example.com',
        title: 'Other',
        text: 'Other task text',
      },
    }),
  )
  expect(screen.queryByRole('textbox', { name: 'Your comment' })).toBeNull()
})

it('grows into the Annotating mode and leaves it on a second click or Escape', () => {
  render(<Host />)
  const toggle = screen.getByRole('button', { name: 'Annotate' })
  expect(toggle.getAttribute('aria-pressed')).toBe('false')
  fireEvent.click(toggle)
  expect(select).toHaveBeenCalledWith(expect.objectContaining({ tabId: 'tab', kind: 'element' }))
  expect(toggle.getAttribute('aria-pressed')).toBe('true')
  expect(toggle.getAttribute('aria-label')).toBe('Annotating')
  expect(toggle.textContent).toBe('Annotating')
  fireEvent.click(toggle)
  expect(cancelSelection).toHaveBeenCalledWith({ tabId: 'tab' })
  expect(toggle.getAttribute('aria-pressed')).toBe('false')
  fireEvent.click(toggle)
  fireEvent.keyDown(window, { key: 'Escape' })
  expect(cancelSelection).toHaveBeenCalledTimes(2)
  expect(toggle.getAttribute('aria-pressed')).toBe('false')
})

it('marks the selection with the speech bubble and places the editor beside it', () => {
  render(<Host />)
  const bounds = { x: 10, y: 20, width: 80, height: 40 }
  const page = document.querySelector('[data-page]')!
  Object.defineProperty(page, 'clientWidth', { value: 1000 })
  Object.defineProperty(page, 'clientHeight', { value: 600 })
  act(() =>
    listener({
      type: 'selection',
      reference: {
        id: 'selection',
        tabId: 'tab',
        threadId: 'task',
        kind: 'region',
        url: 'https://example.com',
        title: 'Example',
        text: '',
        bounds,
      },
    }),
  )
  expect(document.querySelector('.dc-browser-page-feedback__selection .dc-browser-page-feedback__marker svg path')).not.toBeNull()
  const editor = document.querySelector<HTMLElement>('.dc-browser-page-feedback__editor')!
  expect([editor.style.left, editor.style.top]).toEqual(['115px', '20px'])
})
