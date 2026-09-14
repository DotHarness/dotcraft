import { describe, expect, it } from 'vitest'
import type { ConversationTurn } from '../types/conversation'
import { modelReviewCommentsAtLine, modelReviewCommentsForFile, parseModelReviewComments } from '../utils/modelReviewComments'

describe('model review positions', () => {
  it('reads completed responses and anchors the same new-file lines in split and unified views', () => {
    const comment = '::code-comment{title="Issue" body="Fix \\"this\\"" file="src/a.ts" start=3 end=5 priority=2}'
    const turn = { id: 'turn', threadId: 'thread', status: 'completed', items: [
      { id: 'final', type: 'agentMessage', text: comment },
      { id: 'progress', type: 'agentMessage', phase: 'commentary', text: comment }
    ] } as ConversationTurn
    const comments = parseModelReviewComments([turn])
    expect(comments).toHaveLength(1)
    const file = modelReviewCommentsForFile(comments, 'C:\\repo\\src\\a.ts', 'C:/repo')
    expect(modelReviewCommentsAtLine(file, 'left', 3)).toEqual([])
    expect(modelReviewCommentsAtLine(file, 'right', 3)).toEqual(comments)
    expect(modelReviewCommentsAtLine(file, 'right', 6)).toEqual([])
    expect(parseModelReviewComments([{ ...turn, status: 'running' }])).toEqual([])
  })
})
