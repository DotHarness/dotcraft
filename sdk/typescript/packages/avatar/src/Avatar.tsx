import { useEffect, useRef, useState, type CSSProperties } from 'react'
import { AppearanceRig } from './AppearanceRig.js'
import type { AvatarPose, MotionMode, AvatarExpression, AvatarGesture } from './characters.js'
import { deriveAppearance, isHeld, type Appearance } from './appearanceModel.js'

import { SecondaryDecoration } from './Decorations.js'
import { AnimatedDecoration } from './AnimatedDecoration.js'
import { useMotionEnvironment, useOnscreen, useTransitionPause } from './environment.js'
import { useGesture } from './useGesture.js'
import { useEventReplay } from './useEventReplay.js'


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
  const visible = useOnscreen(ref, motion !== 'off' && size > 20)
  const environment = useMotionEnvironment(motion, size > 20)
  useTransitionPause(ref, paused || !visible)
  const [displayed, setDisplayed] = useState(state)
  const [displayedSequence, setDisplayedSequence] = useState(eventSequence)
  const [exiting, setExiting] = useState(false)
  const identity = JSON.stringify(appearance)
  const previousIdentity = useRef(identity)
  const compact = size <= 20
  const animated = environment && !compact
  const held = !compact && isHeld(appearance.secondary)
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
  const classes = [
    'dca-rig',
    !compact && pose === 'waiting' ? 'dca-action-hold-sign' : '',
    !compact && pose === 'working' ? 'dca-action-prop-laptop' : '',
    !compact && pose === 'done' ? 'dca-action-celebrate' : '',
    !compact && pose === 'greeting' && !exiting ? 'dca-action-wave' : '',
  ].filter(Boolean).join(' ')
  return <span ref={ref} className={`dca-robot${className ? ` ${className}` : ''}`} style={{ width: size, height: size, '--dca-loop': '2.6s' } as CSSProperties}
    data-pose={pose} data-mode={motion} data-motion={animated ? 'on' : 'off'} data-paused={paused || !visible} data-exiting={exiting} data-compact={compact}
    data-primary={appearance.primary} data-secondary={appearance.secondary} data-base-face={appearance.baseFace}
    data-gesture={animated ? gesture : undefined} data-held-state={held ? (pose === 'working' || pose === 'waiting' ? 'stowed' : 'holding') : undefined}>
    <svg className="dca-canvas" width={size} height={size} viewBox="0 0 1024 1024" fill="none" role={label ? 'img' : undefined} aria-hidden={label ? undefined : true}
      aria-label={label}>
      <g className="dca-body-motion"><g className={classes}>
        <AppearanceRig appearance={appearance} pose={pose} expression={animated ? shownExpression : expression}
          top={appearance.primary === 'none' ? undefined : <AnimatedDecoration id={appearance.primary} pose={state} sequence={eventSequence} enabled={animated} paused={paused || !visible} />}
          held={!compact && held ? <g data-accessory={appearance.secondary} className="dca-part-held"><SecondaryDecoration id={appearance.secondary} /></g> : undefined}
          accessory={!compact && !held ? <g data-accessory={appearance.secondary}><SecondaryDecoration id={appearance.secondary} /></g> : undefined} />
      </g></g>
    </svg>
  </span>
}

export interface AvatarProps extends Omit<AppearanceAvatarProps, 'appearance'> { name: string }
export function Avatar({ name, ...props }: AvatarProps) { return <AppearanceAvatar appearance={deriveAppearance(name)} {...props} /> }
