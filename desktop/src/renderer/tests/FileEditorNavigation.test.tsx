import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { EditorView } from '@codemirror/view'
import { StateEffect } from '@codemirror/state'
import { undo, undoDepth } from '@codemirror/commands'
import { FileEditor } from '../components/detail/viewers/FileEditor'
import { fileNavigationField } from '../components/detail/viewers/fileNavigation'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useFileEditorStore } from '../stores/fileEditorStore'
import { installDesktopApiMock } from './desktopApiMock'
import type { FileNavigationHint, ReadTextResult } from '../../shared/viewer/types'

const path = 'C:/repo/navigation.ts'
const text = Array.from({ length: 150 }, (_, index) => `line ${index + 1}`).join('\n')
const store = () => useFileEditorStore.getState()
const view = () => EditorView.findFromDOM(screen.getByRole('textbox', { name: path }))!
const navigation = () => view().state.field(fileNavigationField)
const session = () => store().sessions.get('one')!

function editor(hint?: FileNavigationHint, revision = 1, markdown = false, tabId = 'one') {
  return <LocaleProvider loadSettings={false}>
    <FileEditor tabId={tabId} absolutePath={path} markdown={markdown} wordWrap
      navigationHint={hint} navigationRevision={revision} />
  </LocaleProvider>
}

const disk: ReadTextResult = {
  text, truncated: false, encoding: 'utf-8', mtimeMs: 1, sizeBytes: text.length,
  hasUtf8Bom: false, lineEnding: 'lf'
}

