import { useCallback, useEffect, useLayoutEffect, useRef, useState, type AnimationEvent, type CSSProperties } from 'react'
import { Avatar } from '../Avatar.js'
import { deriveAppearance, mascotPaletteOf, type AvatarPose } from '../index.js'
import { useComposerAvatarBehavior } from './useComposerAvatarBehavior.js'
import { consumeMascotHandoff, recordMascotHandoff } from './mascotHandoff.js'
import { useComposerProfile } from './useComposerProfile.js'
import { useComposerMotion } from './useComposerMotion.js'
import { MASCOT_SIZE, MASCOT_SCALE, MASCOT_HIDDEN_RATIO, MASCOT_RAISE, MASCOT_SLEEP_AFTER_MS, MASCOT_WAVE_DURATION_MS, MASCOT_ACTIVE_IDLE_MIN_MS, MASCOT_ACTIVE_IDLE_JITTER_MS, MASCOT_ACTIVE_IDLE_ACTIVITY_THROTTLE_MS, MASCOT_ACTIVE_IDLE_TRAVEL_MS, MASCOT_ACTIVE_IDLE_HOLD_MS, MASCOT_SPARKLES, pickMascotActiveIdle, type MascotActiveIdleState, type MascotActiveIdleMotion } from './constants.js'
import type { ComposerMascotProps, ComposerMascotContext, MascotExpression, MascotLight } from './types.js'
export function ComposerMascot({ name, motion = 'system', focused = false, dragOver = false, bounceSignal = 0, interaction, reasoningEffort = 'off', speed = 'standard', contextMax = false, anchorOffset = 0, anchorPushSignal = 0, handoff = false, renderCharacter, renderMenu, onNameRendered }: ComposerMascotProps) {
  const reduced = !useComposerMotion(motion)
  const { avatar, profileTransition, profileTransitionRevision } = useComposerProfile(name, reduced)
  useEffect(() => { onNameRendered?.(avatar) }, [avatar, onNameRendered])
  const [menuPos, setMenuPos] = useState<{ x: number; y: number } | null>(null)
  const [ambientSleeping, setSleeping] = useState(false)
  const sleeping = ambientSleeping || interaction?.expression === 'sleep'
  const [waving, setWaving] = useState(false)
  const [greetingSequence, setGreetingSequence] = useState(0)
  const [startled, setStartled] = useState(false)
  const [launching, setLaunching] = useState(false)
  const [cheering, setCheering] = useState(false)
  const [sparkling, setSparkling] = useState(false)
  const [shaking, setShaking] = useState(false)
  const [nodding, setNodding] = useState(false)
  const [landing, setLanding] = useState(false)
  const [pushLift, setPushLift] = useState(false)
  const [activeIdle, setActiveIdle] = useState<MascotActiveIdleState | null>(null)
  const [activityRevision, setActivityRevision] = useState(0)
  const rootRef = useRef<HTMLDivElement | null>(null)
  const lastActivityRef = useRef(0)
  const lastActiveIdleRef = useRef<MascotActiveIdleMotion | null>(null)

  const baseExpression: MascotExpression =
    interaction?.expression ?? (dragOver ? 'operator' : focused ? 'happy' : 'neutral')
  const light: MascotLight = interaction?.light ?? 'default'
  const hasMenu = renderMenu != null
  const bubble = interaction?.bubble ?? null
  const holdSign = interaction?.hold === 'sign'

  const laptopActive =
    !sleeping &&
    !dragOver &&
    !holdSign &&
    bubble == null &&
    baseExpression === 'operator' &&
    light === 'default'
  const expression: MascotExpression = sleeping ? 'sleep' : waving ? 'happy' : baseExpression
  const semanticAvatarPose: AvatarPose = light === 'error'
    ? 'blocked'
    : light === 'success'
      ? 'done'
      : sleeping
        ? 'sleep'
        : waving
          ? 'greeting'
          : holdSign || bubble != null
            ? 'waiting'
            : laptopActive
              ? 'working'
              : 'idle'
  const avatarBehavior = useComposerAvatarBehavior({
    semanticPose: semanticAvatarPose,
    baseExpression,
    focused,
    dragOver,
    sleeping,
    waving,
    activeIdle: activeIdle != null,
    bounceSignal,
    reducedMotion: reduced
  })
  const mascotPalette = mascotPaletteOf(deriveAppearance(avatar ?? ''))
  const activity: ComposerMascotContext["activity"] = light === 'error'
    ? 'error'
    : light === 'success'
      ? 'success'
      : sleeping
        ? 'sleeping'
        : dragOver
          ? 'dragging'
          : holdSign
            ? 'decision'
            : baseExpression === 'operator'
              ? 'working'
              : focused
                ? 'focused'
                : 'idle'
  const context: ComposerMascotContext = {
    size: MASCOT_SIZE,
    activity,
    expression,
    light,
    submitRevision: bounceSignal,
    reasoningEffort,
    speed,
    contextMax,
    reducedMotion: reduced
  }
  const ambient =
    !focused &&
    !dragOver &&
    !bubble &&
    !holdSign &&
    menuPos == null &&
    baseExpression === 'neutral' &&
    light === 'default'

  const markActivity = useCallback(() => {
    setActiveIdle(null)
    setSleeping((current) => {
      if (current && !reduced) setStartled(true)
      return false
    })
    const now = Date.now()
    if (now - lastActivityRef.current < MASCOT_ACTIVE_IDLE_ACTIVITY_THROTTLE_MS) return
    lastActivityRef.current = now
    setActivityRevision((value) => value + 1)
  }, [reduced])

  useEffect(() => {
    const markPointerMoveActivity = (): void => {
      if (Date.now() - lastActivityRef.current >= MASCOT_ACTIVE_IDLE_ACTIVITY_THROTTLE_MS) {
        markActivity()
      }
    }
    window.addEventListener('keydown', markActivity)
    window.addEventListener('pointerdown', markActivity)
    window.addEventListener('pointermove', markPointerMoveActivity, { passive: true })
    window.addEventListener('wheel', markActivity, { passive: true })
    window.addEventListener('focusin', markActivity)
    return () => {
      window.removeEventListener('keydown', markActivity)
      window.removeEventListener('pointerdown', markActivity)
      window.removeEventListener('pointermove', markPointerMoveActivity)
      window.removeEventListener('wheel', markActivity)
      window.removeEventListener('focusin', markActivity)
    }
  }, [markActivity])

  useEffect(() => {
    if (!ambient || sleeping || reduced) {
      setActiveIdle(null)
      return undefined
    }

    let timer = 0
    const start = (): void => {
      if (document.hidden) {
        timer = window.setTimeout(start, 5000)
        return
      }
      const motion = pickMascotActiveIdle(Math.random(), lastActiveIdleRef.current)
      lastActiveIdleRef.current = motion
      avatarBehavior.clearGesture()
      setActiveIdle({ motion, phase: 'outbound' })
    }
    timer = window.setTimeout(
      start,
      MASCOT_ACTIVE_IDLE_MIN_MS + Math.random() * MASCOT_ACTIVE_IDLE_JITTER_MS
    )
    return () => window.clearTimeout(timer)
  }, [ambient, activityRevision, sleeping, reduced, avatarBehavior.clearGesture])

  useEffect(() => {
    if (!activeIdle) return undefined
    const delay = activeIdle.phase === 'away'
      ? MASCOT_ACTIVE_IDLE_HOLD_MS[activeIdle.motion]
      : MASCOT_ACTIVE_IDLE_TRAVEL_MS[activeIdle.motion] + 160
    const timer = window.setTimeout(() => {
      setActiveIdle((current) => {
        if (!current) return null
        if (current.phase === 'outbound') return { ...current, phase: 'away' }
        if (current.phase === 'away') return { ...current, phase: 'inbound' }
        return null
      })
    }, delay)
    return () => window.clearTimeout(timer)
  }, [activeIdle])

  useLayoutEffect(() => {
    if (!handoff) return undefined
    const el = rootRef.current
    if (!el) return undefined
    let timer = 0
    const dy = reduced ? null : consumeMascotHandoff(el)
    if (dy != null) {
      const rising = dy > 0
      el.style.transition = 'none'
      el.style.transform = `translateY(${dy}px)`
      void el.offsetHeight
      el.style.transition = rising
        ? 'transform 420ms cubic-bezier(0.34, 1.56, 0.64, 1)'
        : 'transform 300ms cubic-bezier(0.55, 0, 0.8, 0.9)'
      el.style.transform = 'translateY(0)'
      if (rising) setStartled(true)
      timer = window.setTimeout(() => {
        el.style.transition = ''
        el.style.transform = ''
        if (!rising) setLanding(true)
      }, rising ? 430 : 310)
    }
    return () => {
      window.clearTimeout(timer)
      recordMascotHandoff(el)
    }
  }, [handoff])

  const previousAnchorOffsetRef = useRef(anchorOffset)
  useLayoutEffect(() => {
    const el = rootRef.current
    const previousOffset = previousAnchorOffsetRef.current
    previousAnchorOffsetRef.current = anchorOffset
    if (!el || previousOffset === anchorOffset || reduced) return undefined

    if (anchorOffset > previousOffset) {

      el.style.transition = ''
      el.style.transform = ''
      setLanding(false)
      return undefined
    }

    const currentVisualTop = el.getBoundingClientRect().top
    const offsetDelta = anchorOffset - previousOffset
    el.style.transition = 'none'
    el.style.transform = ''
    const targetTop = el.getBoundingClientRect().top
    const dy = currentVisualTop + offsetDelta - targetTop
    if (Math.abs(dy) < 1) return undefined

    const rising = dy > 0
    let timer = 0
    el.style.transform = `translateY(${dy}px)`
    void el.offsetHeight
    el.style.transition = rising
      ? 'transform 420ms cubic-bezier(0.34, 1.56, 0.64, 1)'
      : 'transform 300ms cubic-bezier(0.55, 0, 0.8, 0.9)'
    el.style.transform = 'translateY(0)'
    if (rising) {
      setStartled(true)
    } else {

      setStartled(false)
      setPushLift(false)
    }
    timer = window.setTimeout(() => {
      el.style.transition = ''
      el.style.transform = ''
      if (!rising) setLanding(true)
    }, rising ? 430 : 310)

    return () => window.clearTimeout(timer)
  }, [anchorOffset])

  const previousAnchorPushSignalRef = useRef(anchorPushSignal)
  useEffect(() => {
    if (anchorPushSignal === previousAnchorPushSignalRef.current) return
    previousAnchorPushSignalRef.current = anchorPushSignal
    if (!reduced) setPushLift(true)
  }, [anchorPushSignal])

  const prevBounceRef = useRef(bounceSignal)
  useEffect(() => {
    if (bounceSignal === prevBounceRef.current) return
    prevBounceRef.current = bounceSignal
    if (!reduced) setLaunching(true)
  }, [bounceSignal])

  const prevLightRef = useRef(light)
  useEffect(() => {
    const prev = prevLightRef.current
    prevLightRef.current = light
    if (light === prev || reduced) return
    if (light === 'success') {
      setCheering(true)
      setSparkling(true)
    } else if (light === 'error') {
      setShaking(true)
    }
  }, [light])

  useEffect(() => {
    if (!ambient || reduced) {
      setSleeping(false)
      return
    }
    if (sleeping || activeIdle) return
    const timer = window.setTimeout(() => setSleeping(true), MASCOT_SLEEP_AFTER_MS)
    return () => window.clearTimeout(timer)
  }, [ambient, activityRevision, sleeping, activeIdle, reduced])

  const wake = useCallback(() => {
    setSleeping(false)
    if (!reduced) setStartled(true)
  }, [reduced])

  useEffect(() => {
    if (!waving) return
    const timer = window.setTimeout(() => setWaving(false), MASCOT_WAVE_DURATION_MS)
    return () => window.clearTimeout(timer)
  }, [waving])

  useEffect(() => {
    if (!focused || reduced) return
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.ctrlKey || event.metaKey || event.altKey) return
      setNodding(true)
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [focused, reduced])

  const onAnimationEnd = (event: AnimationEvent<HTMLDivElement>): void => {
    if (activeIdle) {
      const expected = activeIdle.motion === 'hop'
        ? 'composer-mascot-idle-hop-travel'
        : activeIdle.motion === 'rocket'
          ? 'composer-mascot-idle-rocket-flight-x'
          : activeIdle.phase === 'outbound'
            ? 'composer-mascot-idle-hover-launch-body'
            : 'composer-mascot-idle-hover-land-body'
      if (event.animationName === expected) {
        setActiveIdle((current) => {
          if (!current) return null
          return current.phase === 'outbound' ? { ...current, phase: 'away' } : null
        })
      }
    }
    switch (event.animationName) {
      case 'composer-mascot-launch':
        setLaunching(false)
        break
      case 'composer-mascot-cheer':
        setCheering(false)
        break
      case 'composer-mascot-sparkle':
        setSparkling(false)
        break
      case 'composer-mascot-shake':
        setShaking(false)
        break
      case 'composer-mascot-startle':
        setStartled(false)
        break
      case 'composer-mascot-nod':
        setNodding(false)
        break
      case 'composer-mascot-land':
        setLanding(false)
        break
      case 'composer-mascot-push-lift':
        setPushLift(false)
        break
    }
  }

  const poseTransform = sleeping
    ? 'translateY(2px) rotate(2.6deg) scale(0.985)'
    : light === 'error'
      ? 'translateY(2px) rotate(-3deg) scale(0.98)'
      : focused
        ? 'scale(1.1)'
        : 'scale(1)'

  const shotClass = cheering
    ? 'composer-mascot-cheer'
    : shaking
      ? 'composer-mascot-shake'
      : pushLift
        ? 'composer-mascot-push-lift'
      : startled
        ? 'composer-mascot-startle'
        : landing
          ? 'composer-mascot-land'
          : launching
            ? 'composer-mascot-launch'
            : nodding
              ? 'composer-mascot-nod'
              : undefined

  const loopClass = sleeping
    ? 'composer-mascot-sleep-breathe'
    : dragOver
      ? 'composer-mascot-eager'
      : baseExpression === 'operator' && light === 'default'
        ? 'composer-mascot-think'
        : 'composer-mascot-breathe'

  const rootClassName =
    [
      activeIdle ? 'composer-mascot-active-idle' : null,
      sleeping ? 'composer-mascot-sleeping' : null,
    ]
      .filter(Boolean)
      .join(' ') || undefined

  const character = <Avatar name={avatar ?? ''} size={MASCOT_SIZE} state={avatarBehavior.pose}
    expression={avatarBehavior.expression} gesture={avatarBehavior.gesture}
    gestureSequence={avatarBehavior.gestureSequence} onGestureComplete={avatarBehavior.completeGesture}
    eventSequence={bounceSignal + greetingSequence} motion={reduced ? 'off' : 'on'} />
  return (
    <div

      aria-hidden={interaction ? undefined : true}
      data-composer-mascot-motion={reduced ? 'off' : 'on'}
      ref={rootRef}
      className={rootClassName}
      data-mascot-name={avatar ?? ''}
      data-mascot-effort={reasoningEffort}
      data-mascot-speed={speed}
      data-mascot-context={contextMax ? 'max' : 'default'}
      data-mascot-profile-transition={profileTransition ? 'active' : 'idle'}
      data-mascot-active-idle={activeIdle?.motion}
      data-mascot-idle-phase={activeIdle?.phase}
      data-mascot-anchor-offset={anchorOffset}
      onAnimationEnd={onAnimationEnd}
      style={{
        '--mascot-body-dark': mascotPalette.bodyD,
        '--mascot-body-mid': mascotPalette.bodyM,
        '--mascot-body-light': mascotPalette.bodyL,
        '--mascot-mark-dark': mascotPalette.markD,
        '--mascot-mark-energy': mascotPalette.markM,
        '--mascot-energy-accent': mascotPalette.accent,
        '--mascot-profile-from-accent': profileTransition?.fromAccent ?? mascotPalette.accent,
        '--mascot-profile-to-accent': profileTransition?.toAccent ?? mascotPalette.accent,
        position: 'absolute',
        right: '40px',
        top: `${-(MASCOT_SIZE * (1 - MASCOT_HIDDEN_RATIO)) - MASCOT_RAISE - anchorOffset}px`,
        zIndex: 0,
        pointerEvents: 'none'
      } as CSSProperties}
    >
      {bubble && (
        <div
          style={{
            position: 'absolute',
            right: 0,
            bottom: 'calc(100% + 8px)',
            zIndex: 5,
            pointerEvents: 'auto'
          }}
        >
          {bubble}
        </div>
      )}
      <div
        key={profileTransitionRevision}
        className="composer-mascot-motion"
        style={{
          transformOrigin: 'bottom center',
          transform: `scale(${MASCOT_SCALE})`,

          filter: `drop-shadow(0 5.3px 7.3px color-mix(in srgb, ${mascotPalette.shadow} 20%, transparent))`
        }}
      >
        <div
          style={{
            transformOrigin: 'bottom center',
            transition: 'transform 280ms cubic-bezier(0.34, 1.56, 0.64, 1)',
            transform: poseTransform
          }}
        >
          <div className={shotClass}>
            <div className={loopClass}>
              <div
                className="composer-mascot-jelly"
                style={{ pointerEvents: 'auto', cursor: hasMenu ? 'context-menu' : undefined }}
                onMouseEnter={sleeping ? wake : undefined}
                onClick={() => {
                  if (sleeping) {
                    wake()
                    return
                  }
                  if (!reduced) {
                    setWaving(true)
                    setGreetingSequence((value) => value + 1)
                  }
                }}
                onContextMenu={
                  hasMenu
                    ? (e) => {
                        e.preventDefault()
                        setMenuPos({ x: e.clientX, y: e.clientY })
                      }
                    : undefined
                }
              >
                <div className="composer-mascot-fast-echo">
                  <div className="composer-mascot-character-stage">
                    {renderCharacter ? renderCharacter(character, context) : character}
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>

        {sleeping && (
          <div aria-hidden className="composer-mascot-zzz">
            <span>z</span>
            <span>z</span>
            <span>z</span>
          </div>
        )}
        {sparkling && (
          <div aria-hidden className="composer-mascot-sparkles">
            {MASCOT_SPARKLES.map((s, i) => (
              <i
                key={i}
                style={{ '--dx': s.dx, '--dy': s.dy, animationDelay: s.delay } as CSSProperties}
              />
            ))}
          </div>
        )}
      </div>

      {menuPos && renderMenu?.(menuPos, () => setMenuPos(null))}
    </div>
  )
}
