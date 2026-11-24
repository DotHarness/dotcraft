export type CraftStoryChannel =
  | 'armL' | 'wrist' | 'armR' | 'body' | 'eyes' | 'light' | 'glow'
  | 'partTop' | 'partLeft' | 'partRight' | 'core' | 'burst' | 'burstScale'

export const CRAFT_STORY_CHANNELS: Record<CraftStoryChannel, { selector: string; property: 'transform' | 'opacity' }> = {
  armL: { selector: '.dca-part-arm-l-w, .dca-part-arm-l-b, .dca-part-arm-l', property: 'transform' },
  wrist: { selector: '.dca-part-held', property: 'transform' },
  armR: { selector: '.dca-part-arm-r-w, .dca-part-arm-r-b', property: 'transform' },
  body: { selector: '.dca-body-motion', property: 'transform' },
  eyes: { selector: '.dca-face-motion', property: 'transform' },
  light: { selector: '.dca-part-light, .dca-part-glow', property: 'transform' },
  glow: { selector: '.dca-part-glow', property: 'opacity' },
  partTop: { selector: '[data-craft-part="top"]', property: 'transform' },
  partLeft: { selector: '[data-craft-part="left"]', property: 'transform' },
  partRight: { selector: '[data-craft-part="right"]', property: 'transform' },
  core: { selector: '[data-craft-part="core"]', property: 'opacity' },
  burst: { selector: '[data-craft-part="burst"]', property: 'opacity' },
  burstScale: { selector: '[data-craft-part="burst"]', property: 'transform' },
}

const RISE_MS = 240
const INSPECT_END = 2400
const PART_AT = INSPECT_END + 240
const LOCK = 6000
const PRESENT = LOCK + 600
const LOWER = PRESENT + 1800

export const CRAFT_STORY_DURATION = LOWER + RISE_MS + 360

type Key = [number, string]
const keys = Object.fromEntries((Object.keys(CRAFT_STORY_CHANNELS) as CraftStoryChannel[]).map(channel => [channel, [] as Key[]])) as Record<CraftStoryChannel, Key[]>
const at = (channel: CraftStoryChannel, time: number, value: string) => { keys[channel].push([time, value]) }

// Raised arms take the wave's hinge so the shoulder end stays inside the body.
const REST_ARM = 'translate(0px, 0px) rotate(0deg) scaleY(1)'
const raisedL = (angle: number) => 'translate(48px, 88px) rotate(' + angle + 'deg) scaleY(0.8)'
const raisedR = (angle: number) => 'translate(-48px, 88px) rotate(' + -angle + 'deg) scaleY(0.8)'
const REST_WRIST = 'scaleY(1) rotate(0deg)'
const body = (y: number, angle = 0) => 'translateY(' + y + 'px) rotate(' + angle + 'deg)'
const look = (x: number, y: number) => 'translate(' + x + 'px, ' + y + 'px)'

// The wrist undoes the arm's angle and squash, so the item stays upright apart from its own turn.
function hold(time: number, arm: number, item = 0) {
  at('armL', time, raisedL(arm))
  at('wrist', time, 'scaleY(1.25) rotate(' + (item - arm) + 'deg)')
}

function rest(time: number) {
  at('armL', time, REST_ARM)
  at('wrist', time, REST_WRIST)
}

function parts(time: number, open: number) {
  at('partTop', time, look(0, -24 * open))
  at('partLeft', time, look(-22 * open, 13 * open))
  at('partRight', time, look(22 * open, 13 * open))
}

rest(0)
hold(RISE_MS, 84)
hold(1200, 88, -6)
hold(INSPECT_END, 84, 4)
at('eyes', 0, look(0, 0)); at('eyes', 600, look(-14, -10)); at('eyes', INSPECT_END, look(-10, -6))
at('body', 0, body(0)); at('body', 1200, body(2, -1)); at('body', INSPECT_END, body(0, -1))

