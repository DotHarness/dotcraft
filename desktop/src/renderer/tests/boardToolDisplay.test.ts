import { describe, expect, it } from 'vitest'
import type { ConversationItem } from '../types/conversation'
import { planToolRunRender } from '../utils/toolCallAggregation'
import { parseBoardCall, type BoardOperation } from '../utils/boardToolDisplay'
import { isToolExecutionFailure } from '../utils/toolCallDisplay'
import { CORE_TOOL_PRESENTATION_IDS } from '../utils/toolRendererRegistry'
import { withTestCorePresentation } from './testToolPresentation'

const TOPIC = 'topic-1'

function preview(messageId: string, author: string, text: string) {
  return { messageId, channelName: 'release', author, threadId: TOPIC, createdAt: '2026-10-13T08:12:00Z', textPreview: text, nChars: text.length, truncated: false }
}

function boardItem(id: string, operation: BoardOperation, result: unknown = {}): ConversationItem {
  return withTestCorePresentation({
    id,
    type: 'toolCall',
    status: 'completed',
    toolName: `Board${operation}`,
    arguments: {},
    result: JSON.stringify(result),
    success: true
  }, CORE_TOOL_PRESENTATION_IDS.board, { operation })
}

function readSlice(text: string, nextOffsetChars: number) {
  return parseBoardCall({
    operation: 'readPost',
    args: {},
    result: JSON.stringify({ messageId: 'post', author: '/root', text, nChars: 10, nextOffsetChars }),
    running: false
  }).post
}

describe('board tool display', () => {
  it('reads a topic oldest first, root post leading', () => {
    const call = parseBoardCall({
      operation: 'readThread',
      args: { threadId: TOPIC },
      result: JSON.stringify({
        rootPost: preview(TOPIC, '/root', 'Release review'),
        results: [preview('reply-2', '/root/tests', 'Tests pass'), preview('reply-1', '/root/docs', 'Docs block')]
      }),
      running: false
    })

    expect(call.posts.map((post) => post.messageId)).toEqual([TOPIC, 'reply-1', 'reply-2'])
    expect(call.posts[0].topic).toBe(true)
  })

  it('treats a board error result as a failed call', () => {
    const call = parseBoardCall({
      operation: 'readPost',
      args: { messageId: 'missing' },
      result: JSON.stringify({ error: { code: 'board_not_found', message: 'Not found in this team.' } }),
      running: false
    })

    expect(call.phase).toBe('failed')
    expect(call.errorMessage).toBe('Not found in this team.')
    expect(call.post).toBeNull()
  })

  it('counts only the returned slice as shown when a post is read from an offset', () => {
    expect(readSlice('efgh', 8)?.hiddenChars).toBe(6)
    expect(readSlice('ij', 10)?.hiddenChars).toBe(8)
  })

  it('reports a board error result as a failed tool so a folded read group shows it', () => {
    const failed = boardItem('failed', 'readThread', { error: { code: 'board_not_found', message: 'Not found in this team.' } })

    expect(isToolExecutionFailure(failed)).toBe(true)
    expect(isToolExecutionFailure(boardItem('read', 'readThread'))).toBe(false)
  })

  it('folds board reads, posts, and subscriptions into one group', () => {
    const entries = planToolRunRender([
      boardItem('post', 'post'),
      boardItem('thread', 'readThread'),
      boardItem('read', 'readPost'),
      boardItem('subscribe', 'subscribe')
    ], { isRunning: false, isTrailingRun: false })

    expect(entries.map((entry) => ('items' in entry ? `${entry.kind}:${entry.items.length}` : entry.item.id)))
      .toEqual(['group:4'])
  })
})
