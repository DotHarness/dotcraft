import { describe, expect, it } from 'vitest'
import type { ConversationTurn } from '../types/conversation'
import { buildComposerHistory } from '../utils/composerHistory'

describe('composer history', () => {
  it('recalls only prompts the person wrote, not a turn started by a background command', () => {
    const turns: ConversationTurn[] = [
      { id: 't1', threadId: 'thread', status: 'completed', items: [{ id: 'u1', type: 'userMessage', status: 'completed', text: 'Run the build' }] },
      {
        id: 't2',
        threadId: 'thread',
        status: 'completed',
        items: [{
          id: 'n1',
          type: 'userMessage',
          status: 'completed',
          deliveryMode: 'queued',
          triggerKind: 'terminal',
          text: 'Background command finished (exit code 0): npm run build'
        }]
      }
    ]

    expect(buildComposerHistory(turns, 'thread').map((entry) => entry.text)).toEqual(['Run the build'])
  })
})
