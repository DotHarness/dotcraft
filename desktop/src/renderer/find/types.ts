// The model layer searches the text a surface holds, because a DOM-only search would
// miss results in a virtualized surface that renders only the rows near the viewport.

export type FindDomain = 'file' | 'diff' | 'conversation'

export interface FindSegment {
  key: string
  rowIndex?: number
  /** Matched as `[data-line="<value>"]` on the rendered row. */
  lineId?: string
  scopeSelector?: string
  text: string
}

export interface FindMatchContext {
  before: string
  match: string
  after: string
}

export interface FindMatch {
  id: string
  surfaceId: string
  domain: FindDomain
  segmentKey: string
  rowIndex: number | undefined
  lineId: string | undefined
  scopeSelector: string | undefined
  /** Character offsets within the segment's text. */
  start: number
  end: number
  /** Which occurrence this is within its own segment. */
  occurrence: number
  context: FindMatchContext
}

export interface FindSurface {
  id: string
  domain: FindDomain
  /** Higher is searched first. */
  priority: number
  getSegments: () => FindSegment[]
  getContainer: () => HTMLElement | null
  reveal?: (match: FindMatch) => void
  /** For surfaces whose blocks carry no `data-line`; falls back to a `data-line` lookup. */
  resolveElement?: (match: FindMatch) => HTMLElement | null
}

/** A query like "e" must not build a million objects. */
export const MAX_FIND_MATCHES = 2000

export const FIND_CONTEXT_LENGTH = 24
