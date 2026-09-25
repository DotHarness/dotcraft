import { useVoiceStore } from '../voice/voiceStore'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ComposerFeedbackAttachments } from '../components/conversation/ComposerFeedbackAttachments'
import { useComposerContextStore } from '../stores/composerContextStore'
import { installDesktopApiMock } from './desktopApiMock'

const reply = {
  kind: 'responseAnnotation' as const,
  id: 'reply',
  threadId: 'task',
  turnId: 'turn',
  itemId: 'item',
  selectedText: 'Keep the original source.',
  comment: '',
}
const page = {
  kind: 'pageReference' as const,
  id: 'page',
  url: 'https://example.test',
  title: 'Example page',
  selectionKind: 'text' as const,
  text: 'Get started',
  comment: 'Make this easier to find',
}
const diff = {
  kind: 'diffAnnotation' as const,
  id: 'diff',
  path: '/src/file.ts',
  side: 'left' as const,
  startLine: 40,
  endLine: 42,
  selectedText: 'return title;',
  comment: 'Preserve whitespace',
}
const paste = {
  kind: 'pastedText' as const,
  id: 'paste',
  path: '/paste.txt',
  fileName: 'paste.txt',
  preview: 'notes',
}
function Fixture() {
  const contexts = useComposerContextStore((state) => state.getContexts('task'))
  return (
    <LocaleProvider>
      <ComposerFeedbackAttachments
        threadId="task"
        contexts={contexts.filter((context) => context.kind !== 'pastedText')}
      />
    </LocaleProvider>
  )
}
const contexts = () => useComposerContextStore.getState().getContexts('task')
const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms))

describe('feedback attachment pills', () => {
  beforeEach(() => {
    useVoiceStore.setState({
      initialized: true,
      recording: null,
      finalizing: null,
      snapshot: {
        model: { phase: 'missing', bytesDownloaded: 0, bytesTotal: null },
        chatGpt: { signedIn: false, enabled: true },
        sessions: [],
        capacity: 2,
      },
    })
    installDesktopApiMock({
      settings: { get: async () => ({ locale: 'en' }) },
    })
    useComposerContextStore.setState({ byThread: {} })
    useComposerContextStore.getState().setContexts('task', [reply, page, diff, paste])
  })

  it('removes everything a pill counts and nothing else', async () => {
    render(<Fixture />)
    fireEvent.click(await screen.findByRole('button', { name: 'Remove comments' }))
    expect(contexts()).toEqual([reply, page, paste])
    fireEvent.click(screen.getByRole('button', { name: 'Remove annotations' }))
    expect(contexts()).toEqual([paste])
  })

  it('opens a list on hover, keeps it while the pointer is over it, and closes after the pointer leaves', async () => {
    render(<Fixture />)
    const pill = await screen.findByRole('button', { name: '1 comment' })
    fireEvent.mouseEnter(pill)
    const list = screen.getByRole('dialog', { name: '1 comment' })
    expect(list).toHaveTextContent(diff.comment)
    fireEvent.mouseLeave(pill)
    fireEvent.mouseEnter(list)
    await wait(150)
    expect(screen.getByRole('dialog', { name: '1 comment' })).toBeVisible()
    fireEvent.mouseLeave(list)
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
  })

  it('toggles the list on click', async () => {
    render(<Fixture />)
    const pill = await screen.findByRole('button', { name: '2 annotations' })
    fireEvent.click(pill)
    expect(screen.getByRole('dialog', { name: '2 annotations' })).toHaveTextContent(page.comment)
    fireEvent.click(pill)
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('edits and removes a response annotation from its row, while line comments offer no row actions', async () => {
    render(<Fixture />)
    fireEvent.click(await screen.findByRole('button', { name: '1 comment' }))
    expect(within(screen.getByRole('dialog')).queryByRole('button', { name: /annotation/ })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: '1 comment' }))

    const pill = screen.getByRole('button', { name: '2 annotations' })
    fireEvent.click(pill)
    fireEvent.click(screen.getByRole('button', { name: 'Edit annotation 1' }))
    const editor = screen.getByRole('textbox', { name: 'Your comment' })
    fireEvent.mouseLeave(pill)
    await wait(150)
    fireEvent.change(editor, { target: { value: 'Keep this exact range' } })
    fireEvent.keyDown(editor, { key: 'Enter' })
    await waitFor(() =>
      expect(contexts()).toEqual([{ ...reply, comment: 'Keep this exact range' }, page, diff, paste]),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Remove annotation 1' }))
    expect(contexts()).toEqual([page, diff, paste])
  })
})
