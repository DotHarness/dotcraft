export interface HighlightSpan {
  text: string
  style?: Record<string, string>
  changed?: boolean
}

export type HighlightedLine = HighlightSpan[]

export interface CharRange {
  start: number
  end: number
}

export type DiffSide = 'deletion' | 'addition'

export interface HighlightSegment {
  text: string
  lineIndices: number[]
}

export interface FileHighlightRequest {
  cacheKey?: string
  name: string
  lang?: string
  contents: string
}

export interface FileHighlightResult {
  lines: HighlightedLine[]
  highlighted: boolean
}

export interface DiffHighlightRequest {
  cacheKey?: string
  name: string
  prevName?: string
  lang?: string
  deletion: HighlightSegment[]
  addition: HighlightSegment[]
}

export interface DiffHighlightResult {
  deletion: HighlightedLine[]
  addition: HighlightedLine[]
  highlighted: boolean
}

/** Beyond this, a line is one unstyled token: minified output, not source. */
export const TOKENIZE_MAX_LINE_LENGTH = 1000
export const MAX_LINE_DIFF_LENGTH = 1000

export const LIGHT_THEME = 'github-light'
export const DARK_THEME = 'github-dark'

export const TOKEN_VARIABLE_PREFIX = '--dc-token-'

// Both themes ride on every token as custom properties, so switching appearance
// repaints without re-tokenizing. `tokenizeTimeLimit: 0` disables shiki's
// per-line deadline; `tokenizeMaxLineLength` already bounds the bad case.
export const TOKEN_OPTIONS = {
  themes: { light: LIGHT_THEME, dark: DARK_THEME },
  defaultColor: false as const,
  cssVariablePrefix: TOKEN_VARIABLE_PREFIX,
  tokenizeMaxLineLength: TOKENIZE_MAX_LINE_LENGTH,
  tokenizeTimeLimit: 0
}
