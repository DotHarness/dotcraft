import test from 'node:test'
import assert from 'node:assert/strict'
import { WORK_STORY_CHANNELS, workStoryKeyframes } from '../dist/workStory.js'
import { CONDUCT_STORY_CHANNELS, conductStoryKeyframes } from '../dist/conductStory.js'
import { CRAFT_STORY_CHANNELS, craftStoryKeyframes } from '../dist/craftStory.js'

for (const [name, channels, keyframes] of [['laptop', WORK_STORY_CHANNELS, workStoryKeyframes], ['conduct', CONDUCT_STORY_CHANNELS, conductStoryKeyframes], ['craft', CRAFT_STORY_CHANNELS, craftStoryKeyframes]]) test(`every ${name} story channel spans the loop in order and closes without a seam`, () => {
  for (const [channel, { property }] of Object.entries(channels)) {
    const frames = keyframes(channel)
    const offsets = frames.map(frame => frame.offset)
    assert.equal(offsets[0], 0, channel)
    assert.equal(offsets.at(-1), 1, channel)
    offsets.slice(1).forEach((offset, index) => assert.ok(offset >= offsets[index] && offset <= 1, channel))
    assert.equal(frames.at(-1)[property], frames[0][property], channel)
  }
})
