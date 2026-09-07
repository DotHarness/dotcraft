import test from 'node:test'
import assert from 'node:assert/strict'
import { primaryIds } from '../dist/appearanceModel.js'
import { advanceDecoration, decorationFrame, decorationMotionProfiles, eventDuration, frameTransform, requestDecorationEvent, restFrame } from '../dist/decorationMotion.js'

test('all 20 stable primary IDs have explicit contact and motion profiles', () => {
  assert.deepEqual(Object.keys(decorationMotionProfiles).sort(), primaryIds.filter(id => id !== 'none').sort())
  const counts = Object.values(decorationMotionProfiles).reduce((result, profile) => ({ ...result, [profile.kind]: (result[profile.kind] ?? 0) + 1 }), {})
  assert.deepEqual(counts, { lift: 9, fitted: 3, bounce: 3, rock: 3, squish: 1, sprout: 1 })
  assert.equal(decorationMotionProfiles['traffic-cone'].angle, decorationMotionProfiles['paper-boat'].angle / 2)
})
test('every event starts and lands at its exact mounting pose, with bounded movement', () => {
  for (const profile of Object.values(decorationMotionProfiles)) {
    for (const pose of ['acknowledge', 'done', 'greeting', 'blocked']) {
      assert.deepEqual(decorationFrame(profile, pose, 0), restFrame)
      assert.deepEqual(decorationFrame(profile, pose, 1), restFrame)
      for (let i = 0; i <= 100; i++) {
        const frame = decorationFrame(profile, pose, i / 100)
        assert.ok(Math.abs(frame.angle) <= profile.angle + .001)
        assert.ok(frame.sy >= .97 && frame.sy <= 1.03)
        assert.ok(frame.shadow >= .55 && frame.shadow <= 1)
        assert.ok(frame.y <= 0 && frame.y >= -.06 * 1024 / 1.3 - .001)
        if (!profile.height) assert.equal(frame.y, 0)
        if (profile.kind === 'fitted') assert.equal(frame.sy, 1, 'hard/fitted hats must not deform')
      }
    }
  }
})
test('ordinary work states stay attached; greeting and receipt are smaller than completion', () => {
  const profile = decorationMotionProfiles['rubber-duck']
  for (const pose of ['idle', 'thinking', 'working', 'waiting']) {
    assert.equal(eventDuration(pose), 0)
    assert.deepEqual(decorationFrame(profile, pose, .4), restFrame)
  }
  const done = decorationFrame(profile, 'done', .4)
  assert.ok(Math.abs(decorationFrame(profile, 'acknowledge', .4).y - done.y / 3) < 1e-10)
  assert.ok(Math.abs(decorationFrame(profile, 'greeting', .4).y - done.y * .65) < 1e-10)
  assert.equal(eventDuration('acknowledge'), 350)
  assert.equal(eventDuration('greeting'), 600)
  assert.equal(eventDuration('done'), 800)
  assert.equal(eventDuration('blocked'), 250)
})
test('pause freezes the actual airborne frame; resume consumes only active time', () => {
  const profile = decorationMotionProfiles['top-hat']
  let clock = advanceDecoration(requestDecorationEvent(null, 'done'), profile, 320)
  assert.ok(clock.frame.y < 0)
  const paused = advanceDecoration(clock, profile, 10000, true)
  assert.equal(paused, clock)
  clock = advanceDecoration(paused, profile, 480)
  assert.equal(clock.phase, 'rest')
  assert.deepEqual(clock.frame, restFrame)
})
test('interruptions settle within 160ms and only the most recent event survives', () => {
  const profile = decorationMotionProfiles['rubber-duck']
  const airborne = advanceDecoration(requestDecorationEvent(null, 'done'), profile, 320)
  let next = requestDecorationEvent(airborne, 'blocked')
  assert.deepEqual(next.frame, airborne.frame, 'request must not snap to rest')
  next = advanceDecoration(next, profile, 80)
  assert.ok(next.frame.y > airborne.frame.y && next.frame.y < 0)
  next = requestDecorationEvent(next, 'waiting')
  next = advanceDecoration(next, profile, 160)
  assert.equal(next.phase, 'rest')
  assert.deepEqual(next.frame, restFrame)
  assert.equal(next.pose, 'waiting')
  const replay = requestDecorationEvent(next, 'done')
  assert.equal(replay.phase, 'event')
  assert.ok(advanceDecoration(replay, profile, 320).frame.y < 0)
})
test('anchored transforms use the configured contact point, without moving arm layers', () => {
  const profile = decorationMotionProfiles.sprout
  const frame = decorationFrame(profile, 'done', .25)
  assert.match(frameTransform(profile, frame), /translate\(512 399\).*translate\(-512 -399\)/)
  assert.equal(frame.x, 0)
  assert.equal(frame.y, 0)
})
