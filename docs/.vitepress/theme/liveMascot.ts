/**
 * Live mascot behavior rig. Only flips `data-state`, one-shot root classes,
 * and the ±1 `--gaze-*` vars — all motion is CSS in mascot.css, except the
 * CTA entrance (WAAPI). Scheduling uses setTimeout, not rAF: timers still
 * fire in hidden tabs, behaviors early-return instead.
 */

const GAZE_RADIUS = 600
const GAZE_MIN_TRAVEL = 24
const POINTER_IDLE_FOR_LOOK = 4000
const OFFSCREEN_MARGIN = 80
const HAPPY_BEAT_MS = 2200
const INTERACT_SUPPRESS_MS = 8000

type Flavor = 'blink' | 'look' | 'antenna' | 'nod'

interface MascotController {
  el: HTMLElement
  role: string | undefined
  baseline: string
  interactive: boolean
  onscreen: boolean
  hovered: boolean
  /** Distance from mascot center to the cursor at the last gaze write. */
  gazeDistance: number
  clickBeat: number
  lastClickAt: number
}

let activeMascotRoot: HTMLElement | null = null
let activeMascotCleanup: (() => void) | null = null

export function setupLiveMascots(): void {
  const root = document.querySelector<HTMLElement>('.dc-home')
  if (root === activeMascotRoot) return
  activeMascotCleanup?.()
  activeMascotRoot = root
  activeMascotCleanup = root ? wireMascots(root) : null
}

