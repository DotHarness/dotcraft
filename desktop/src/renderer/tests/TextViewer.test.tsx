// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { TextViewer } from '../components/detail/viewers/TextViewer'
import type { FileNavigationHint } from '../../shared/viewer/types'

const editorMock = vi.hoisted(() => ({
  getModel: vi.fn(),
  setPosition: vi.fn(),
  revealPositionInCenter: vi.fn()
}))

const modelMock = vi.hoisted(() => ({
  getLineCount: vi.fn(),
  getLineMaxColumn: vi.fn()
}))

vi.mock('monaco-editor', () => ({
  editor: {}
}))

vi.mock('@monaco-editor/react', async () => {
  const React = await vi.importActual<typeof import('react')>('react')
  return {
    loader: { config: vi.fn() },
    default: function MonacoEditorMock(props: {
      onMount?: (editor: typeof editorMock, monaco: unknown) => void
      value?: string
    }): JSX.Element {
      React.useEffect(() => {
        props.onMount?.(editorMock, {})
      }, [props])

      return React.createElement('div', {
        'data-testid': 'monaco-editor',
        'data-value': props.value ?? ''
      })
    }
  }
})

const readTextMock = vi.fn()

describe('TextViewer', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    readTextMock.mockResolvedValue({
      text: 'one\ntwo\nthree',
      truncated: false
    })
    editorMock.getModel.mockReturnValue(modelMock)
    modelMock.getLineCount.mockReturnValue(3)
    modelMock.getLineMaxColumn.mockImplementation((lineNumber: number) => {
      return lineNumber === 2 ? 4 : 8
    })
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: () => Promise.resolve({ locale: 'en' }) },
        workspace: {
          viewer: {
            readText: readTextMock
          }
        }
      }
    })
  })

  function renderViewer(navigationHint?: FileNavigationHint): ReturnType<typeof render> {
    return render(
      <LocaleProvider>
        <TextViewer
          absolutePath="C:/repo/src/Foo.cs"
          navigationHint={navigationHint}
        />
      </LocaleProvider>
    )
  }

  it('jumps to the requested line and clamps the requested column', async () => {
    renderViewer({ line: 2, column: 99 })

    await waitFor(() => {
      expect(editorMock.setPosition).toHaveBeenCalledWith({ lineNumber: 2, column: 4 })
    })
    expect(editorMock.revealPositionInCenter).toHaveBeenCalledWith({ lineNumber: 2, column: 4 })
  })

  it('clamps line hints to the loaded text range', async () => {
    modelMock.getLineCount.mockReturnValue(2)
    modelMock.getLineMaxColumn.mockReturnValue(5)

    renderViewer({ line: 200, column: 20 })

    await waitFor(() => {
      expect(editorMock.setPosition).toHaveBeenCalledWith({ lineNumber: 2, column: 5 })
    })
    expect(editorMock.revealPositionInCenter).toHaveBeenCalledWith({ lineNumber: 2, column: 5 })
  })

  it('ignores invalid line hints', async () => {
    renderViewer({ line: 0, column: 2 })

    await waitFor(() => {
      expect(editorMock.getModel).toHaveBeenCalled()
    })
    expect(editorMock.setPosition).not.toHaveBeenCalled()
    expect(editorMock.revealPositionInCenter).not.toHaveBeenCalled()
  })
})
