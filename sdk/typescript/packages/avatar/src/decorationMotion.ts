import type { PrimaryId } from './appearanceModel.js'
import type { AvatarPose } from './characters.js'

export type MotionKind = 'lift' | 'fitted' | 'bounce' | 'rock' | 'squish' | 'sprout'
export interface MotionProfile { kind: MotionKind; x: number; y: number; height: number; angle: number; shadow: number }
const lift = (x = 512, y = 400): MotionProfile => ({ kind: 'lift', x, y, height: .05, angle: 3, shadow: 95 })
const fitted = (x = 512): MotionProfile => ({ kind: 'fitted', x, y: 400, height: 0, angle: 3, shadow: 0 })
const bounce = (x = 512): MotionProfile => ({ kind: 'bounce', x, y: 400, height: .06, angle: 5, shadow: 64 })
export const decorationMotionProfiles: Record<Exclude<PrimaryId, 'none'>, MotionProfile> = {
  'baseball-cap': lift(478), 'bucket-hat': lift(), beret: lift(494), beanie: fitted(),
  'top-hat': lift(), 'wizard-hat': lift(), 'chef-hat': lift(), 'party-hat': lift(), crown: lift(),
  'hard-hat': fitted(), nightcap: fitted(493), 'straw-hat': lift(),
  poop: bounce(), 'rubber-duck': bounce(520), donut: { ...bounce(), shadow: 40 },
  banana: { kind: 'rock', x: 487, y: 402, height: 0, angle: 5, shadow: 0 },
  'paper-boat': { kind: 'rock', x: 512, y: 399, height: 0, angle: 5, shadow: 0 },
  'traffic-cone': { kind: 'rock', x: 512, y: 399, height: 0, angle: 2.5, shadow: 0 },
  'fried-egg': { kind: 'squish', x: 512, y: 400, height: 0, angle: 0, shadow: 0 },
  sprout: { kind: 'sprout', x: 512, y: 399, height: 0, angle: 6, shadow: 0 },
}
export const motionKindLabels: Record<MotionKind, string> = {
  lift: 'Gentle lift', fitted: 'Fitted sway', bounce: 'Bounce and settle', rock: 'Rocking response', squish: 'Soft wobble', sprout: 'Nod and unfurl',
}
export interface DecorationFrame { x: number; y: number; angle: number; sy: number; shadow: number }
export const restFrame: DecorationFrame = { x: 0, y: 0, angle: 0, sy: 1, shadow: 1 }
export function eventDuration(pose: AvatarPose) {
  return pose === 'done' ? 800 : pose === 'greeting' ? 600 : pose === 'acknowledge' ? 350 : pose === 'blocked' ? 250 : 0
}
const mix = (a: number, b: number, t: number) => a + (b - a) * t
export function mixFrame(a: DecorationFrame, b: DecorationFrame, t: number): DecorationFrame {
  return { x: mix(a.x, b.x, t), y: mix(a.y, b.y, t), angle: mix(a.angle, b.angle, t), sy: mix(a.sy, b.sy, t), shadow: mix(a.shadow, b.shadow, t) }
}
export function decorationFrame(profile: MotionProfile, pose: AvatarPose, progress: number): DecorationFrame {
  const t = Math.max(0, Math.min(1, progress))
  if (!eventDuration(pose) || t === 0 || t === 1) return { ...restFrame }
  // Convert a fraction of displayed avatar height into the production rig's 1.3x brand coordinates.
  if (pose === 'blocked') return { ...restFrame, x: Math.sin(t * Math.PI * 6) * (1 - t) * 1024 / 1.3 * .008 }
  const strength = pose === 'acknowledge' ? 1 / 3 : pose === 'greeting' ? .65 : 1
  const sway = Math.sin(t * Math.PI * 2) * Math.sin(t * Math.PI) * strength
  if (profile.kind === 'squish') return { ...restFrame, sy: 1 - .03 * Math.sin(t * Math.PI * 2) * Math.sin(t * Math.PI) * strength }
  if (!profile.height) return { ...restFrame, angle: profile.angle * sway }
  let liftAmount: number
  if (t < .38) liftAmount = 1 - (1 - t / .38) ** 3
  else if (t < .46) liftAmount = 1
  else if (t < .82) liftAmount = 1 - ((t - .46) / .36) ** 2
  else liftAmount = profile.kind === 'bounce' ? Math.sin((t - .82) / .18 * Math.PI) * .08 : 0
  return { ...restFrame, y: -liftAmount * profile.height * 1024 / 1.3 * strength,
    angle: profile.angle * sway, shadow: 1 - liftAmount * .45 * strength }
}
export interface DecorationClock { phase: 'return' | 'event' | 'rest'; elapsed: number; pose: AvatarPose; from: DecorationFrame; frame: DecorationFrame }
export function requestDecorationEvent(current: DecorationClock | null, pose: AvatarPose): DecorationClock {
  const from = current?.frame ?? restFrame
  const displaced = Math.abs(from.x) + Math.abs(from.y) + Math.abs(from.angle) + Math.abs(from.sy - 1) > .0001
  return { phase: displaced ? 'return' : eventDuration(pose) ? 'event' : 'rest', elapsed: 0, pose, from, frame: { ...from } }
}
export function advanceDecoration(clock: DecorationClock, profile: MotionProfile, delta: number, paused = false): DecorationClock {
  if (paused || clock.phase === 'rest') return clock
  let elapsed = clock.elapsed + Math.max(0, delta)
  if (clock.phase === 'return') {
    if (elapsed < 160) return { ...clock, elapsed, frame: mixFrame(clock.from, restFrame, 1 - (1 - elapsed / 160) ** 3) }
    elapsed -= 160
  }
  const duration = eventDuration(clock.pose)
  if (!duration || elapsed >= duration) return { ...clock, phase: 'rest', elapsed: 0, frame: { ...restFrame } }
  return { ...clock, phase: 'event', elapsed, frame: decorationFrame(profile, clock.pose, elapsed / duration) }
}
export function frameTransform(profile: MotionProfile, frame: DecorationFrame) {
  return `translate(${frame.x} ${frame.y}) translate(${profile.x} ${profile.y}) rotate(${frame.angle}) scale(1 ${frame.sy}) translate(${-profile.x} ${-profile.y})`
}