function wireMascots(root: HTMLElement): (() => void) | null {
  const els = Array.from(root.querySelectorAll<HTMLElement>('.dc-mascot[data-live]'))
  if (els.length === 0) return null

  // Reduced motion: the SSR'd data-state already renders a static face — park.
  if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return () => {}

  const controllers: MascotController[] = els.map((el, index) => {
    // Ambient loops read this as a negative animation-delay — phase desync.
    el.style.setProperty('--dc-desync', String(index * 0.7 + Math.random() * 0.5))
    return {
      el,
      role: el.dataset.role,
      baseline: el.dataset.state ?? 'idle',
      interactive: el.dataset.interactive === 'true',
      onscreen: true,
      hovered: false,
      gazeDistance: Infinity,
      clickBeat: 0,
      lastClickAt: 0
    }
  })

  let disposed = false
  let lastInteractAt = 0
  let lastPointerMoveAt = 0
  const timeouts = new Set<number>()

  const later = (ms: number, fn: () => void): void => {
    const id = window.setTimeout(() => {
      timeouts.delete(id)
      if (!disposed) fn()
    }, ms)
    timeouts.add(id)
  }

  const rand = (min: number, max: number): number => min + Math.random() * (max - min)

  const setState = (c: MascotController, state: string): void => {
    c.el.dataset.state = state
  }

  /** Cleared on a fixed timer — animationend fires once per animated sub-part. */
  const oneShot = (c: MascotController, cls: string, duration: number): void => {
    if (c.el.classList.contains(cls)) return
    c.el.classList.add(cls)
    later(duration, () => c.el.classList.remove(cls))
  }

  const blink = (c: MascotController): void => oneShot(c, 'dc-mascot-blink', 320)

  const look = (c: MascotController): void => {
    oneShot(c, Math.random() < 0.5 ? 'dc-mascot-look-l' : 'dc-mascot-look-r', 1200)
  }

  /** Look one-shots and the gaze var share the eye transform — never both. */
  const gazeEngaged = (c: MascotController): boolean =>
    performance.now() - lastPointerMoveAt < POINTER_IDLE_FOR_LOOK && c.gazeDistance <= GAZE_RADIUS

  const flavorOf = (c: MascotController): Flavor => {
    if (c.role === 'leader') return 'antenna'
    if (c.role === 'builder') return 'nod'
    if (c.role === 'explorer') return 'look'
    if (!c.role) return 'look'
    return 'blink'
  }

  const act = (c: MascotController): void => {
    if (document.visibilityState === 'hidden' || !c.onscreen || c.hovered) return
    const roll = Math.random()
    if (roll < 0.6) {
      blink(c)
    } else if (roll < 0.85) {
      if (gazeEngaged(c)) blink(c)
      else look(c)
    } else {
      const flavor = flavorOf(c)
      if (flavor === 'antenna') oneShot(c, 'dc-mascot-antenna-bob', 800)
      else if (flavor === 'nod') oneShot(c, 'dc-mascot-nod', 520)
      else if (flavor === 'look' && !gazeEngaged(c)) look(c)
      else blink(c)
    }
  }

  const idleLoop = (c: MascotController, first: boolean): void => {
    const stagger = first ? controllers.indexOf(c) * 700 + rand(0, 400) : 0
    later(rand(2000, 6000) + stagger, () => {
      act(c)
      idleLoop(c, false)
    })
  }

  const hero = controllers.find((c) => c.el.closest('.dc-hero__mascot'))
  const happyLoop = (): void => {
    later(rand(9000, 16000), () => {
      const settled = performance.now() - lastInteractAt > INTERACT_SUPPRESS_MS
      if (hero && hero.onscreen && !hero.hovered && settled && document.visibilityState === 'visible') {
        setState(hero, 'happy')
        later(HAPPY_BEAT_MS, () => {
          if (hero.el.dataset.state === 'happy' && !hero.hovered) setState(hero, hero.baseline)
        })
      }
      happyLoop()
    })
  }

  // CTA entrance: the robot heaves the headline in (strain, three shoves with
  // friction slip-backs, overshoot, hop, wave). Parking is inline-style only,
  // so no-JS and reduced-motion visitors always get the settled layout.
  const ctaController = controllers.find((c) => c.el.closest('.dc-cta__mascot'))
  const ctaWrap = ctaController?.el.closest<HTMLElement>('.dc-cta__mascot') ?? null
  const ctaTitle = root.querySelector<HTMLElement>('[data-cta-title]')
  const rideAnimations: Animation[] = []
  let ctaPerformed = false
  let ctaOffset = 0

  if (ctaController && ctaWrap && ctaTitle) {
    ctaOffset = Math.round(Math.min(window.innerWidth * 0.55, 520))
    ctaTitle.style.transform = `translateX(${ctaOffset}px)`
    ctaWrap.style.transform = `translateX(${ctaOffset}px)`
  }

  const performCtaPush = (): void => {
    if (ctaPerformed || !ctaController || !ctaWrap || !ctaTitle) return
    ctaPerformed = true
    const c = ctaController
    // Let the section's .dc-reveal fade get underway before the shove starts.
    later(420, () => {
      c.el.classList.add('dc-mascot-pushing')
      const x = (fraction: number): string => `translateX(${Math.round(ctaOffset * fraction)}px)`
      const rideFrames: Keyframe[] = [
        { transform: x(1), offset: 0, easing: 'cubic-bezier(0.65, 0, 0.85, 1)' },
        { transform: x(0.96), offset: 0.14, easing: 'cubic-bezier(0.35, 0, 0.25, 1)' },
        { transform: x(0.6), offset: 0.34, easing: 'ease-out' },
        { transform: x(0.635), offset: 0.42, easing: 'cubic-bezier(0.35, 0, 0.25, 1)' },
        { transform: x(0.28), offset: 0.6, easing: 'ease-out' },
        { transform: x(0.315), offset: 0.68, easing: 'cubic-bezier(0.3, 0, 0.2, 1)' },
        { transform: 'translateX(-14px)', offset: 0.88, easing: 'ease-out' },
        { transform: 'translateX(0px)', offset: 1 }
      ]
      // Per-keyframe ease only: an options-level easing warps the whole
      // iteration and would desynchronize the lean from the ride. Ends at the
      // .dc-mascot-pushing static -7deg so the class removal springs cleanly.
      const leanFrames: Keyframe[] = [
        { transform: 'rotate(-3deg)', offset: 0, easing: 'ease-in-out' },
        { transform: 'rotate(-10deg)', offset: 0.12, easing: 'ease-in-out' },
        { transform: 'rotate(-6deg)', offset: 0.34, easing: 'ease-in-out' },
        { transform: 'rotate(-10deg)', offset: 0.44, easing: 'ease-in-out' },
        { transform: 'rotate(-6deg)', offset: 0.6, easing: 'ease-in-out' },
        { transform: 'rotate(-10deg)', offset: 0.7, easing: 'ease-in-out' },
        { transform: 'rotate(-4deg)', offset: 0.88, easing: 'ease-in-out' },
        { transform: 'rotate(-7deg)', offset: 1 }
      ]
      const options: KeyframeAnimationOptions = {
        duration: 2800,
        easing: 'linear',
        fill: 'forwards'
      }
      const jelly = c.el.querySelector<HTMLElement>('.dc-mascot__jelly')
      const titleRide = ctaTitle.animate(rideFrames, options)
      const robotRide = ctaWrap.animate(rideFrames, options)
      const leanRide = jelly?.animate(leanFrames, options)
      rideAnimations.push(titleRide, robotRide)
      if (leanRide) rideAnimations.push(leanRide)
      titleRide.finished
        .then(() => {
          if (disposed) return
          ctaTitle.style.transform = ''
          ctaWrap.style.transform = ''
          titleRide.cancel()
          robotRide.cancel()
          leanRide?.cancel()
          c.el.classList.remove('dc-mascot-pushing')
          later(140, () => oneShot(c, 'dc-mascot-startle', 340))
          later(620, () => oneShot(c, 'dc-mascot-wave', 1650))
        })
        .catch(() => {})
    })
  }

  // Rect-based onscreen gate (repo pattern — see homeMotion.ts on why not IO).
  let scheduledFrame = 0
  const checkOnscreen = (): void => {
    scheduledFrame = 0
    if (disposed || !root.isConnected) {
      cleanup()
      return
    }
    for (const c of controllers) {
      const rect = c.el.getBoundingClientRect()
      const visible = rect.bottom > -OFFSCREEN_MARGIN && rect.top < window.innerHeight + OFFSCREEN_MARGIN
      if (visible !== c.onscreen) {
        c.onscreen = visible
        c.el.classList.toggle('is-offscreen', !visible)
      }
      if (visible && c === ctaController) performCtaPush()
    }
  }
  const scheduleCheck = (): void => {
    if (disposed || scheduledFrame) return
    scheduledFrame = requestAnimationFrame(checkOnscreen)
  }

  // Mouse layer is skipped entirely on touch-first devices.
  const finePointer = window.matchMedia('(hover: hover) and (pointer: fine)').matches
  const interactive = finePointer ? controllers.filter((c) => c.interactive) : []
  let gazeFrame = 0
  let pointerX = 0
  let pointerY = 0
  let appliedX = -Infinity
  let appliedY = -Infinity

  const applyGaze = (): void => {
    gazeFrame = 0
    if (disposed) return
    appliedX = pointerX
    appliedY = pointerY
    for (const c of interactive) {
      if (!c.onscreen) continue
      const rect = c.el.getBoundingClientRect()
      const dx = pointerX - (rect.left + rect.width / 2)
      const dy = pointerY - (rect.top + rect.height / 2)
      const distance = Math.hypot(dx, dy)
      c.gazeDistance = distance
      if (distance > GAZE_RADIUS) {
        if (c.el.style.getPropertyValue('--gaze-x') !== '') {
          c.el.style.removeProperty('--gaze-x')
          c.el.style.removeProperty('--gaze-y')
        }
        continue
      }
      const scale = distance > 0 ? Math.min(1, distance / 160) / distance : 0
      c.el.style.setProperty('--gaze-x', (dx * scale).toFixed(3))
      c.el.style.setProperty('--gaze-y', (dy * scale).toFixed(3))
    }
  }

  const onPointerMove = (event: PointerEvent): void => {
    pointerX = event.clientX
    pointerY = event.clientY
    lastPointerMoveAt = performance.now()
    // Only re-aim after meaningful travel — no continuous style writes.
    if (Math.hypot(pointerX - appliedX, pointerY - appliedY) < GAZE_MIN_TRAVEL) return
    if (!gazeFrame) gazeFrame = requestAnimationFrame(applyGaze)
  }

  const releaseGaze = (): void => {
    for (const c of interactive) {
      c.gazeDistance = Infinity
      c.el.style.removeProperty('--gaze-x')
      c.el.style.removeProperty('--gaze-y')
    }
    appliedX = -Infinity
    appliedY = -Infinity
  }

  const clickGesture = (c: MascotController): void => {
    lastInteractAt = performance.now()
    // Repeat-clicks within 5s walk a gesture cycle instead of replaying.
    const now = performance.now()
    c.clickBeat = now - c.lastClickAt < 5000 ? (c.clickBeat + 1) % 3 : 0
    c.lastClickAt = now
    if (c.clickBeat === 0) {
      oneShot(c, 'dc-mascot-wave', 1650)
    } else if (c.clickBeat === 1) {
      // Held class: arms pose and return via the 320ms spring transition.
      oneShot(c, 'dc-mascot-cheer', 1300)
    } else {
      oneShot(c, 'dc-mascot-antenna-bob', 800)
    }
  }

  const elListeners: Array<() => void> = []
  for (const c of interactive) {
    const onEnter = (): void => {
      c.hovered = true
      lastInteractAt = performance.now()
      setState(c, 'happy')
    }
    const onLeave = (): void => {
      c.hovered = false
      later(600, () => {
        if (!c.hovered && c.el.dataset.state === 'happy') setState(c, c.baseline)
      })
    }
    const onClick = (): void => clickGesture(c)
    c.el.addEventListener('pointerenter', onEnter)
    c.el.addEventListener('pointerleave', onLeave)
    c.el.addEventListener('click', onClick)
    elListeners.push(() => {
      c.el.removeEventListener('pointerenter', onEnter)
      c.el.removeEventListener('pointerleave', onLeave)
      c.el.removeEventListener('click', onClick)
    })
  }

  const rootObserver = new MutationObserver(() => {
    if (!root.isConnected) cleanup()
  })

  const cleanup = (): void => {
    if (disposed) return
    disposed = true
    for (const id of timeouts) window.clearTimeout(id)
    timeouts.clear()
    if (scheduledFrame) cancelAnimationFrame(scheduledFrame)
    if (gazeFrame) cancelAnimationFrame(gazeFrame)
    for (const animation of rideAnimations) animation.cancel()
    window.removeEventListener('scroll', scheduleCheck)
    window.removeEventListener('resize', scheduleCheck)
    if (finePointer) {
      window.removeEventListener('pointermove', onPointerMove)
      document.documentElement.removeEventListener('mouseleave', releaseGaze)
    }
    for (const remove of elListeners) remove()
    rootObserver.disconnect()
    if (activeMascotRoot === root) {
      activeMascotRoot = null
      activeMascotCleanup = null
    }
  }

  window.addEventListener('scroll', scheduleCheck, { passive: true })
  window.addEventListener('resize', scheduleCheck, { passive: true })
  if (finePointer) {
    window.addEventListener('pointermove', onPointerMove, { passive: true })
    document.documentElement.addEventListener('mouseleave', releaseGaze)
  }
  rootObserver.observe(document.body, { childList: true, subtree: true })

  checkOnscreen()
  for (const c of controllers) idleLoop(c, true)
  if (hero) happyLoop()

  return cleanup
}
