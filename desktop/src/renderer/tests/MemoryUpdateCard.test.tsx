import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { installDesktopApiMock } from './desktopApiMock'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ToolCallCard } from '../components/conversation/ToolCallCard'
import { useConversationStore } from '../stores/conversationStore'
import { CORE_TOOL_PRESENTATION_IDS } from '../utils/toolRendererRegistry'
import { withTestCorePresentation } from './testToolPresentation'

describe('memory update card', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
    installDesktopApiMock({
      settings: { get: async () => ({ locale: 'en' }) },
      appServer: { sendRequest: vi.fn(async () => ({})) }
    })
  })

  it('expands into the memory file diff', () => {
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
    useConversationStore.setState({
      itemDiffs: new Map([[item.id, {
        filePath: '.craft/memory/user_role.md',
        additions: 1,
        deletions: 1,
        diffHunks: [{ oldStart: 1, oldLines: 1, newStart: 1, newLines: 1, lines: [
          { type: 'remove', content: 'a' },
          { type: 'add', content: 'b' }
        ] }],
        status: 'written',
        isNewFile: false
      }]])
    })

    render(<LocaleProvider><ToolCallCard threadId="thread-1" item={item} turnId="turn-1" /></LocaleProvider>)
    fireEvent.click(screen.getByText('Memory updated'))

    expect(screen.getByTestId('inline-diff-view')).toHaveTextContent('user_role.md')
  })
})
