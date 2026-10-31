import { elementText, type PresentationSlide, type SlideFrame } from '../../../../../shared/viewer/presentation'

export const NARROW_DECK_WIDTH = 520

const PREVIOUS = new Set(['ArrowLeft', 'ArrowUp', 'PageUp'])
const NEXT = new Set(['ArrowRight', 'ArrowDown', 'PageDown'])
const SHOW_PREVIOUS = new Set([...PREVIOUS, 'Backspace'])
const SHOW_NEXT = new Set([...NEXT, ' ', 'Enter'])

function step(key: string, current: number, count: number, previous: Set<string>, next: Set<string>): number | null {
  if (count <= 0) return null
  let target: number | null = null
  if (previous.has(key)) target = current - 1
  else if (next.has(key)) target = current + 1
  else if (key === 'Home') target = 0
  else if (key === 'End') target = count - 1
  if (target === null) return null
  return Math.min(count - 1, Math.max(0, target))
}

export function slideForKey(key: string, current: number, count: number): number | null {
  return step(key, current, count, PREVIOUS, NEXT)
}

export function slideshowSlideForKey(key: string, current: number, count: number): number | null {
  return step(key, current, count, SHOW_PREVIOUS, SHOW_NEXT)
}

export function showOrder(slides: PresentationSlide[], start: number): number[] {
  return slides.flatMap((slide, index) => (!slide.hidden || index === start ? [index] : []))
}

export function fitScale(viewport: { width: number; height: number }, slide: { width: number; height: number }, padding: number): number {
  if (slide.width <= 0 || slide.height <= 0) return 1
  const width = Math.max(1, viewport.width - padding * 2)
  const height = Math.max(1, viewport.height - padding * 2)
  return Math.min(width / slide.width, height / slide.height)
}

interface SlideMatch {
  slide: number
  elementId: string
}

export function findSlideMatches(slides: PresentationSlide[], query: string): SlideMatch[] {
  const needle = query.trim().toLocaleLowerCase()
  if (!needle) return []
  const matches: SlideMatch[] = []
  slides.forEach((slide, index) => {
    for (const element of slide.elements) {
      if (!element.inherited && elementText(element).toLocaleLowerCase().includes(needle)) matches.push({ slide: index, elementId: element.id })
    }
  })
  return matches
}

export function firstMatchFrom(matches: SlideMatch[], slide: number): number {
  if (matches.length === 0) return -1
  const index = matches.findIndex((match) => match.slide >= slide)
  return index >= 0 ? index : 0
}

export function stepMatchIndex(index: number, count: number, direction: 1 | -1): number {
  if (count === 0) return -1
  return index < 0 ? (direction > 0 ? 0 : count - 1) : (index + direction + count) % count
}

export function scaleFrame(frame: SlideFrame, scale: number, offset: number): { top: number; left: number; width: number; height: number } {
  return { top: offset + frame.y * scale, left: offset + frame.x * scale, width: frame.w * scale, height: Math.max(frame.h * scale, 2) }
}
