export const PRESENTATION_MAX_SLIDES = 500
export const PRESENTATION_MAX_ELEMENTS = 1000
export const PRESENTATION_MAX_IMAGE_BYTES = 8 * 1024 * 1024
export const PRESENTATION_MAX_TOTAL_IMAGE_BYTES = 32 * 1024 * 1024

export interface SlideFrame {
  x: number
  y: number
  w: number
  h: number
  rotation?: number
  flipH?: boolean
  flipV?: boolean
}

export interface GradientFill {
  angle: number
  stops: Array<{ offset: number; color: string }>
}

export type SlideFill = { color: string } | { gradient: GradientFill } | { image: string }

export interface SlideLine {
  color: string
  width: number
  dash?: boolean
}

export interface TextRun {
  text: string
  bold?: boolean
  italic?: boolean
  underline?: boolean
  strike?: boolean
  size?: number
  color?: string
  font?: string
  link?: string
  baseline?: number
}

export interface TextBullet {
  text: string
  color?: string
  scale?: number
  font?: string
}

export interface TextParagraph {
  runs: TextRun[]
  align?: 'left' | 'center' | 'right' | 'justify'
  level: number
  bullet?: TextBullet
  marginLeft?: number
  indent?: number
  lineSpacing?: number
  lineSpacingPx?: number
  spaceBefore?: number
  spaceAfter?: number
  emptySize?: number
}

export interface TextBody {
  paragraphs: TextParagraph[]
  anchor: 'top' | 'middle' | 'bottom'
  insets: { left: number; top: number; right: number; bottom: number }
  wrap: boolean
}

export type ShapeGeometry =
  | { kind: 'rect' }
  | { kind: 'roundRect'; radius: number }
  | { kind: 'ellipse' }
  | { kind: 'line' }
  | { kind: 'polygon'; points: Array<[number, number]> }
  | { kind: 'path'; d: string; width: number; height: number }

export interface ImageCrop {
  left: number
  top: number
  right: number
  bottom: number
}

export interface TableCell {
  text: TextBody
  fill?: string
  colSpan?: number
  rowSpan?: number
  border?: string
}

export interface SlideTable {
  columns: number[]
  rows: Array<{ height: number; cells: Array<TableCell | null> }>
}

export type SlideElementKind = 'text' | 'shape' | 'picture' | 'table' | 'chart' | 'unsupported'

export interface SlideElement {
  id: string
  name: string
  kind: SlideElementKind
  frame: SlideFrame
  placeholder?: string
  inherited?: boolean
  geometry?: ShapeGeometry
  fill?: SlideFill
  line?: SlideLine
  text?: TextBody
  image?: string
  crop?: ImageCrop
  table?: SlideTable
  chartTitle?: string
}

export interface PresentationSlide {
  number: number
  hidden: boolean
  background?: SlideFill
  notes: string
  elements: SlideElement[]
  failed?: boolean
}

export interface ReadPresentationParams {
  absolutePath: string
}

export interface ReadPresentationResult {
  width: number
  height: number
  slides: PresentationSlide[]
  slideCount: number
  images: Record<string, string>
}

const KIND_LABELS: Record<SlideElementKind, string> = {
  text: 'Text',
  shape: 'Shape',
  picture: 'Picture',
  table: 'Table',
  chart: 'Chart',
  unsupported: 'Object'
}

function friendlyLabel(element: SlideElement): string {
  if (element.placeholder === 'title' || element.placeholder === 'ctrTitle') return 'Title'
  if (element.placeholder === 'subTitle') return 'Subtitle'
  return KIND_LABELS[element.kind]
}

/** Labels are unique per slide because comment targets are deduplicated by their label. */
export function elementLabels(slide: PresentationSlide): Map<string, string> {
  const commentable = slide.elements.filter((element) => !element.inherited)
  const friendly = commentable.map(friendlyLabel)
  const counts = new Map<string, number>()
  for (const label of friendly) counts.set(label, (counts.get(label) ?? 0) + 1)
  const labels = new Map<string, string>()
  const used = new Set<string>()
  commentable.forEach((element, index) => {
    const base = counts.get(friendly[index]!) === 1 ? friendly[index]! : element.name.trim() || friendly[index]!
    let label = base
    for (let suffix = 2; used.has(label); suffix++) label = `${base} ${suffix}`
    used.add(label)
    labels.set(element.id, label)
  })
  return labels
}

export function slideTarget(slide: number, elementLabel?: string): string {
  return elementLabel ? `Slide ${slide} · ${elementLabel}` : `Slide ${slide}`
}

export function elementText(element: SlideElement): string {
  const bodies = element.table
    ? element.table.rows.flatMap((row) => row.cells.flatMap((cell) => (cell ? [cell.text] : [])))
    : element.text
      ? [element.text]
      : []
  const parts = bodies.map((body) => body.paragraphs.map((paragraph) => paragraph.runs.map((run) => run.text).join('')).join('\n'))
  if (element.chartTitle) parts.push(element.chartTitle)
  return parts.join('\n')
}
