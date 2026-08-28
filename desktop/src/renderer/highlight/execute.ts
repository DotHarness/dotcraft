import type { HighlighterCore } from 'shiki/core'
import { plainLines, plainSegments, tokenizeSegments, tokenizeText } from './tokenize'
import type {
  DiffHighlightRequest,
  DiffHighlightResult,
  FileHighlightRequest,
  FileHighlightResult,
  HighlightedLine,
  HighlightSegment
} from './types'

export function executeFile(
  highlighter: HighlighterCore,
  request: FileHighlightRequest,
  lang: string | undefined
): FileHighlightResult {
  if (lang === undefined) return { lines: plainLines(request.contents), highlighted: false }
  return { lines: tokenizeText(highlighter, request.contents, lang), highlighted: true }
}

export function executeDiff(
  highlighter: HighlighterCore,
  request: DiffHighlightRequest,
  deletionLang: string | undefined,
  additionLang: string | undefined
): DiffHighlightResult {
  const side = (segments: HighlightSegment[], lang: string | undefined): {
    lines: HighlightedLine[]
    highlighted: boolean
  } => lang === undefined
    ? { lines: plainSegments(segments), highlighted: false }
    : { lines: tokenizeSegments(highlighter, segments, lang), highlighted: true }

  const deletion = side(request.deletion, deletionLang)
  const addition = side(request.addition, additionLang)

  return {
    deletion: deletion.lines,
    addition: addition.lines,
    highlighted: deletion.highlighted || addition.highlighted
  }
}
