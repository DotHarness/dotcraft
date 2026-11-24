import { useEffect, useRef, useState, type CSSProperties } from 'react'
import { AppearanceRig } from './AppearanceRig.js'
import type { AvatarPose, MotionMode, AvatarExpression, AvatarGesture } from './characters.js'
import type { BackId, FaceId, HandId, HeadId, SkinId } from './items.js'
import { isItemId } from './items.js'
import { deriveAppearance, type Appearance } from './appearanceModel.js'
import { fittedMotion } from './decorationMotion.js'
import { AnimatedDecoration } from './AnimatedDecoration.js'
import { CostumeHeadBackDecoration, hasCostumeHeadBack } from './CostumeDecorations.js'
import { FaceDecoration, faceplateOf } from './FaceDecorations.js'
import { HandDecoration } from './HandDecorations.js'
import { BackDecoration, BackFrontDecoration, hasFrontPart } from './BackDecorations.js'
import { SkinOverlay, SkinPaintSurface, skinPaint } from './SkinDecorations.js'
import { materialBounds } from './RigMaterial.js'
import { useAvatarCatalog, useOutfit, type AvatarCatalog } from './pack.js'
import { useMotionEnvironment, useOnscreen, useTransitionPause } from './environment.js'
import { useGesture } from './useGesture.js'
import { useEventReplay } from './useEventReplay.js'
import { useWorkStory } from './useWorkStory.js'
import { usePoseHandoff } from './usePoseHandoff.js'

export type SizeTier = 'compact' | 'standard' | 'full'
export function sizeTier(size: number): SizeTier { return size <= 20 ? 'compact' : size < 44 ? 'standard' : 'full' }

