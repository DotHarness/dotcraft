/**
 * Homepage motion orchestration.
 *
 * - Marks the root with `dc-motion-ready` so motion.css may hold pre-reveal
 *   states (the class is the JS-availability gate: without it every reveal
 *   target renders visible).
 * - Plays the hero texts-reveal once the hero mounts.
 * - Reveals `.dc-reveal` sections the first time they enter the viewport.
 * - Wires the quick-start copy buttons (icon swap to a check on success).
 *
 * Safe to call repeatedly: each hook marks the elements it wired.
 */

/** Reveal once a block's top rises above this share of the viewport height. */
const REVEAL_FOLD = 0.92

export function setupHomeMotion(): void {
  document.documentElement.classList.add('dc-motion-ready')

  const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches

  revealHero()
  revealOnScroll(reducedMotion)
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
 * Rect-based rather than IntersectionObserver: an observer only reports
 * threshold crossings, so a block jumped past in one step (anchor navigation,
 * or lazy images shifting the layout under a fast scroll) would never fire and
 * stay invisible. Checking rects on a passive, rAF-throttled scroll listener
 * also reveals anything already above the viewport. Listeners detach once
 * every block is shown.
 */
function revealOnScroll(reducedMotion: boolean): void {
  const targets = Array.from(document.querySelectorAll<HTMLElement>('.dc-reveal'))
    .filter((el) => el.dataset.motionInit !== 'true')
  if (targets.length === 0) return

  for (const el of targets) {
    el.dataset.motionInit = 'true'
  }

  if (reducedMotion) {
    for (const el of targets) {
      el.classList.add('is-shown')
    }
    return
  }

  const pending = new Set(targets)
  let ticking = false

  const check = (): void => {
    ticking = false
    const fold = window.innerHeight * REVEAL_FOLD
    for (const el of pending) {
      const rect = el.getBoundingClientRect()
      // Below-the-fold blocks wait; anything at, in, or above the viewport
      // shows (a detached element reports a zero rect and is released too).
      if (rect.top < fold || rect.bottom < 0) {
        el.classList.add('is-shown')
        pending.delete(el)
      }
    }
    if (pending.size === 0) {
      window.removeEventListener('scroll', onScroll)
      window.removeEventListener('resize', onScroll)
    }
  }

  const onScroll = (): void => {
    if (ticking) return
    ticking = true
    requestAnimationFrame(check)
  }

  window.addEventListener('scroll', onScroll, { passive: true })
  window.addEventListener('resize', onScroll, { passive: true })
  // Double rAF so the hidden state paints once before above-the-fold blocks
  // flip to shown — otherwise their entrance transition never plays.
  requestAnimationFrame(() => requestAnimationFrame(check))
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
