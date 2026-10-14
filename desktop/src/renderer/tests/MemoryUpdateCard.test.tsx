import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { installDesktopApiMock } from './desktopApiMock'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ToolCallCard } from '../components/conversation/ToolCallCard'
import { TranscriptThreadContext } from '../components/conversation/transcriptDiffs'
import { useConversationStore } from '../stores/conversationStore'
import { useSubAgentTranscriptStore } from '../stores/subAgentTranscriptStore'
import type { FileDiff } from '../types/toolCall'
import { CORE_TOOL_PRESENTATION_IDS } from '../utils/toolRendererRegistry'
import { withTestCorePresentation } from './testToolPresentation'

const item = withTestCorePresentation({
  id: 'memory-edit-1',
  type: 'toolCall',
  status: 'completed',
  toolName: 'EditFile',
  toolCallId: 'call-1',
  arguments: { path: '.craft/memory/user_role.md', oldText: 'a', newText: 'b' },
  result: 'ok',
  success: true,
  createdAt: '2026-10-13T00:00:00Z'
}, CORE_TOOL_PRESENTATION_IDS.fileWrite, { operation: 'edit', memory: 'update' })

const diff: FileDiff = {
  filePath: '.craft/memory/user_role.md',
  additions: 1,
  deletions: 1,
  diffHunks: [{ oldStart: 1, oldLines: 1, newStart: 1, newLines: 1, lines: [
    { type: 'remove', content: 'a' },
    { type: 'add', content: 'b' }
  ] }],
  status: 'written',
  isNewFile: false
}

describe('memory update card', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
    useSubAgentTranscriptStore.getState().reset()
    installDesktopApiMock({
      settings: { get: async () => ({ locale: 'en' }) },
      appServer: { sendRequest: vi.fn(async () => ({})) }
    })
  })

  it('expands into the memory file diff', () => {
    useConversationStore.setState({ itemDiffs: new Map([[item.id, diff]]) })

    render(<LocaleProvider><ToolCallCard threadId="thread-1" item={item} turnId="turn-1" /></LocaleProvider>)
    fireEvent.click(screen.getByText('Memory updated'))

    expect(screen.getByTestId('inline-diff-view')).toHaveTextContent('user_role.md')
  })

  it('reads a SubAgent transcript diff from its own store', () => {
    useSubAgentTranscriptStore.setState({
      transcripts: new Map([['child-1', {
        status: 'ready',
        turns: [],
        live: null,
        configuration: null,
        workspacePath: null,
        itemDiffs: new Map([[item.id, diff]]),
        turnDiffs: new Map(),
        subscribed: true
      }]])
    })

    render(
      <LocaleProvider>
        <TranscriptThreadContext.Provider value="child-1">
          <ToolCallCard threadId="child-1" item={item} turnId="turn-1" />
        </TranscriptThreadContext.Provider>
      </LocaleProvider>
    )
    fireEvent.click(screen.getByText('Memory updated'))

    expect(screen.getByTestId('inline-diff-view')).toHaveTextContent('user_role.md')
  })
})
