import { useEffect, useRef } from 'react'
import type { PrimaryId } from './appearanceModel.js'
import type { AvatarPose } from './characters.js'
import { PrimaryDecoration } from './Decorations.js'
import { advanceDecoration, decorationMotionProfiles, frameTransform, requestDecorationEvent, restFrame, type DecorationClock, type DecorationFrame } from './decorationMotion.js'

export function AnimatedDecoration({ id, pose, sequence, enabled, paused }: {
  id: Exclude<PrimaryId, 'none'>; pose: AvatarPose; sequence: number; enabled: boolean; paused: boolean
}) {
  const art = useRef<SVGGElement>(null)
  const shadow = useRef<SVGEllipseElement>(null)
  const clock = useRef<DecorationClock | null>(null)
  const profile = decorationMotionProfiles[id]
  const previousId = useRef(id)
  function paint(frame: DecorationFrame) {
    art.current?.setAttribute('transform', frameTransform(profile, frame))
    shadow.current?.setAttribute('opacity', String(.12 * frame.shadow))
    shadow.current?.setAttribute('rx', String(profile.shadow * (.7 + .3 * frame.shadow)))
  }
  useEffect(() => {
    if (previousId.current !== id || !enabled) { clock.current = null; previousId.current = id }
    clock.current = enabled ? requestDecorationEvent(clock.current, pose) : null
    paint(clock.current?.frame ?? restFrame)
  }, [id, pose, sequence, enabled])
  useEffect(() => {
    if (!enabled || paused) return
    let handle = 0
    let last: number | undefined
    const tick = (now: number) => {
      if (!clock.current) return
      clock.current = advanceDecoration(clock.current, profile, last === undefined ? 0 : now - last)
      last = now
      paint(clock.current.frame)
      if (clock.current.phase !== 'rest') handle = requestAnimationFrame(tick)
    }
    handle = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(handle)
  }, [id, pose, sequence, enabled, paused])
  return <g data-decoration-motion={profile.kind}>
    {profile.shadow > 0 && <ellipse ref={shadow} cx={profile.x} cy="404" rx={profile.shadow} ry="7" fill="#243344" opacity=".12" />}
    <g ref={art} transform={frameTransform(profile, restFrame)}><PrimaryDecoration id={id} /></g>
  </g>
}
