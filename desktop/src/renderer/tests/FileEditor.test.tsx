import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { EditorView } from '@codemirror/view'
import { undo, redo } from '@codemirror/commands'
import { FileEditor } from '../components/detail/viewers/FileEditor'
import { renderMermaidSvg } from '../components/conversation/MermaidDiagram'
import { canPreviewFileReview } from '../components/detail/viewers/FileReview'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useFileEditorStore } from '../stores/fileEditorStore'
import { useFindStore } from '../stores/findStore'
import { installDesktopApiMock } from './desktopApiMock'
import type { ReadTextResult, WriteTextParams } from '../../shared/viewer/types'

vi.mock('../components/conversation/MermaidDiagram', () => ({
  renderMermaidSvg: vi.fn(async () => '<svg aria-label="diagram"></svg>')
}))

const path = 'C:/repo/file.md'
const store = () => useFileEditorStore.getState()
const current = () => store().sessions.get('one')!
const view = () => EditorView.findFromDOM(screen.getByRole('textbox', { name: path }))!
const markdown =
  '# Heading\n\n**bold** and *italic* and ~~gone~~ with `inline`\n\n> quote\n\n- [x] item\n\n| A | B |\n| --- | --- |\n| x | y |\n\n```ts\nconst a = 1\n```\n\n```mermaid\nflowchart LR\n  A --> B\n```'

