import { describe, expect, it } from 'vitest'
import type { FileAnnotationContext } from '../../shared/composerContext'
import type { PresentationSlide, SlideElement } from '../../shared/viewer/presentation'
import { elementLabels } from '../../shared/viewer/presentation'
import {
  findSlideMatches,
  firstMatchFrom,
  fitScale,
  showOrder,
  slideForKey,
  slideshowSlideForKey,
  stepMatchIndex
} from '../components/detail/viewers/presentation/slideNavigation'
import { annotationSelection, commentCounts, selectionAnnotation } from '../components/detail/viewers/presentation/useSlideComments'

const frame = { x: 0, y: 0, w: 100, h: 50 }

function textElement(id: string, text: string, extra: Partial<SlideElement> = {}): SlideElement {
  return {
    id,
    name: `TextBox ${id}`,
    kind: 'text',
    frame,
    text: { paragraphs: [{ level: 0, runs: [{ text }] }], anchor: 'top', insets: { left: 0, top: 0, right: 0, bottom: 0 }, wrap: true },
    ...extra
  }
}

function slide(number: number, elements: SlideElement[], hidden = false): PresentationSlide {
  return { number, hidden, notes: '', elements }
}

function annotation(target: string, targetKind: FileAnnotationContext['targetKind'], location: FileAnnotationContext['location']): FileAnnotationContext {
  return { id: target, kind: 'fileAnnotation', path: '/deck.pptx', target, targetKind, location, comment: 'note' }
}

describe('slide navigation', () => {
  it('moves with arrows, page keys, Home, and End within bounds', () => {
    expect(slideForKey('ArrowRight', 0, 3)).toBe(1)
    expect(slideForKey('PageDown', 2, 3)).toBe(2)
    expect(slideForKey('ArrowUp', 0, 3)).toBe(0)
    expect(slideForKey('PageUp', 2, 3)).toBe(1)
    expect(slideForKey('Home', 2, 3)).toBe(0)
    expect(slideForKey('End', 0, 3)).toBe(2)
    expect(slideForKey(' ', 0, 3)).toBeNull()
    expect(slideForKey('ArrowRight', 0, 0)).toBeNull()
  })

  it('advances the slideshow with Space and Enter and goes back with Backspace', () => {
    expect(slideshowSlideForKey(' ', 0, 3)).toBe(1)
    expect(slideshowSlideForKey('Enter', 1, 3)).toBe(2)
    expect(slideshowSlideForKey('Backspace', 1, 3)).toBe(0)
    expect(slideshowSlideForKey('Escape', 1, 3)).toBeNull()
  })

  it('skips hidden slides in the slideshow unless the show starts on one', () => {
    const slides = [slide(1, []), slide(2, [], true), slide(3, [])]
    expect(showOrder(slides, 0)).toEqual([0, 2])
    expect(showOrder(slides, 1)).toEqual([0, 1, 2])
  })

  it('fits the slide inside the padded viewport', () => {
    expect(fitScale({ width: 1328, height: 2000 }, { width: 1280, height: 720 }, 24)).toBe(1)
    expect(fitScale({ width: 2000, height: 408 }, { width: 1280, height: 720 }, 24)).toBe(0.5)
    expect(fitScale({ width: 100, height: 100 }, { width: 0, height: 720 }, 24)).toBe(1)
  })
})

describe('slide find', () => {
  const slides = [
    slide(1, [textElement('2', 'Revenue overview'), textElement('m-1', 'Revenue footer', { inherited: true })]),
    slide(2, [textElement('3', 'Costs')]),
    slide(3, [textElement('4', 'Revenue by region'), { id: '5', name: 'Chart 4', kind: 'chart', frame, chartTitle: 'Revenue trend' }])
  ]

  it('matches slide text case-insensitively, skipping inherited master text', () => {
    expect(findSlideMatches(slides, 'revenue')).toEqual([
      { slide: 0, elementId: '2' },
      { slide: 2, elementId: '4' },
      { slide: 2, elementId: '5' }
    ])
    expect(findSlideMatches(slides, '  ')).toEqual([])
  })

  it('starts from the current slide and wraps when stepping', () => {
    const matches = findSlideMatches(slides, 'revenue')
    expect(firstMatchFrom(matches, 1)).toBe(1)
    expect(firstMatchFrom(matches, 3)).toBe(0)
    expect(firstMatchFrom([], 0)).toBe(-1)
    expect(stepMatchIndex(2, 3, 1)).toBe(0)
    expect(stepMatchIndex(0, 3, -1)).toBe(2)
    expect(stepMatchIndex(-1, 3, -1)).toBe(2)
    expect(stepMatchIndex(0, 0, 1)).toBe(-1)
  })
})

describe('slide comments', () => {
  const deck = slide(3, [
    textElement('2', 'Title', { placeholder: 'title' }),
    textElement('3', 'One'),
    textElement('4', 'Two'),
    { id: '5', name: 'Chart 4', kind: 'chart', frame }
  ])

  it('labels commentable elements and builds slide and element targets', () => {
    const labels = elementLabels(deck)
    expect([...labels.values()]).toEqual(['Title', 'TextBox 3', 'TextBox 4', 'Chart'])
    expect(selectionAnnotation({ slide: 3, elementId: '5' }, labels)).toEqual({
      target: 'Slide 3 · Chart',
      targetKind: 'element',
      location: { slide: 3, elementId: '5' }
    })
    expect(selectionAnnotation({ slide: 3, elementId: null }, labels)).toEqual({ target: 'Slide 3', targetKind: 'slide', location: { slide: 3 } })
    expect(selectionAnnotation({ slide: 3, elementId: 'missing' }, labels)).toBeNull()
  })

  it('maps saved annotations back to their selection and counts them per slide', () => {
    const saved = [
      annotation('Slide 3 · Chart', 'element', { slide: 3, elementId: '5' }),
      annotation('Slide 3', 'slide', { slide: 3 }),
      annotation('Slide 1', 'slide', { slide: 1 }),
      annotation('Revenue!A1', 'range', { sheet: 'Revenue', range: 'A1' })
    ]
    expect(annotationSelection(saved[0]!)).toEqual({ slide: 3, elementId: '5' })
    expect(annotationSelection(saved[1]!)).toEqual({ slide: 3, elementId: null })
    expect(annotationSelection(saved[3]!)).toBeNull()
    expect(commentCounts(saved)).toEqual(new Map([[3, 2], [1, 1]]))
  })
})