parts(0, 0); parts(INSPECT_END, 0); parts(PART_AT + 200, 1); parts(LOCK - 60, 1)
at('core', 0, '0'); at('core', INSPECT_END, '0'); at('core', PART_AT + 200, '1'); at('core', LOCK - 60, '1')
at('armR', 0, REST_ARM); at('armR', PART_AT, REST_ARM)
const TAPS = [70, 86, 72, 88, 70, 84, 72, 86, 70, 82]
TAPS.forEach((angle, index) => at('armR', PART_AT + RISE_MS + index * 300, raisedR(angle)))
at('armR', LOCK, raisedR(70))
at('armR', LOCK + RISE_MS, REST_ARM)
hold(3000, 86, -14); hold(3900, 90, 12); hold(4800, 86, -10); hold(LOCK - 300, 84, 0)
at('eyes', 3000, look(-16, -4)); at('eyes', 3900, look(-4, -10)); at('eyes', 4800, look(-16, -8)); at('eyes', LOCK - 300, look(-10, -8))
at('body', 3000, body(1, -2)); at('body', 4800, body(2, -1))

parts(LOCK + 120, 0)
at('core', LOCK + 360, '0')
at('burst', 0, '0'); at('burst', LOCK + 60, '0'); at('burst', LOCK + 140, '1'); at('burst', LOCK + 520, '0')
at('burstScale', 0, 'scale(0.4)'); at('burstScale', LOCK + 60, 'scale(0.4)'); at('burstScale', LOCK + 520, 'scale(1.5)'); at('burstScale', LOCK + 560, 'scale(0.4)')
at('body', LOCK + 120, body(8)); at('body', LOCK + 420, body(-3))

hold(PRESENT, 88); hold(PRESENT + RISE_MS, 116); hold(LOWER, 118, 2)
at('eyes', PRESENT + RISE_MS, look(-8, -18)); at('eyes', LOWER, look(-8, -18))
at('body', PRESENT + RISE_MS, body(-4)); at('body', LOWER, body(-4))
rest(LOWER + RISE_MS)
rest(CRAFT_STORY_DURATION)
parts(CRAFT_STORY_DURATION, 0)
at('core', CRAFT_STORY_DURATION, '0')
at('burst', CRAFT_STORY_DURATION, '0')
at('burstScale', CRAFT_STORY_DURATION, 'scale(0.4)')
at('armR', CRAFT_STORY_DURATION, REST_ARM)
at('body', LOWER + RISE_MS, body(0)); at('body', CRAFT_STORY_DURATION, body(0))
at('eyes', LOWER + RISE_MS, look(0, 0)); at('eyes', CRAFT_STORY_DURATION, look(0, 0))

;[[0, 0], [600, 0.4], [INSPECT_END, 0.2], [PART_AT + 200, 0.5], [LOCK + 120, 1], [LOCK + 600, 0.4], [LOWER, 0.3], [CRAFT_STORY_DURATION, 0]]
  .forEach(([time, value]) => at('glow', time!, String(value)))
;[[0, 1], [600, 1.04], [INSPECT_END, 1], [LOCK + 120, 1.14], [LOCK + 600, 1.04], [LOWER, 1.02], [CRAFT_STORY_DURATION, 1]]
  .forEach(([time, value]) => at('light', time!, 'scale(' + value + ')'))

const SINE = 'cubic-bezier(0.45, 0, 0.55, 1)'
const SNAP = 'cubic-bezier(0.55, 0, 0.35, 1)'
const SNAP_CHANNELS = new Set<CraftStoryChannel>(['armL', 'wrist', 'armR', 'partTop', 'partLeft', 'partRight'])

export function craftStoryKeyframes(channel: CraftStoryChannel): Keyframe[] {
  const property = CRAFT_STORY_CHANNELS[channel].property
  const easing = SNAP_CHANNELS.has(channel) ? SNAP : SINE
  return keys[channel]
    .map((key, index) => ({ key, index }))
    .sort((a, b) => a.key[0] - b.key[0] || a.index - b.index)
    .map(({ key: [time, value] }) => ({ [property]: value, offset: time / CRAFT_STORY_DURATION, easing }))
}
