import { describe, expect, it, vi } from 'vitest'
import type { ConversationTurn } from '../types/conversation'
import { collectConversationVisualizations, galleryForVisualization } from '../components/conversation/visualizationGallery'
import { reloadVisualizationsReferencedBy, subscribeVisualizationReload } from '../components/conversation/visualizationReloads'

const FILE = 'Sales_报告 v2.HTML'
const reference = (path: string): string => `visualize${JSON.stringify({ path })}`

describe('visualization filename identity', () => {
  it('opens the matching fullscreen gallery entry without folding filename case', () => {
    const turns: ConversationTurn[] = [{
      id: 'turn',
      threadId: 'thread',
      status: 'completed',
      startedAt: '2026-01-01T00:00:00Z',
      items: [{
        id: 'message',
        type: 'agentMessage',
        status: 'completed',
        createdAt: '2026-01-01T00:00:00Z',
        text: [reference(FILE.toLowerCase()), reference(FILE)].join('\n')
      }]
    }]
    const entries = collectConversationVisualizations(turns)
    const entry = { threadId: 'thread', turnId: 'turn', itemId: 'message', path: FILE }
    const gallery = galleryForVisualization(entry, () => entries)

    expect(gallery.entries.map(item => item.path)).toEqual([FILE.toLowerCase(), FILE])
    expect(gallery.index).toBe(1)
  })

  it('reloads copies of the exact file in the same thread once per message', () => {
    const inline = vi.fn()
    const fullscreen = vi.fn()
    const differentCase = vi.fn()
    const otherThread = vi.fn()
    const unsubscribe = [
      subscribeVisualizationReload('thread', FILE, inline),
      subscribeVisualizationReload('thread', FILE, fullscreen),
      subscribeVisualizationReload('thread', FILE.toLowerCase(), differentCase),
      subscribeVisualizationReload('other-thread', FILE, otherThread)
    ]
    try {
      reloadVisualizationsReferencedBy('thread', 'updated-message', [reference(FILE), reference(FILE)].join('\n'))

      expect(inline.mock.calls).toEqual([['updated-message']])
      expect(fullscreen.mock.calls).toEqual([['updated-message']])
      expect(differentCase).not.toHaveBeenCalled()
      expect(otherThread).not.toHaveBeenCalled()
    } finally {
      unsubscribe.forEach(stop => stop())
    }
  })
})
