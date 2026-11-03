export type WorkStoryChannel = 'armL' | 'armR' | 'body' | 'eyes' | 'light' | 'glow' | 'lines' | 'caret'

export const WORK_STORY_CHANNELS: Record<WorkStoryChannel, { selector: string; property: 'transform' | 'opacity' }> = {
  armL: { selector: '.dca-part-arm-l-w, .dca-part-arm-l-b, .dca-part-arm-l', property: 'transform' },
  armR: { selector: '.dca-part-arm-r-w, .dca-part-arm-r-b', property: 'transform' },
  body: { selector: '.dca-body-motion', property: 'transform' },
  eyes: { selector: '.dca-face-motion', property: 'transform' },
  light: { selector: '.dca-part-light, .dca-part-glow', property: 'transform' },
  glow: { selector: '.dca-part-glow', property: 'opacity' },
  lines: { selector: '.dca-part-laptop-lines', property: 'transform' },
  caret: { selector: '.dca-part-laptop-caret', property: 'opacity' },
}

export type WorkStorySegmentId = 'typing' | 'focused' | 'pause' | 'think' | 'idea' | 'refocus' | 'settle'

export interface WorkStorySegment { id: WorkStorySegmentId; start: number; end: number; kind: 'scene' | 'transition' }

export const WORK_STORY_SEGMENTS: readonly WorkStorySegment[] = [
  { id: 'typing', start: 0, end: 5200, kind: 'scene' },
  { id: 'focused', start: 5200, end: 10400, kind: 'scene' },
  { id: 'pause', start: 10400, end: 11000, kind: 'transition' },
  { id: 'think', start: 11000, end: 16200, kind: 'scene' },
  { id: 'idea', start: 16200, end: 17000, kind: 'transition' },
  { id: 'refocus', start: 17000, end: 19600, kind: 'scene' },
  { id: 'settle', start: 19600, end: 20400, kind: 'transition' },
]

export const WORK_STORY_DURATION = 20400

type Key = [number, string]
const keys = Object.fromEntries((Object.keys(WORK_STORY_CHANNELS) as WorkStoryChannel[]).map(channel => [channel, [] as Key[]])) as Record<WorkStoryChannel, Key[]>
const at = (channel: WorkStoryChannel, time: number, value: string) => { keys[channel].push([Math.min(time, WORK_STORY_DURATION), value]) }

const arm = (x: number, y: number, angle = 0) => 'translate(' + x + 'px, ' + y + 'px) rotate(' + angle + 'deg)'
const body = (y: number, angle = 0, scale = 1) => 'translateY(' + y + 'px) rotate(' + angle + 'deg) scaleY(' + scale + ')'
const look = (x: number, y: number) => 'translate(' + x + 'px, ' + y + 'px)'
const KEYS_L = arm(18, 0)
const KEYS_R = arm(-18, 0)
const REST_L = arm(4, 18, 10)
const REST_R = arm(-4, 18, -10)
const BURST_MS = 2600
const SCROLL_MS = 2200

function typingBursts(start: number, end: number) {
  for (let t = start; t + BURST_MS <= end; t += BURST_MS) {
    ;[0, 0.08, 0.16, 0.24, 0.32, 0.4, 0.48].forEach((offset, index) => at('armL', t + offset * BURST_MS, index % 2 ? arm(18, 12) : KEYS_L))
    at('armR', t, KEYS_R)
    ;[0.04, 0.12, 0.2, 0.28, 0.36, 0.44].forEach((offset, index) => at('armR', t + offset * BURST_MS, index % 2 ? KEYS_R : arm(-18, 12)))
    ;[[0, look(0, 12), body(12)], [0.54, look(0, 12), body(12)], [0.66, look(6, -4), body(8)], [0.86, look(6, -4), body(8)], [1, look(0, 12), body(12)]]
      .forEach(([offset, eyes, pose]) => { at('eyes', t + (offset as number) * BURST_MS, eyes as string); at('body', t + (offset as number) * BURST_MS, pose as string) })
  }
}

