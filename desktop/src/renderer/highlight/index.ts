export { diffCacheKey, fileCacheKey } from './cacheKey'
export { buildDiffSides, NO_ROW, type DiffSideModel } from './diffSides'
export { isPlainLanguage, languageFromPath, resolveLanguage, PLAIN_LANGUAGE } from './languages'
export { HighlighterPool, type HighlighterPoolStats } from './pool/highlighterPool'
export { HighlightProvider, useHighlighterPool } from './react/HighlightProvider'
export { useDiffHighlight, useFileHighlight } from './react/useHighlight'
export { applyChangeRanges } from './spans'
export { normalizeNewlines, plainLine, plainLines, splitLines } from './tokenize'
export { computeWordDiff, pairChangedLines, type WordDiffRanges } from './wordDiff'
export type {
  CharRange,
  DiffHighlightRequest,
  DiffHighlightResult,
  DiffSide,
  FileHighlightRequest,
  FileHighlightResult,
  HighlightedLine,
  HighlightSegment,
  HighlightSpan
} from './types'
