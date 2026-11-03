import test from 'node:test'
import assert from 'node:assert/strict'
import { WORK_STORY_CHANNELS, workStoryKeyframes } from '../dist/workStory.js'

test('every work story channel spans the loop in order and closes without a seam', () => {
  for (const [channel, { property }] of Object.entries(WORK_STORY_CHANNELS)) {
    const frames = workStoryKeyframes(channel)
    const offsets = frames.map(frame => frame.offset)
    assert.equal(offsets[0], 0, channel)
    assert.equal(offsets.at(-1), 1, channel)
    offsets.slice(1).forEach((offset, index) => assert.ok(offset >= offsets[index] && offset <= 1, channel))
    assert.equal(frames.at(-1)[property], frames[0][property], channel)
  }
})
