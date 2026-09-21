import { useEffect, useRef, useState, type CSSProperties } from 'react'
import { AppearanceRig } from './AppearanceRig.js'
import type { AvatarPose, MotionMode, AvatarExpression, AvatarGesture } from './characters.js'
import { deriveAppearance, type Appearance } from './appearanceModel.js'
import { AnimatedDecoration } from './AnimatedDecoration.js'
import { FaceDecoration, faceplateOf } from './FaceDecorations.js'
import { HandDecoration } from './HandDecorations.js'
import { BackDecoration, BackFrontDecoration, hasFrontPart } from './BackDecorations.js'
import { SkinOverlay, SkinPaintSurface, skinPaint } from './SkinDecorations.js'
import { useMotionEnvironment, useOnscreen, useTransitionPause } from './environment.js'
import { useGesture } from './useGesture.js'
import { useEventReplay } from './useEventReplay.js'

export type SizeTier = 'compact' | 'standard' | 'full'
export function sizeTier(size: number): SizeTier { return size <= 20 ? 'compact' : size < 44 ? 'standard' : 'full' }

export interface AppearanceAvatarProps {
  appearance: Appearance
  state?: AvatarPose
  size?: number
  motion?: MotionMode
  className?: string
  paused?: boolean
  label?: string
  eventSequence?: number
  expression?: AvatarExpression
  gesture?: AvatarGesture
  gestureSequence?: number
  onGestureComplete?: (sequence: number) => void
}
export function AppearanceAvatar({ appearance, state = 'idle', size = 44, motion = 'off', paused = false, label, eventSequence = 0, className, expression, gesture, gestureSequence = 0, onGestureComplete }: AppearanceAvatarProps) {
  const ref = useRef<HTMLSpanElement>(null)
  const tier = sizeTier(size)
  const compact = tier === 'compact'
  const visible = useOnscreen(ref, motion !== 'off' && !compact)
  const environment = useMotionEnvironment(motion, !compact)
  useTransitionPause(ref, paused || !visible)
  const [displayed, setDisplayed] = useState(state)
  const [displayedSequence, setDisplayedSequence] = useState(eventSequence)
  const [exiting, setExiting] = useState(false)
  const identity = JSON.stringify(appearance)
  const previousIdentity = useRef(identity)
  const animated = environment && !compact
  const held = !compact && appearance.hand !== 'none'
  const [shownExpression, setShownExpression] = useState(expression)
  useEffect(() => { if (!animated || (!paused && visible)) setShownExpression(expression) }, [expression, animated, paused, visible])
  useGesture(ref, gesture, gestureSequence, animated, paused || !visible, onGestureComplete)
  useEventReplay(ref, displayedSequence, animated, paused || !visible)

  useEffect(() => {
    if (previousIdentity.current !== identity) {
      previousIdentity.current = identity; setDisplayed(state); setDisplayedSequence(eventSequence); setExiting(false); return
    }
    if (!animated) { setDisplayed(state); setDisplayedSequence(eventSequence); setExiting(false); return }
    if (state === displayed && eventSequence === displayedSequence) { setExiting(false); return }
    if (paused || !visible) return
    const changesWorkProp = displayed !== state && [displayed, state].some(pose => pose === 'working' || pose === 'waiting')
    if (!changesWorkProp) { setDisplayed(state); setDisplayedSequence(eventSequence); setExiting(false); return }
    // Retract props before changing the original paired arm pose; rapid changes cancel this timer.
    setExiting(true)
    const timer = window.setTimeout(() => { setDisplayed(state); setDisplayedSequence(eventSequence); setExiting(false) }, 160)
    return () => window.clearTimeout(timer)
  }, [state, displayed, animated, paused, visible, identity, eventSequence, displayedSequence])

  const pose = animated ? displayed : state
  const effects = compact ? 'off' : tier === 'full' && animated ? 'live' : 'static'
  const classes = [
    'dca-rig',
    !compact && pose === 'waiting' ? 'dca-action-hold-sign' : '',
    !compact && pose === 'working' ? 'dca-action-prop-laptop' : '',
    !compact && pose === 'done' ? 'dca-action-celebrate' : '',
    !compact && pose === 'greeting' && !exiting ? 'dca-action-wave' : '',
  ].filter(Boolean).join(' ')
  const paint = appearance.skin === 'none' ? undefined : skinPaint(appearance.skin)
  const overlay = compact || appearance.skin === 'none' ? undefined
    : paint ? <SkinPaintSurface id={appearance.skin} /> : <SkinOverlay id={appearance.skin} />
  const faceplate = faceplateOf(appearance.face)
  const back = appearance.back !== 'none' ? <g className="dca-part-back" data-back={appearance.back}><BackDecoration id={appearance.back} /></g> : undefined
  const front = appearance.back !== 'none' && hasFrontPart(appearance.back) ? <g className="dca-part-front"><BackFrontDecoration id={appearance.back} /></g> : undefined
  return <span ref={ref} className={`dca-robot${className ? ` ${className}` : ''}`} style={{ width: size, height: size, '--dca-loop': '2.6s' } as CSSProperties}
    data-pose={pose} data-mode={motion} data-motion={animated ? 'on' : 'off'} data-paused={paused || !visible} data-exiting={exiting} data-compact={compact}
    data-size-tier={tier} data-effects={effects}
    data-head={appearance.head} data-face={appearance.face} data-hand={appearance.hand} data-back={appearance.back} data-skin={appearance.skin} data-base-face={appearance.baseFace}
    data-gesture={animated ? gesture : undefined} data-held-state={held ? (pose === 'working' || pose === 'waiting' ? 'stowed' : 'holding') : undefined}>
    <svg className="dca-canvas" width={size} height={size} viewBox="0 0 1024 1024" fill="none" role={label ? 'img' : undefined} aria-hidden={label ? undefined : true}
      aria-label={label}>
      <g className="dca-body-motion"><g className={classes}>
        <AppearanceRig appearance={appearance} pose={pose} expression={animated ? shownExpression : expression}
          top={appearance.head === 'none' ? undefined : <AnimatedDecoration id={appearance.head} pose={state} sequence={eventSequence} enabled={animated} paused={paused || !visible} />}
          held={held && appearance.hand !== 'none' ? <g data-accessory={appearance.hand} className="dca-part-held"><HandDecoration id={appearance.hand} /></g> : undefined}
          accessory={!compact && appearance.face !== 'none' && !faceplate ? <g data-accessory={appearance.face}><FaceDecoration id={appearance.face} /></g> : undefined}
          faceplate={faceplate} back={back} front={front} surface={overlay} paint={paint} />
      </g></g>
    </svg>
  </span>
}

export interface AvatarProps extends Omit<AppearanceAvatarProps, 'appearance'> { name: string }
export function Avatar({ name, ...props }: AvatarProps) { return <AppearanceAvatar appearance={deriveAppearance(name)} {...props} /> }
