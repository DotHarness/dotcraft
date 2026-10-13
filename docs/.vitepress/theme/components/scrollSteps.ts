import { onBeforeUnmount, onMounted, ref, type Ref } from 'vue'

/** The section is taller than the viewport and its `pin` child sticks; scroll distance through it selects the step. */
export function useScrollSteps(
  section: Ref<HTMLElement | undefined>,
  pin: Ref<HTMLElement | undefined>,
  count: () => number,
  // Pinning needs room for the pinned frame; smaller screens get the steps as ordinary controls.
  media = '(min-width: 960px) and (min-height: 640px)'
) {
  const index = ref(0)
  const progress = ref(0)
  const pinned = ref(false)
  let frame = 0
  let query: MediaQueryList | undefined

  function travel(): { top: number; distance: number } | null {
    if (!section.value || !pin.value) return null
    const rect = section.value.getBoundingClientRect()
    const top = parseFloat(getComputedStyle(pin.value).top) || 0
    return { top: rect.top - top, distance: Math.max(1, rect.height - pin.value.offsetHeight) }
  }

  function measure(): void {
    frame = 0
    const span = travel()
    if (!span || !pinned.value) return
    progress.value = Math.min(1, Math.max(0, -span.top / span.distance))
    index.value = Math.min(count() - 1, Math.floor(progress.value * count()))
  }

  function onScroll(): void {
    if (!frame) frame = requestAnimationFrame(measure)
  }

  function sync(): void {
    pinned.value = query?.matches ?? false
    measure()
  }

  function goTo(step: number): void {
    if (!pinned.value) {
      index.value = step
      return
    }
    const span = travel()
    if (!span) return
    const offset = ((step + 0.5) / count()) * span.distance
    const smooth = !window.matchMedia('(prefers-reduced-motion: reduce)').matches
    window.scrollTo({ top: window.scrollY + span.top + offset, behavior: smooth ? 'smooth' : 'auto' })
  }

  onMounted(() => {
    query = window.matchMedia(media)
    query.addEventListener('change', sync)
    window.addEventListener('scroll', onScroll, { passive: true })
    window.addEventListener('resize', onScroll)
    sync()
  })

  onBeforeUnmount(() => {
    cancelAnimationFrame(frame)
    query?.removeEventListener('change', sync)
    window.removeEventListener('scroll', onScroll)
    window.removeEventListener('resize', onScroll)
  })

  function stepProgress(): number {
    return Math.min(1, Math.max(0, progress.value * count() - index.value))
  }

  return { index, progress, pinned, goTo, stepProgress }
}