describe('FileEditor integration', () => {
  let disk: ReadTextResult
  const writeText = vi.fn()
  beforeEach(() => {
    vi.mocked(renderMermaidSvg).mockClear()
    disk = {
      text: markdown,
      truncated: false,
      encoding: 'utf-8',
      mtimeMs: 1,
      sizeBytes: markdown.length,
      hasUtf8Bom: false,
      lineEnding: 'lf'
    }
    writeText.mockReset().mockImplementation(async (request: WriteTextParams) => {
      disk = { ...disk, text: request.text, mtimeMs: disk.mtimeMs + 1 }
      return { outcome: 'saved', mtimeMs: disk.mtimeMs, sizeBytes: disk.text.length }
    })
    installDesktopApiMock({
      workspace: {
        viewer: {
          readText: vi.fn(async () => ({ ...disk })),
          writeText,
          watchText: vi.fn(async () => () => {})
        }
      }
    })
    Range.prototype.getClientRects = () => [] as unknown as DOMRectList
    Range.prototype.getBoundingClientRect = () => new DOMRect()
  })
  afterEach(() => {
    act(() => {
      for (const id of store().sessions.keys()) store().discard(id)
    })
  })
  const editor = (tabId = 'one', isMarkdown = true) => (
    <LocaleProvider loadSettings={false}>
      <FileEditor tabId={tabId} absolutePath={path} markdown={isMarkdown} wordWrap />
    </LocaleProvider>
  )

  it('saves with the shortcut and keeps undo/redo through tab unmounts', async () => {
    const mounted = render(editor('one', false))
    await screen.findByRole('textbox', { name: path })
    act(() => {
      view().dispatch({ changes: { from: 0, insert: '// edit\n' }, selection: { anchor: 8 } })
    })
    expect(current().text).toBe('// edit\n' + markdown)
    fireEvent.keyDown(screen.getByRole('textbox', { name: path }), { key: 's', ctrlKey: true })
    await waitFor(() => expect(disk.text).toBe('// edit\n' + markdown))
    mounted.rerender(editor('two', false))
    await waitFor(() => expect(store().sessions.get('two')?.status).toBe('ready'))
    mounted.rerender(editor('one', false))
    await waitFor(() => expect(view().state.selection.main.head).toBe(8))
    act(() => {
      undo(view())
    })
    expect(current().text).toBe(markdown)
    act(() => {
      redo(view())
    })
    expect(current().text).toBe('// edit\n' + markdown)
  })

  it('switches semantic/source modes without modifying Markdown syntax and invalidates stale mode history', async () => {
    render(editor())
    await screen.findByRole('textbox', { name: path })
    expect(view().state.doc.toString()).toBe(markdown)
    await waitFor(() =>
      expect(renderMermaidSvg).toHaveBeenCalledWith(
        expect.objectContaining({
          source: 'flowchart LR\n  A --> B'
        })
      )
    )
    act(() => {
      view().dispatch({ changes: { from: markdown.length, insert: '\npreview edit' } })
    })
    await act(async () => {
      await store().switchMode('one', 'source')
    })
    expect(view().state.doc.toString()).toBe(markdown + '\npreview edit')
    act(() => {
      view().dispatch({ changes: { from: 0, insert: 'source edit\n' } })
    })
    await act(async () => {
      await store().switchMode('one', 'preview')
    })
    expect(view().state.doc.toString()).toBe('source edit\n' + markdown + '\npreview edit')
    expect(current().text).toBe(disk.text)
  })

  it('returns to the same mounted editor after review and focuses Edit', async () => {
    render(editor('one', false))
    await screen.findByRole('textbox', { name: path })
    const original = view()
    act(() => {
      original.dispatch({ changes: { from: 0, insert: 'local\n' } })
    })
    disk = { ...disk, text: 'disk\n', mtimeMs: 2 }
    await act(async () => {
      await store().refreshFromDisk('one')
    })
    expect(original.dom.isConnected).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Edit', exact: true }))
    await waitFor(() => expect(current().review).toBeUndefined())
    expect(view()).toBe(original)
    expect(view().hasFocus).toBe(true)
    expect(view().state.doc.toString()).toBe('local\n' + markdown)
    act(() => {
      undo(view())
    })
    expect(current().text).toBe(markdown)
  })

  it('owns Find without opening replace or the global overlay', async () => {
    render(editor('one', false))
    await screen.findByRole('textbox', { name: path })
    fireEvent.keyDown(screen.getByRole('textbox', { name: path }), { key: 'f', ctrlKey: true })
    const query = await screen.findByRole('textbox', { name: 'Find', exact: true })
    fireEvent.change(query, { target: { value: 'Heading' } })
    await waitFor(() =>
      expect(
        view().state.sliceDoc(view().state.selection.main.from, view().state.selection.main.to)
      ).toBe('Heading')
    )
    expect(useFindStore.getState().open).toBe(false)
    expect(screen.queryByRole('textbox', { name: 'Replace' })).toBeNull()
    fireEvent.keyDown(query, { key: 'Escape' })
    expect(screen.queryByRole('search')).toBeNull()
  })

  it('keeps semantic Markdown read-only for large files', async () => {
    disk = { ...disk, readOnlyReason: 'large-file' }
    render(editor())
    await screen.findByRole('textbox', { name: path })
    expect(current().mode).toBe('preview')
    expect(view().state.readOnly).toBe(true)
    expect(view().state.doc.toString()).toBe(markdown)
  })

  it('honours one-based file navigation lines and columns', async () => {
    render(
      <LocaleProvider loadSettings={false}>
        <FileEditor
          tabId="one"
          absolutePath={path}
          markdown={false}
          wordWrap
          navigationHint={{ line: 3, column: 6 }}
        />
      </LocaleProvider>
    )
    await screen.findByRole('textbox', { name: path })
    expect(view().state.selection.main.anchor).toBe(view().state.doc.line(3).from + 5)
  })

  it('does not restore a stale focused-marker state after returning to a tab', async () => {
    const mounted = render(editor())
    await screen.findByRole('textbox', { name: path })
    act(() => {
      view().focus()
      view().dispatch({ selection: { anchor: 0 } })
    })
    expect(screen.getByRole('textbox', { name: path }).textContent).toContain('# Heading')
    mounted.rerender(editor('two'))
    await waitFor(() => expect(store().sessions.get('two')?.status).toBe('ready'))
    mounted.rerender(editor())
    await waitFor(() =>
      expect(screen.getByRole('textbox', { name: path }).textContent).not.toContain('# Heading')
    )
    expect(view().state.selection.main.anchor).toBe(0)
  })

  it('bounds review by UTF-8 bytes and line count, keeping boundary values previewable', () => {
    expect(canPreviewFileReview('a'.repeat(256 * 1024))).toBe(true)
    expect(canPreviewFileReview('a'.repeat(256 * 1024 + 1))).toBe(false)
    expect(canPreviewFileReview('文'.repeat(90000))).toBe(false)
    expect(canPreviewFileReview(Array(5000).fill('a').join('\n'))).toBe(true)
    expect(canPreviewFileReview(Array(5001).fill('a').join('\n'))).toBe(false)
  })
})
