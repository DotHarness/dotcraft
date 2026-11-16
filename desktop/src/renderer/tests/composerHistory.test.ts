import { describe, expect, it, vi } from 'vitest'
import type { ConversationTurn, QueuedTurnInput } from '../types/conversation'
import { buildComposerHistory, queuedInputToComposerDraft } from '../utils/composerHistory'

describe('composer history', () => {
  it('recalls only prompts the person wrote, not a turn started by a background task', () => {
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
      },
      {
        id: 't3',
        threadId: 'thread',
        status: 'completed',
        items: [{
          id: 'n2',
          type: 'userMessage',
          status: 'completed',
          deliveryMode: 'queued',
          triggerKind: 'subagent',
          triggerRefId: 'agent-1',
          text: 'Subagent reviewer finished'
        }]
      }
    ]

    expect(buildComposerHistory(turns, 'thread').map((entry) => entry.text)).toEqual(['Run the build'])
  })
  it('keeps actual host paths when queued previews are unavailable rather than replacing them with display labels', async () => {
    vi.stubGlobal('window', { api: { workspace: { readImageAsDataUrl: async () => { throw new Error('offline') } } } })
    try {
      const item = { threadId: 'history-owner', nativeInputParts: [
        { type: 'fileRef', path: '/host/actual/report.txt', displayPath: 'report.txt' },
        { type: 'localImage', path: '/host/actual/image.png', fileName: 'image.png', mimeType: 'image/png' }
      ] } as QueuedTurnInput
      const draft = await queuedInputToComposerDraft(item)
      expect(draft.segments).toEqual([{ type: 'file', relativePath: '/host/actual/report.txt' }])
      expect(draft.images).toEqual([{ tempPath: '/host/actual/image.png', fileName: 'image.png', mimeType: 'image/png', dataUrl: '', source: 'host' }])
    } finally { vi.unstubAllGlobals() }
  })

})
