/**
 * Homepage motion orchestration.
 *
 * - Marks the root with `dc-motion-ready` so motion.css may hold pre-reveal
 *   states (the class is the JS-availability gate: without it every reveal
 *   target renders visible).
 * - Plays the hero texts-reveal once the hero mounts.
 * - Reveals `.dc-reveal` sections whenever they re-enter the viewport after
 *   leaving it completely, with motion that follows the scroll direction.
 * - Wires the quick-start copy buttons (icon swap to a check on success).
 *
 * Safe to call repeatedly: each hook marks the elements it wired.
 */

/** Reveal inside the centered band left by these symmetric viewport folds. */
const REVEAL_FOLD = 0.92
const REVEAL_RESET_MARGIN = 24
const NAV_SOLID_SCROLL_Y = 24
const NAV_TRANSPARENT_SCROLL_Y = 8

let activeRevealRoot: HTMLElement | null = null
let activeRevealCleanup: (() => void) | null = null
let activeNavRoot: HTMLElement | null = null
let activeNavCleanup: (() => void) | null = null

export function setupHomeMotion(): void {
  document.documentElement.classList.add('dc-motion-ready')

  const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches
  const revealRoot = document.querySelector<HTMLElement>('.dc-home')

  revealHero()
  if (revealRoot !== activeRevealRoot) {
    activeRevealCleanup?.()
    activeRevealRoot = revealRoot
    activeRevealCleanup = revealRoot ? revealOnScroll(revealRoot, reducedMotion) : null
  }
  if (revealRoot !== activeNavRoot) {
    activeNavCleanup?.()
    activeNavRoot = revealRoot
    activeNavCleanup = revealRoot ? setupHomeNavState(revealRoot) : null
  }
  wireCopyButtons()
}

function revealHero(): void {
  const hero = document.querySelector<HTMLElement>('.dc-hero .t-stagger')
  if (!hero || hero.dataset.motionInit === 'true') return
  hero.dataset.motionInit = 'true'

  // Double rAF: the first frame paints the hidden state, the second flips it,
  // so the entrance transition actually plays after route changes.
  requestAnimationFrame(() => {
    requestAnimationFrame(() => hero.classList.add('is-shown'))
  })
}

/**
 * The docs homepage uses the page layout, so VitePress does not expose its
 * built-in home/top navbar state. Mirror that state on the document root with
 * a small hysteresis band so the transparent and solid nav styles can
 * crossfade without chattering around the threshold.
 */
function setupHomeNavState(root: HTMLElement): () => void {
  const pageRoot = document.documentElement
  let solid = window.scrollY >= NAV_SOLID_SCROLL_Y
  let scheduledFrame = 0
  let disposed = false

  const apply = (): void => {
    pageRoot.classList.add('dc-home-active')
    pageRoot.classList.toggle('dc-home-nav-solid', solid)
  }

  const cleanup = (): void => {
    if (disposed) return
    disposed = true
    window.removeEventListener('scroll', schedule)
    if (scheduledFrame) cancelAnimationFrame(scheduledFrame)
    rootObserver.disconnect()
    if (activeNavRoot === root) {
      pageRoot.classList.remove('dc-home-active', 'dc-home-nav-solid')
      activeNavRoot = null
      activeNavCleanup = null
    }
  }

  const check = (): void => {
    scheduledFrame = 0
    if (disposed || !root.isConnected) {
      cleanup()
      return
    }

    const scrollY = window.scrollY
    if (!solid && scrollY >= NAV_SOLID_SCROLL_Y) {
      solid = true
      apply()
    } else if (solid && scrollY <= NAV_TRANSPARENT_SCROLL_Y) {
      solid = false
      apply()
    }
  }

  const schedule = (): void => {
    if (disposed || scheduledFrame) return
    scheduledFrame = requestAnimationFrame(check)
  }

  const rootObserver = new MutationObserver(() => {
    if (!root.isConnected) cleanup()
  })

  apply()
  window.addEventListener('scroll', schedule, { passive: true })
  rootObserver.observe(document.body, { childList: true, subtree: true })

  return cleanup
}

/**
 * Rect-based rather than IntersectionObserver: checking every rAF-throttled
 * viewport change catches fast jumps, anchor navigation, and layout shifts.
 * A block rearms only after leaving the viewport completely, then re-enters
 * from the edge that matches the current scroll direction.
 */
