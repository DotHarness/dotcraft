import { createRef } from 'react'
import { act, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { RichInputArea, type RichInputAreaHandle } from '../components/conversation/RichInputArea'
import { useComposerFileReferenceRequest } from '../components/conversation/useComposerFileReferenceRequest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useUIStore } from '../stores/uiStore'
import { useComposerFileReferenceStore } from '../stores/composerFileReferenceStore'
import { useToastStore } from '../stores/toastStore'
import { useThreadStore } from '../stores/threadStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import { installDesktopApiMock } from './desktopApiMock'
import { buildComposerInputParts } from '../utils/composeInputParts'

const editorRef = createRef<RichInputAreaHandle>()

interface ComposerProps {
  disabled?: boolean
  remote?: boolean
  scopeId?: string | null
  ready?: boolean
}

function Composer({ disabled = false, remote = false, scopeId = null, ready = true }: ComposerProps) {
  useComposerFileReferenceRequest(editorRef, 'C:\\workspace', remote, disabled, scopeId, ready)
  return <RichInputArea ref={editorRef} disabled={disabled} onSubmit={vi.fn()} />
}

function fixture(props: ComposerProps = {}) {
  return render(<LocaleProvider><Composer {...props} /></LocaleProvider>)
}

describe('composer file reference requests', () => {
  beforeEach(() => {
    useUIStore.setState({ composerFileAttachmentRequest: null })
    useComposerFileReferenceStore.setState({ pendingByScope: new Map() })
    useToastStore.setState({ toasts: [] })
    useThreadStore.setState({ activeThreadId: null })
    useViewerTabStore.setState({ welcomeScopeId: null })
    installDesktopApiMock({ settings: { get: async () => ({ locale: 'en' }) } })
  })

  it('normalizes the workspace path and submits the file inline, not as an attachment', async () => {
    fixture()
    act(() => {
      editorRef.current!.setPlainText('Check end')
      editorRef.current!.setSelectionRange({ start: 6, end: 6 })
      screen.getByRole('textbox').blur()
      window.getSelection()?.removeAllRanges()
      useComposerFileReferenceStore.getState().request('c:\\WORKSPACE\\src\\file with spaces.ts')
    })
    await waitFor(() => expect(editorRef.current!.getSegments()).toEqual([
      { type: 'text', value: 'Check ' },
      { type: 'file', relativePath: 'src/file with spaces.ts' },
      { type: 'text', value: '\u00a0end' }
    ]))
    expect(useComposerFileReferenceStore.getState().pendingByScope.size).toBe(0)
    expect(useUIStore.getState().composerFileAttachmentRequest).toBeNull()
    expect(buildComposerInputParts({
      text: editorRef.current!.getText(),
      segments: editorRef.current!.getSegments()
    }).inputParts).toEqual([
      { type: 'text', text: 'Check ' },
      { type: 'fileRef', path: 'src/file with spaces.ts', displayPath: 'src/file with spaces.ts' },
      { type: 'text', text: '\u00a0end' }
    ])
  })

  it('retains an external absolute path rather than making it relative to the wrong root', async () => {
    fixture()
    act(() => useComposerFileReferenceStore.getState().request('D:\\other\\file.ts'))
    await waitFor(() => expect(editorRef.current!.getSegments()).toContainEqual({
      type: 'file', relativePath: 'D:/other/file.ts'
    }))
  })

  it('leaves a request pending while the composer is disabled and consumes it once editable', async () => {
    const view = fixture({ disabled: true })
    act(() => useComposerFileReferenceStore.getState().request('C:\\workspace\\src\\file.ts'))
    expect(editorRef.current!.getSegments()).toEqual([])
    expect(useComposerFileReferenceStore.getState().pendingByScope.has(null)).toBe(true)
    view.rerender(<LocaleProvider><Composer /></LocaleProvider>)
    await waitFor(() => expect(editorRef.current!.getSegments()).toContainEqual({
      type: 'file', relativePath: 'src/file.ts'
    }))
    expect(useComposerFileReferenceStore.getState().pendingByScope.size).toBe(0)
  })

  it('does not let a different composer consume a request queued for a disabled thread', async () => {
    useThreadStore.setState({ activeThreadId: 'thread-a' })
    const view = fixture({ scopeId: 'thread-a', disabled: true })
    act(() => useComposerFileReferenceStore.getState().request('C:\\workspace\\src\\file.ts'))
    view.rerender(<LocaleProvider><Composer scopeId="thread-b" /></LocaleProvider>)
    expect(editorRef.current!.getSegments()).toEqual([])
    expect(useComposerFileReferenceStore.getState().pendingByScope.get('thread-a')).toEqual(['C:\\workspace\\src\\file.ts'])
    view.rerender(<LocaleProvider><Composer scopeId="thread-a" /></LocaleProvider>)
    await waitFor(() => expect(editorRef.current!.getSegments()).toContainEqual({
      type: 'file', relativePath: 'src/file.ts'
    }))
  })

  it('waits for draft hydration and inserts all pending files in order', async () => {
    const view = fixture({ ready: false })
    act(() => {
      useComposerFileReferenceStore.getState().request('C:\\workspace\\src\\first.ts')
      useComposerFileReferenceStore.getState().request('C:\\workspace\\src\\second.ts')
    })
    expect(editorRef.current!.getSegments()).toEqual([])
    act(() => editorRef.current!.setPlainText('Saved draft '))
    view.rerender(<LocaleProvider><Composer /></LocaleProvider>)
    await waitFor(() => expect(editorRef.current!.getSegments()).toEqual([
      { type: 'text', value: 'Saved draft ' },
      { type: 'file', relativePath: 'src/first.ts' },
      { type: 'text', value: '\u00a0' },
      { type: 'file', relativePath: 'src/second.ts' },
      { type: 'text', value: '\u00a0' }
    ]))
  })

  it('rejects local files for remote-workspace input without modifying the draft', async () => {
    fixture({ remote: true })
    act(() => {
      editorRef.current!.setPlainText('Keep')
      useComposerFileReferenceStore.getState().request('C:\\workspace\\src\\file.ts')
    })
    await waitFor(() => expect(useToastStore.getState().toasts).toContainEqual(
      expect.objectContaining({ type: 'warning' })
    ))
    expect(editorRef.current!.getText()).toBe('Keep')
    expect(useComposerFileReferenceStore.getState().pendingByScope.size).toBe(0)
  })

  it('preserves the structured file when restoring the resulting draft', async () => {
    fixture()
    act(() => useComposerFileReferenceStore.getState().request('C:\\workspace\\src\\file.ts'))
    await waitFor(() => expect(useComposerFileReferenceStore.getState().pendingByScope.size).toBe(0))
    const segments = editorRef.current!.getSegments()
    act(() => {
      editorRef.current!.clear()
      editorRef.current!.setContent({ segments })
    })
    expect(editorRef.current!.getSegments()).toEqual(segments)
  })
})