export interface AppearanceAvatarProps {
  appearance: Appearance
  catalog?: AvatarCatalog
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
export function AppearanceAvatar({ appearance: storedAppearance, catalog, state = 'idle', size = 44, motion = 'off', paused = false, label, eventSequence = 0, className, expression, gesture, gestureSequence = 0, onGestureComplete }: AppearanceAvatarProps) {
  const { appearance, catalog: activeCatalog } = useOutfit(storedAppearance, useAvatarCatalog(catalog))
  const { registry } = activeCatalog
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

  const resolveSlot = (slot: 'head' | 'face' | 'hand' | 'back' | 'skin', id: string) =>
    id !== 'none' && registry.get(id)?.slot === slot ? id : 'none'
  const headId = resolveSlot('head', appearance.head)
  const faceId = resolveSlot('face', appearance.face)
  const handId = resolveSlot('hand', appearance.hand)
  const backId = resolveSlot('back', appearance.back)
  const skinId = resolveSlot('skin', appearance.skin)

  const held = !compact && handId !== 'none'
  const heldComponent = held ? activeCatalog.componentOf(handId) : undefined
  const heldStory = heldComponent?.slot === 'hand' ? heldComponent.workStory : undefined
  const stowsHeld = (target: AvatarPose) => (target === 'working' && !heldStory) || target === 'waiting'
  const [shownExpression, setShownExpression] = useState(expression)
  useEffect(() => { if (!animated || (!paused && visible)) setShownExpression(expression) }, [expression, animated, paused, visible])
  useGesture(ref, gesture, gestureSequence, animated, paused || !visible, onGestureComplete)
  useEventReplay(ref, displayedSequence, animated, paused || !visible)
  const captureHandoff = usePoseHandoff(ref, animated && !paused && visible)

  useEffect(() => {
    if (previousIdentity.current !== identity) {
      previousIdentity.current = identity; setDisplayed(state); setDisplayedSequence(eventSequence); setExiting(false); return
    }
    if (!animated) { setDisplayed(state); setDisplayedSequence(eventSequence); setExiting(false); return }
    if (state === displayed && eventSequence === displayedSequence) { if (exiting) { captureHandoff(); setExiting(false) } return }
    if (paused || !visible) return
    const show = () => { captureHandoff(); setDisplayed(state); setDisplayedSequence(eventSequence); setExiting(false) }
    const changesWorkProp = displayed !== state && [displayed, state].some(stowsHeld)
    if (!changesWorkProp) { show(); return }
    // Retract props before changing the paired arm pose, since rapid changes cancel this timer.
    if (!exiting) { captureHandoff(); setExiting(true) }
    const timer = window.setTimeout(show, 160)
    return () => window.clearTimeout(timer)
  }, [state, displayed, animated, paused, visible, identity, eventSequence, displayedSequence, exiting, captureHandoff, heldStory])

  const pose = animated ? displayed : state
  useWorkStory(ref, animated && pose === 'working' && !exiting, paused || !visible, heldStory ?? 'laptop')
  const effects = compact ? 'off' : tier === 'full' && animated ? 'live' : 'static'
  const classes = [
    'dca-rig',
    !compact && pose === 'waiting' ? 'dca-action-hold-sign' : '',
    !compact && pose === 'working' && !heldStory ? 'dca-action-prop-laptop' : '',
    !compact && pose === 'done' ? 'dca-action-celebrate' : '',
    !compact && pose === 'greeting' && !exiting ? 'dca-action-wave' : '',
  ].filter(Boolean).join(' ')

  const customHead = headId === 'none' ? undefined : activeCatalog.componentOf(headId)
  const customFace = faceId === 'none' ? undefined : activeCatalog.componentOf(faceId)
  const customHand = handId === 'none' ? undefined : activeCatalog.componentOf(handId)
  const customBack = backId === 'none' ? undefined : activeCatalog.componentOf(backId)
  const customSkin = skinId === 'none' ? undefined : activeCatalog.componentOf(skinId)

  const paint = skinId === 'none'
    ? undefined
    : customSkin?.slot === 'skin'
      ? (customSkin.kind === 'paint' ? { render: customSkin.renderPaint, shadow: customSkin.paintMaterial.shadow } : undefined)
      : isItemId(skinId) ? skinPaint(skinId as SkinId) : undefined

  const overlay = compact || skinId === 'none'
    ? undefined
    : customSkin?.slot === 'skin'
      ? (customSkin.kind === 'paint'
        ? <g data-skin-overlay={skinId}>{customSkin.renderSurface?.()}<rect className="dca-fx dca-skin-energy" {...materialBounds} fill={customSkin.paintMaterial.accent} opacity="0" /></g>
        : <g data-skin-overlay={skinId}>{customSkin.renderSurface()}</g>)
      : isItemId(skinId)
        ? (paint ? <SkinPaintSurface id={skinId as SkinId} /> : <SkinOverlay id={skinId as SkinId} />)
        : undefined

  const faceplate = customFace?.slot === 'face'
    ? (customFace.kind === 'faceplate' ? { plate: customFace.renderPlate(), eyes: customFace.renderEyes() } : undefined)
    : isItemId(faceId) ? faceplateOf(faceId as FaceId) : undefined

  const accessory = !compact && faceId !== 'none' && !faceplate
    ? <g data-accessory={faceId}>{customFace?.slot === 'face' && customFace.kind === 'accessory' ? customFace.render() : isItemId(faceId) ? <FaceDecoration id={faceId as FaceId} /> : null}</g>
    : undefined

  const top = headId === 'none'
    ? undefined
    : customHead?.slot === 'head'
      ? <AnimatedDecoration id={headId} pose={state} sequence={eventSequence} enabled={animated} paused={paused || !visible} profile={customHead.motion}>
          <g data-decoration={headId}>{customHead.render()}</g>
        </AnimatedDecoration>
      : isItemId(headId)
        ? <AnimatedDecoration id={headId} pose={state} sequence={eventSequence} enabled={animated} paused={paused || !visible} />
        : undefined

  const headBack = headId === 'none'
    ? undefined
    : customHead?.slot === 'head' && customHead.renderBack
      ? <AnimatedDecoration id={`${headId}:back`} pose={state} sequence={eventSequence} enabled={animated} paused={paused || !visible}
          profile={{ ...(customHead.motion ?? fittedMotion()), height: 0, shadow: 0 }} showShadow={false}>
          <g data-head-back={headId}>{customHead.renderBack()}</g>
        </AnimatedDecoration>
      : isItemId(headId) && hasCostumeHeadBack(headId)
        ? <AnimatedDecoration id={`${headId}:back`} pose={state} sequence={eventSequence} enabled={animated} paused={paused || !visible}
            profile={fittedMotion()} showShadow={false}>
            <g data-head-back={headId}><CostumeHeadBackDecoration id={headId as HeadId} /></g>
          </AnimatedDecoration>
        : undefined

  const heldNode = held && handId !== 'none'
    ? <g data-accessory={handId} className="dca-part-held">
        {customHand?.slot === 'hand' ? <g data-held-decoration={handId}>{customHand.render()}</g> : isItemId(handId) ? <HandDecoration id={handId as HandId} /> : null}
      </g>
    : undefined

  const back = backId !== 'none'
    ? <g className="dca-part-back" data-back={backId}>
        {customBack?.slot === 'back' ? customBack.renderBack() : isItemId(backId) ? <BackDecoration id={backId as BackId} /> : null}
      </g>
    : undefined

  const front = backId !== 'none'
    ? customBack?.slot === 'back' && customBack.renderFront
      ? <g className="dca-part-front"><g data-back-front={backId}>{customBack.renderFront()}</g></g>
      : isItemId(backId) && hasFrontPart(backId as BackId)
        ? <g className="dca-part-front"><BackFrontDecoration id={backId as BackId} /></g>
        : undefined
    : undefined

  return <span ref={ref} className={`dca-robot${className ? ` ${className}` : ''}`} style={{ width: size, height: size, '--dca-loop': '2.6s' } as CSSProperties}
    data-pose={pose} data-mode={motion} data-motion={animated ? 'on' : 'off'} data-paused={paused || !visible} data-exiting={exiting} data-compact={compact}
    data-size-tier={tier} data-effects={effects}
    data-head={headId} data-face={faceId} data-hand={handId} data-back={backId} data-skin={skinId} data-base-face={appearance.baseFace}
    data-gesture={animated ? gesture : undefined} data-held-state={held ? (stowsHeld(pose) ? 'stowed' : 'holding') : undefined}>
    <svg className="dca-canvas" width={size} height={size} viewBox="0 0 1024 1024" fill="none" role={label ? 'img' : undefined} aria-hidden={label ? undefined : true}
      aria-label={label}>
      <g className="dca-body-motion"><g className={classes}>
        <AppearanceRig appearance={appearance} pose={pose} expression={animated ? shownExpression : expression}
          top={top} headBack={headBack} held={heldNode} accessory={accessory}
          faceplate={faceplate} back={back} front={front} surface={overlay} paint={paint} paletteRegistry={registry} />
      </g></g>
    </svg>
  </span>
}

export interface AvatarProps extends Omit<AppearanceAvatarProps, 'appearance'> { name: string }
export function Avatar({ name, catalog, ...props }: AvatarProps) {
  const activeCatalog = useAvatarCatalog(catalog)
  return <AppearanceAvatar appearance={deriveAppearance(name, activeCatalog.registry)} catalog={activeCatalog} {...props} />
}
