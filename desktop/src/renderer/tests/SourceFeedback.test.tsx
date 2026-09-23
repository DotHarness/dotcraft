import { act, fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ResponseFeedback } from '../components/conversation/ResponseFeedback'
import { DiffViewer } from '../components/detail/DiffViewer'
import { useComposerContextStore } from '../stores/composerContextStore'
import { useThreadStore } from '../stores/threadStore'
import { useConversationStore } from '../stores/conversationStore'
import { useVoiceStore } from '../voice/voiceStore'
import { installDesktopApiMock } from './desktopApiMock'
import type { FileDiff } from '../types/toolCall'

beforeEach(() => {
  Range.prototype.getBoundingClientRect = vi.fn(() => ({ left: 0, top: 0, right: 200, bottom: 24, width: 200, height: 24 }) as DOMRect)
  useComposerContextStore.setState({ byThread: {} })
  useThreadStore.setState({ activeThreadId: 'task' })
  useConversationStore.setState({ turns: [] })
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
  installDesktopApiMock({ settings: { get: async () => ({ locale: 'en' }) } })
})

it('captures only the actual selected reply text with its source identity', () => {
  render(
    <LocaleProvider>
      <ResponseFeedback threadId="task" turnId="turn" itemId="item">
        <p>Review these changes carefully.</p>
      </ResponseFeedback>
    </LocaleProvider>,
  )
  const paragraph = screen.getByText('Review these changes carefully.')
  const range = document.createRange()
  range.setStart(paragraph.firstChild!, 7)
  range.setEnd(paragraph.firstChild!, 20)
  window.getSelection()!.removeAllRanges()
  window.getSelection()!.addRange(range)
  fireEvent.mouseUp(paragraph)
  fireEvent.click(screen.getByRole('button', { name: 'Add to chat' }))
  expect(useComposerContextStore.getState().getContexts('task')).toEqual([
    expect.objectContaining({
      kind: 'responseAnnotation',
      threadId: 'task',
      turnId: 'turn',
      itemId: 'item',
      selectedText: 'these changes',
      comment: '',
    }),
  ])
})

const diff: FileDiff = {
  filePath: '/workspace/a.ts',
  additions: 1,
  deletions: 1,
  status: 'written',
  isNewFile: false,
  diffHunks: [
    {
      oldStart: 40,
      oldLines: 2,
      newStart: 44,
      newLines: 2,
      lines: [
        { type: 'context', content: 'shared' },
        { type: 'remove', content: 'old' },
        { type: 'add', content: 'new' },
      ],
    },
  ],
}

it.each(['inline', 'split'] as const)(
  'keeps old-side range and user comment when using %s diff',
  (mode) => {
    render(
      <LocaleProvider>
        <DiffViewer diff={diff} workspacePath="/workspace" mode={mode} />
      </LocaleProvider>,
    )
    fireEvent.click(
      screen.getByRole('button', { name: 'Comment on old line 40' }),
    )
    fireEvent.click(
      screen.getByRole('button', { name: 'Comment on old line 41' }),
      { shiftKey: true },
    )
    fireEvent.change(screen.getByRole('textbox', { name: 'Your comment' }), {
      target: { value: 'Keep the old behavior' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(useComposerContextStore.getState().getContexts('task')).toEqual([
      expect.objectContaining({
        kind: 'diffAnnotation',
        side: 'left',
        startLine: 40,
        endLine: 41,
        selectedText: 'shared\nold',
        comment: 'Keep the old behavior',
      }),
    ])
  },
)

it('renders model review at the matching new line without making it a user draft', () => {
  act(() =>
    useConversationStore.setState({
      turns: [
        {
          id: 'turn',
          threadId: 'task',
          status: 'completed',
          startedAt: '2026-09-14T00:00:00Z',
          items: [
            {
              id: 'review',
              createdAt: '2026-09-14T00:00:00Z',
              type: 'agentMessage',
              status: 'completed',
              text: '::code-comment{title="Review" body="Check the new value" file="a.ts" start=45 end=45}',
            },
          ],
        },
      ],
    }),
  )
  render(
    <LocaleProvider>
      <DiffViewer diff={diff} workspacePath="/workspace" mode="split" />
    </LocaleProvider>,
  )
  expect(screen.getByText('Check the new value')).toBeTruthy()
  expect(screen.getByText('Model review · 45–45')).toBeTruthy()
  expect(useComposerContextStore.getState().getContexts('task')).toEqual([])
})