function scrollLines(start: number, end: number) {
  for (let t = start; t + SCROLL_MS <= end; t += SCROLL_MS) {
    at('lines', t, 'translateY(0px)'); at('lines', t + 1280, 'translateY(0px)'); at('lines', t + 1450, 'translateY(-36px)')
    at('lines', t + SCROLL_MS, 'translateY(-36px)'); at('lines', t + SCROLL_MS, 'translateY(0px)')
  }
}

function blink(start: number, end: number, period: number) {
  for (let t = start; t < end; t += period) {
    const next = Math.min(end, t + period)
    at('caret', t, '1'); at('caret', t + period / 2, '1'); at('caret', t + period / 2, '0'); at('caret', next, '0'); at('caret', next, '1')
  }
}

at('armL', 0, KEYS_L); at('armL', 5200, KEYS_L)
at('armR', 0, KEYS_R); at('armR', 5200, KEYS_R)
at('eyes', 0, look(0, 8)); at('eyes', 4600, look(0, 8))
;[0, 1300, 2600, 3900].forEach((t, index) => at('body', t, index % 2 ? body(5) : body(12, 2)))
typingBursts(5200, 10400)
at('armL', 10400, KEYS_L); at('armR', 10400, KEYS_R)
at('armL', 11000, REST_L); at('armR', 11000, REST_R)
at('body', 11000, body(4, -3)); at('eyes', 11000, look(-14, -10))
at('armL', 16200, REST_L); at('armR', 16200, REST_R)
;[[12300, 0, -5], [13600, 4, -3], [14900, 0, -5], [16200, 2, -4]].forEach(([t, y, angle]) => at('body', t, body(y, angle)))
at('eyes', 11800, look(-30, -24)); at('eyes', 13400, look(-36, -30)); at('eyes', 14200, look(-34, -30)); at('eyes', 15300, look(28, -26)); at('eyes', 16200, look(20, -22))
;[[0, 0], [10400, 0], [11000, 0], [12000, 0.3], [13200, 0.75], [14400, 0.3], [15600, 0.75], [16200, 0.55]].forEach(([t, value]) => at('glow', t, String(value)))
;[[0, 1], [10400, 1], [12000, 1], [13200, 1.08], [14400, 1], [15600, 1.08], [16200, 1.04]].forEach(([t, value]) => at('light', t, 'scale(' + value + ')'))
at('armL', 16560, KEYS_L); at('armR', 16560, KEYS_R)
at('body', 16450, body(-12, 0, 1.02)); at('body', 16750, body(14, 0, 0.98))
at('eyes', 16450, look(0, -10))
at('light', 16450, 'scale(1.25)'); at('light', 17000, 'scale(1)'); at('light', WORK_STORY_DURATION, 'scale(1)')
at('glow', 16450, '1'); at('glow', 17000, '0'); at('glow', WORK_STORY_DURATION, '0')
typingBursts(17000, 19600)
at('armL', WORK_STORY_DURATION, KEYS_L); at('armR', WORK_STORY_DURATION, KEYS_R)
at('body', WORK_STORY_DURATION, body(12, 2)); at('eyes', 20000, look(0, 8)); at('eyes', WORK_STORY_DURATION, look(0, 8))
scrollLines(0, 10400); at('lines', 10400, 'translateY(0px)'); at('lines', 17000, 'translateY(0px)')
scrollLines(17000, 19200); at('lines', WORK_STORY_DURATION, 'translateY(0px)')
blink(0, 10400, 800); blink(10400, 17000, 1400); blink(17000, WORK_STORY_DURATION, 800)

const SINE = 'cubic-bezier(0.45, 0, 0.55, 1)'

export function workStoryKeyframes(channel: WorkStoryChannel): Keyframe[] {
  const property = WORK_STORY_CHANNELS[channel].property
  return keys[channel]
    .map((key, index) => ({ key, index }))
    .sort((a, b) => a.key[0] - b.key[0] || a.index - b.index)
    .map(({ key: [time, value] }) => ({ [property]: value, offset: time / WORK_STORY_DURATION, easing: channel === 'caret' ? 'steps(1, end)' : SINE }))
}