describe('file-link navigation in editable documents', () => {
  const readText = vi.fn()
  const writeText = vi.fn()
  beforeEach(() => {
    readText.mockReset().mockImplementation(async () => ({ ...disk }))
    writeText.mockReset().mockImplementation(async () => ({ outcome: 'saved', mtimeMs: 2, sizeBytes: text.length }))
    installDesktopApiMock({ workspace: { viewer: {
      readText, writeText, watchText: vi.fn(async () => () => {})
    } } })
    Range.prototype.getClientRects = () => [] as unknown as DOMRectList
    Range.prototype.getBoundingClientRect = () => new DOMRect()
  })
  afterEach(() => {
    act(() => { for (const id of store().sessions.keys()) store().discard(id) })
  })

  it('navigates a range without selecting or modifying its text or undo history', async () => {
    render(editor({ line: 53, endLine: 57 }))
    await waitFor(() => expect(session().lastNavigationRevision).toBe(1))
    expect(navigation()).toMatchObject({ line: 53, endLine: 57 })
    expect(view().state.selection.main.empty).toBe(true)
    expect(view().state.selection.main.anchor).toBe(view().state.doc.line(53).from)
    expect(session().text).toBe(text)
    expect(undoDepth(view().state)).toBe(0)
    expect(writeText).not.toHaveBeenCalled()
  })

  it('replays explicit clicks on the same position but not ordinary rerenders', async () => {
    const hint = { line: 123 }
    const mounted = render(editor(hint))
    await waitFor(() => expect(session().lastNavigationRevision).toBe(1))
    act(() => { view().dispatch({ selection: { anchor: 0 } }) })
    expect(navigation()).toBeUndefined()
    mounted.rerender(editor(hint))
    expect(view().state.selection.main.anchor).toBe(0)
    mounted.rerender(editor(hint, 2))
    await waitFor(() => expect(session().lastNavigationRevision).toBe(2))
    expect(view().state.selection.main.anchor).toBe(view().state.doc.line(123).from)
    expect(navigation()?.line).toBe(123)
  })

  it('preserves the reading position on tab return instead of replaying a consumed link', async () => {
    const hint = { line: 53, endLine: 57 }
    const mounted = render(editor(hint))
    await waitFor(() => expect(session().lastNavigationRevision).toBe(1))
    act(() => { view().dispatch({ selection: { anchor: 3 } }) })
    mounted.rerender(editor(undefined, 1, false, 'two'))
    await waitFor(() => expect(store().sessions.get('two')?.lastNavigationRevision).toBe(1))
    mounted.rerender(editor(hint))
    await waitFor(() => expect(view().state.selection.main.anchor).toBe(3))
    expect(navigation()).toBeUndefined()
  })

  it('waits for a delayed load and uses only the latest link request', async () => {
    let finish!: (value: ReadTextResult) => void
    readText.mockReturnValueOnce(new Promise<ReadTextResult>((resolve) => { finish = resolve }))
    const mounted = render(editor({ line: 10 }))
    mounted.rerender(editor({ line: 90, endLine: 93 }, 2))
    await act(async () => { finish({ ...disk }) })
    await waitFor(() => expect(session().lastNavigationRevision).toBe(2))
    expect(navigation()).toMatchObject({ line: 90, endLine: 93 })
    expect(view().state.doc.lineAt(view().state.selection.main.anchor).number).toBe(90)
  })

  it('retains the hint during focus and unrelated updates, but clears it on editing', async () => {
    render(editor({ line: 3, endLine: 5 }))
    await waitFor(() => expect(session().lastNavigationRevision).toBe(1))
    act(() => { view().focus(); view().dispatch({ effects: StateEffect.appendConfig.of([]) }) })
    expect(navigation()).toMatchObject({ line: 3, endLine: 5 })
    act(() => {
      const anchor = view().state.selection.main.anchor
      view().dispatch({ changes: { from: anchor, insert: 'x' }, selection: { anchor: anchor + 1 } })
    })
    expect(navigation()).toBeUndefined()
    expect(session().text).toContain('xline 3')
    act(() => { undo(view()) })
    expect(session().text).toBe(text)
    expect(navigation()).toBeUndefined()
  })

  it('clears the hint on a click at the current caret or an arrow key at a boundary', async () => {
    const mounted = render(editor({ line: 1 }))
    await waitFor(() => expect(session().lastNavigationRevision).toBe(1))
    fireEvent.mouseDown(screen.getByRole('textbox', { name: path }))
    expect(navigation()).toBeUndefined()
    mounted.rerender(editor({ line: 1 }, 2))
    await waitFor(() => expect(session().lastNavigationRevision).toBe(2))
    fireEvent.keyDown(screen.getByRole('textbox', { name: path }), { key: 'ArrowLeft' })
    expect(navigation()).toBeUndefined()
  })

  it('clears an old range when the file is opened without a location', async () => {
    const mounted = render(editor({ line: 20, endLine: 25 }))
    await waitFor(() => expect(session().lastNavigationRevision).toBe(1))
    mounted.rerender(editor(undefined, 2))
    await waitFor(() => expect(session().lastNavigationRevision).toBe(2))
    expect(navigation()).toBeUndefined()
    expect(view().state.doc.lineAt(view().state.selection.main.anchor).number).toBe(20)
  })

  it('requests Markdown source mode for line links', async () => {
    render(editor({ line: 12, endLine: 20 }, 1, true))
    await waitFor(() => expect(session().lastNavigationRevision).toBe(1))
    expect(session().mode).toBe('source')
    expect(navigation()).toMatchObject({ line: 12, endLine: 20 })
    expect(session().text).toBe(text)
  })

  it('does not bypass failed saving when a line link requests Markdown source mode', async () => {
    await act(async () => { await store().load('one', path, true) })
    act(() => { store().updateText('one', text + '\ndraft') })
    writeText.mockRejectedValue(new Error('denied'))
    render(editor({ line: 12 }, 1, true))
    await waitFor(() => expect(session().saveState).toBe('failed'))
    expect(session().mode).toBe('preview')
    expect(session().lastNavigationRevision).toBeUndefined()
    expect(session().text).toBe(text + '\ndraft')
  })

  it('clears navigation on external content changes without replaying the link', async () => {
    render(editor({ line: 3 }))
    await waitFor(() => expect(session().lastNavigationRevision).toBe(1))
    readText.mockResolvedValue({ ...disk, text: 'updated\n' + text, mtimeMs: 2 })
    await act(async () => { await store().refreshFromDisk('one') })
    await act(async () => { await store().resolveReview('one', 'accept') })
    expect(navigation()).toBeUndefined()
    expect(session().lastNavigationRevision).toBe(1)
    expect(session().text).toBe('updated\n' + text)
  })
})