function revealOnScroll(root: HTMLElement, reducedMotion: boolean): () => void {
  const targets = Array.from(root.querySelectorAll<HTMLElement>('.dc-reveal'))

  if (reducedMotion) {
    for (const el of targets) {
      el.classList.remove('dc-reveal--from-top', 'dc-reveal--resetting')
      el.classList.add('is-shown')
    }
    return () => {}
  }

  type ScrollDirection = 'up' | 'down'
  let direction: ScrollDirection = 'down'
  let lastScrollY = window.scrollY
  let scheduledFrame = 0
  let paintFrame = 0
  let disposed = false

  const setOrigin = (el: HTMLElement, origin: ScrollDirection): void => {
    el.classList.toggle('dc-reveal--from-top', origin === 'up')
  }

  const rearm = (el: HTMLElement, origin: ScrollDirection): void => {
    el.classList.add('dc-reveal--resetting')
    setOrigin(el, origin)
    el.classList.remove('is-shown')
    // Apply the hidden state immediately while the block is safely offscreen.
    void el.offsetWidth
    el.classList.remove('dc-reveal--resetting')
  }

  const check = (): void => {
    scheduledFrame = 0
    if (disposed || !root.isConnected) {
      cleanup()
      return
    }

    const currentScrollY = window.scrollY
    if (currentScrollY > lastScrollY) direction = 'down'
    if (currentScrollY < lastScrollY) direction = 'up'
    lastScrollY = currentScrollY

    const lowerFold = window.innerHeight * REVEAL_FOLD
    const upperFold = window.innerHeight * (1 - REVEAL_FOLD)
    for (const el of targets) {
      const rect = el.getBoundingClientRect()

      if (el.classList.contains('is-shown')) {
        if (rect.bottom < -REVEAL_RESET_MARGIN) {
          rearm(el, 'up')
        } else if (rect.top > window.innerHeight + REVEAL_RESET_MARGIN) {
          rearm(el, 'down')
        }
        continue
      }

      // Keep fast-jumped blocks armed from the side they now occupy.
      if (rect.bottom < -REVEAL_RESET_MARGIN) {
        setOrigin(el, 'up')
      } else if (rect.top > window.innerHeight + REVEAL_RESET_MARGIN) {
        setOrigin(el, 'down')
      } else if (rect.top < lowerFold && rect.bottom > upperFold) {
        setOrigin(el, direction)
        el.classList.add('is-shown')
      }
    }
  }

  const schedule = (): void => {
    if (disposed || scheduledFrame) return
    scheduledFrame = requestAnimationFrame(check)
  }

  const rootObserver = new MutationObserver(() => {
    if (!root.isConnected) cleanup()
  })

  const cleanup = (): void => {
    if (disposed) return
    disposed = true
    window.removeEventListener('scroll', schedule)
    window.removeEventListener('resize', schedule)
    if (scheduledFrame) cancelAnimationFrame(scheduledFrame)
    if (paintFrame) cancelAnimationFrame(paintFrame)
    rootObserver.disconnect()
    if (activeRevealRoot === root) {
      activeRevealRoot = null
      activeRevealCleanup = null
    }
  }

  window.addEventListener('scroll', schedule, { passive: true })
  window.addEventListener('resize', schedule, { passive: true })
  rootObserver.observe(document.body, { childList: true, subtree: true })
  // Double rAF so the hidden state paints once before above-the-fold blocks
  // flip to shown — otherwise their entrance transition never plays.
  paintFrame = requestAnimationFrame(() => {
    paintFrame = 0
    schedule()
  })

  return cleanup
}

function wireCopyButtons(): void {
  const buttons = document.querySelectorAll<HTMLButtonElement>('[data-copy]')
  for (const button of buttons) {
    if (button.dataset.motionInit === 'true') continue
    button.dataset.motionInit = 'true'

    button.addEventListener('click', () => {
      const code = button.closest('.dc-cmd')?.querySelector('code')
      const text = code?.textContent?.trim()
      if (!text) return
      void navigator.clipboard?.writeText(text).then(() => {
        const swap = button.querySelector<HTMLElement>('.t-icon-swap')
        if (!swap) return
        swap.dataset.state = 'b'
        window.setTimeout(() => {
          swap.dataset.state = 'a'
        }, 1600)
      })
    })
  }
}
