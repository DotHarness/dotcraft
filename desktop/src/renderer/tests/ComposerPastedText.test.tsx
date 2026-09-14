import { useRef } from 'react'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import {
  RichInputArea,
  type RichInputAreaHandle,
} from '../components/conversation/RichInputArea'
import { ComposerContextAttachments } from '../components/conversation/ComposerContextAttachments'
import {
  usePastedText,
  usePendingPasteStore,
} from '../components/conversation/usePastedText'
import { useComposerContextStore } from '../stores/composerContextStore'
import { installDesktopApiMock } from './desktopApiMock'

const text = 'review\n'.repeat(18000)
const context = {
  kind: 'pastedText' as const,
  id: 'paste',
  path: '/fixture/paste.txt',
  fileName: 'paste.txt',
  preview: 'review',
  characterCount: text.length,
}
const create = vi.fn()
const read = vi.fn()
const restore = vi.fn()

function Composer() {
  const editor = useRef<RichInputAreaHandle>(null)
  const pastedText = usePastedText('task', '/fixture')
  return (
    <LocaleProvider>
      <ComposerContextAttachments
        threadId="task"
        editorRef={editor}
        pastedText={pastedText}
      />
      <RichInputArea
        ref={editor}
        onPasteText={pastedText.onPasteText}
        onSubmit={() => {}}
      />
    </LocaleProvider>
  )
}
function paste(value: string) {
  fireEvent.paste(screen.getByRole('textbox'), {
    clipboardData: {
      items: [],
      types: ['text/plain'],
      getData: (type: string) => (type === 'text/plain' ? value : ''),
    },
  })
}

describe('pasted text composer', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useComposerContextStore.setState({ byThread: {} })
    usePendingPasteStore.setState({ pastes: [] })
    create.mockResolvedValue(context)
    read.mockResolvedValue({ text })
    restore.mockResolvedValue({ text: 'r'.repeat(5000) })
    installDesktopApiMock({
      settings: { get: async () => ({ locale: 'en' }) },
      workspace: {
        createPastedText: create,
        readPastedText: read,
        restorePastedText: restore,
      },
    })
    HTMLDialogElement.prototype.showModal = function () {
      this.setAttribute('open', '')
    }
  })

  it('passes more than 100,000 characters intact to file creation and opens the full file', async () => {
    render(<Composer />)
    paste(text)
    await waitFor(() =>
      expect(create).toHaveBeenCalledWith({ text, workspacePath: '/fixture' }),
    )
    fireEvent.click(
      await screen.findByRole('button', { name: 'Preview pasted text' }),
    )
    await waitFor(() =>
      expect(screen.getByRole('dialog').querySelector('pre')?.textContent).toBe(
        text,
      ),
    )
    expect(
      screen.queryByRole('button', { name: 'Show in text field' }),
    ).toBeNull()
    expect(screen.getByRole('textbox').textContent).toBe('')
  })

  it('restores eligible text without replacing the current draft', async () => {
    useComposerContextStore
      .getState()
      .addContext('task', { ...context, characterCount: 5000 })
    render(<Composer />)
    const editor = screen.getByRole('textbox')
    editor.textContent = 'Keep my question'
    fireEvent.input(editor)
    fireEvent.click(screen.getByRole('button', { name: 'Show in text field' }))
    await waitFor(() =>
      expect(editor.textContent).toBe(
        `Keep my question\n\n${'r'.repeat(5000)}`,
      ),
    )
    expect(useComposerContextStore.getState().getContexts('task')).toEqual([])
  })

  it('does not offer restoration for an unknown length', () => {
    useComposerContextStore
      .getState()
      .addContext('task', { ...context, characterCount: undefined })
    render(<Composer />)
    expect(
      screen.queryByRole('button', { name: 'Show in text field' }),
    ).toBeNull()
  })

  it('keeps a removed pending paste removed after file creation completes', async () => {
    let finish!: (value: typeof context) => void
    create.mockImplementation(
      () =>
        new Promise((resolve) => {
          finish = resolve
        }),
    )
    render(<Composer />)
    paste(text)
    fireEvent.click(screen.getByRole('button', { name: 'Remove attachment' }))
    await act(async () => finish(context))
    expect(useComposerContextStore.getState().getContexts('task')).toEqual([])
    expect(
      screen.queryByRole('button', { name: 'Preview pasted text' }),
    ).toBeNull()
  })
})
