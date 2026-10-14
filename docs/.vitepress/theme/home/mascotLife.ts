export type Pose = 'idle' | 'greeting' | 'done'

const GAZE_OFFSET = 34
const SUCCESS_LIGHT = 'var(--dca-success, #16a34a)'
const GAZE_REACH = 420

const blinkFrames: Keyframe[] = [
  { transform: 'scaleY(1)' },
  { transform: 'scaleY(0.08)', offset: 0.35 },
  { transform: 'scaleY(0.08)', offset: 0.62 },
  { transform: 'scaleY(1)' }
]

export function bringToLife(mascot: HTMLElement): () => void {
  for (const robot of mascot.querySelectorAll<HTMLElement>('.dca-robot')) robot.dataset.motion = 'on'

  let blinkTimer = 0
  const scheduleBlink = (): void => {
    blinkTimer = window.setTimeout(() => {
      for (const eyes of mascot.querySelectorAll<SVGElement>('.dca-part-eyes')) eyes.animate(blinkFrames, 180)
      scheduleBlink()
    }, 2600 + Math.random() * 3800)
  }
  scheduleBlink()

  const faces = [...mascot.querySelectorAll<SVGElement>('.dca-face-motion')]
  let gaze = 0
  let target = 0
  let frame = 0
  const follow = (): void => {
    gaze += (target - gaze) * 0.08
    if (Math.abs(target - gaze) < 0.05) gaze = target
    for (const face of faces) face.setAttribute('transform', `translate(${gaze.toFixed(2)} 0)`)
    frame = gaze === target ? 0 : requestAnimationFrame(follow)
  }
  const onPointerMove = (event: PointerEvent): void => {
    const box = mascot.getBoundingClientRect()
    const reach = (event.clientX - (box.left + box.width / 2)) / GAZE_REACH
    target = Math.max(-1, Math.min(1, reach)) * GAZE_OFFSET
    if (!frame) frame = requestAnimationFrame(follow)
  }
  window.addEventListener('pointermove', onPointerMove, { passive: true })

  return () => {
    window.clearTimeout(blinkTimer)
    cancelAnimationFrame(frame)
    window.removeEventListener('pointermove', onPointerMove)
  }
}

export function setPose(mascot: HTMLElement, pose: Pose): void {
  mascot.querySelector<HTMLElement>('.dca-robot')!.dataset.pose = pose
  mascot.querySelector<SVGElement>('.dca-part-robot')!.dataset.expression = pose === 'idle' ? 'neutral' : 'happy'
  const rig = mascot.querySelector('.dca-rig')!.classList
  rig.toggle('dca-action-wave', pose === 'greeting')
  rig.toggle('dca-action-celebrate', pose === 'done')
  for (const light of mascot.querySelectorAll<SVGElement>('.dca-part-glow, .dca-part-light')) {
    light.dataset.fill ??= light.getAttribute('fill') ?? ''
    light.setAttribute('fill', pose === 'done' ? SUCCESS_LIGHT : light.dataset.fill)
  }
}
