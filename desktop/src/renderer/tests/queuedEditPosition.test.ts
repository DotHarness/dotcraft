import { describe, expect, it } from 'vitest'
import { placeAtQueuedEditPosition } from '../components/conversation/useQueuedInputEditing'

describe('placeAtQueuedEditPosition', () => {
  it('falls back to the next neighbor and keeps the order when both neighbors are gone', () => {
    expect(placeAtQueuedEditPosition(['a', 'c', 'x'], 'x', { previousId: 'gone', nextId: 'c' })).toEqual(['a', 'x', 'c'])
    expect(placeAtQueuedEditPosition(['a', 'x'], 'x', { previousId: 'gone', nextId: 'gone' })).toEqual(['a', 'x'])
  })
})
