export type ConductStoryChannel = 'armL' | 'wrist' | 'armR' | 'body' | 'eyes' | 'light' | 'glow'

export const CONDUCT_STORY_CHANNELS: Record<ConductStoryChannel, { selector: string; property: 'transform' | 'opacity' }> = {
  armL: { selector: '.dca-part-arm-l-w, .dca-part-arm-l-b, .dca-part-arm-l', property: 'transform' },
  wrist: { selector: '.dca-part-held', property: 'transform' },
  armR: { selector: '.dca-part-arm-r-w, .dca-part-arm-r-b', property: 'transform' },
  body: { selector: '.dca-body-motion', property: 'transform' },
  eyes: { selector: '.dca-face-motion', property: 'transform' },
  light: { selector: '.dca-part-light, .dca-part-glow', property: 'transform' },
  glow: { selector: '.dca-part-glow', property: 'opacity' },
}

const BEAT_MS = 600
const BAR_MS = BEAT_MS * 4
const RISE_MS = 240
const LISTEN_END = 3000
const PHRASE = LISTEN_END + RISE_MS
const CRESCENDO = PHRASE + BAR_MS * 3
const FERMATA = CRESCENDO + BAR_MS * 2
const LOWER = FERMATA + BEAT_MS * 3

export const CONDUCT_STORY_DURATION = LOWER + RISE_MS

type Key = [number, string]
const keys = Object.fromEntries((Object.keys(CONDUCT_STORY_CHANNELS) as ConductStoryChannel[]).map(channel => [channel, [] as Key[]])) as Record<ConductStoryChannel, Key[]>
const at = (channel: ConductStoryChannel, time: number, value: string) => { keys[channel].push([time, value]) }

// Raised arms take the wave's hinge so the shoulder end stays inside the body.
const REST_ARM = 'translate(0px, 0px) rotate(0deg) scaleY(1)'
const raisedL = (angle: number) => 'translate(48px, 88px) rotate(' + angle + 'deg) scaleY(0.8)'
const raisedR = (angle: number) => 'translate(-48px, 88px) rotate(' + -angle + 'deg) scaleY(0.8)'
const REST_WRIST = 'scaleY(1) rotate(0deg)'
const body = (y: number, angle = 0) => 'translateY(' + y + 'px) rotate(' + angle + 'deg)'
const look = (x: number, y: number) => 'translate(' + x + 'px, ' + y + 'px)'

// The wrist sets the item's own angle and undoes the arm's squash, so the item keeps its length.
function hold(time: number, arm: number, item: number) {
  at('armL', time, raisedL(arm))
  at('wrist', time, 'scaleY(1.25) rotate(' + (item - arm) + 'deg)')
}

function rest(time: number) {
  at('armL', time, REST_ARM)
  at('wrist', time, REST_WRIST)
}

type Beats = readonly (readonly [number, number])[]
const QUIET: Beats = [[60, -5], [60, 13], [74, -12], [92, 4]]
const WIDE: Beats = [[60, -11], [65, 25], [99, -27], [117, 2]]
const BODY = [body(6), body(2), body(3, -1.5), body(-2)]
const EYES = [look(-4, 8), look(8, 0), look(-16, 0), look(-6, -16)]

function bar(start: number, beats: Beats, lift = 1) {
  beats.forEach(([arm, item], index) => {
    const time = start + index * BEAT_MS
    hold(time, arm, item)
    at('body', time, index === 0 ? body(6 * lift) : BODY[index]!)
    at('eyes', time, EYES[index]!)
  })
}

function cue(start: number, end: number, angle: number) {
  at('armR', start - RISE_MS, REST_ARM)
  at('armR', start, raisedR(angle))
  at('armR', end, raisedR(angle))
  at('armR', end + RISE_MS, REST_ARM)
}

rest(0)
rest(LISTEN_END)
at('armR', 0, REST_ARM)
;[[0, 0], [1200, 3], [2400, 0]].forEach(([time, y]) => at('body', time!, body(y!)))
at('eyes', 0, look(0, 0)); at('eyes', 900, look(-6, -10)); at('eyes', 2400, look(4, -8))

bar(PHRASE, QUIET)
bar(PHRASE + BAR_MS, QUIET)
cue(PHRASE + BAR_MS + BEAT_MS, PHRASE + BAR_MS + BEAT_MS * 3, 76)
bar(PHRASE + BAR_MS * 2, QUIET)
bar(CRESCENDO, WIDE, 1.3)
bar(CRESCENDO + BAR_MS, WIDE, 1.3)
cue(CRESCENDO + BAR_MS, FERMATA + BEAT_MS * 2, 96)

hold(FERMATA, 117, 2)
hold(LOWER, 116, 1)
at('body', FERMATA, body(-3)); at('body', LOWER, body(-4))
at('eyes', FERMATA, look(-6, -16)); at('eyes', LOWER, look(-6, -16))
rest(CONDUCT_STORY_DURATION)
at('armR', CONDUCT_STORY_DURATION, REST_ARM)
at('body', CONDUCT_STORY_DURATION, body(0))
at('eyes', CONDUCT_STORY_DURATION, look(0, 0))

;[[0, 0], [900, 0.45], [2400, 0.2], [CRESCENDO, 0], [CRESCENDO + BAR_MS, 0.45], [FERMATA, 0.85], [LOWER, 0.3], [CONDUCT_STORY_DURATION, 0]]
  .forEach(([time, value]) => at('glow', time!, String(value)))
;[[0, 1], [900, 1.06], [2400, 1], [CRESCENDO, 1], [FERMATA, 1.12], [LOWER, 1.04], [CONDUCT_STORY_DURATION, 1]]
  .forEach(([time, value]) => at('light', time!, 'scale(' + value + ')'))

const SINE = 'cubic-bezier(0.45, 0, 0.55, 1)'
const ICTUS = 'cubic-bezier(0.55, 0, 0.35, 1)'

export function conductStoryKeyframes(channel: ConductStoryChannel): Keyframe[] {
  const property = CONDUCT_STORY_CHANNELS[channel].property
  const easing = channel === 'armL' || channel === 'wrist' ? ICTUS : SINE
  return keys[channel]
    .map((key, index) => ({ key, index }))
    .sort((a, b) => a.key[0] - b.key[0] || a.index - b.index)
    .map(({ key: [time, value] }) => ({ [property]: value, offset: time / CONDUCT_STORY_DURATION, easing }))
}
