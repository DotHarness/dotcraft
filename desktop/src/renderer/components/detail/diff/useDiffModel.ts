// Shared by both display modes, so switching between them reuses one cached
// tokenization rather than starting over.
import { useMemo } from 'react'
import {
  applyChangeRanges,
  buildDiffSides,
  computeWordDiff,
  diffCacheKey,
  languageFromPath,
  NO_ROW,
  plainLine,
  useDiffHighlight,
  type DiffSideModel,
  type HighlightedLine
} from '../../../highlight'
import type { FileDiff } from '../../../types/toolCall'
import type { DiffCell } from './diffRows'

export interface DiffModel {
  sides: DiffSideModel
  /** False while the rows are the diff's own plain text rather than tokens. */
  highlighted: boolean
  cacheKey: string
  lineFor: (cell: DiffCell) => HighlightedLine | undefined
}

export function useDiffModel(diff: FileDiff): DiffModel {
  const sides = useMemo(() => buildDiffSides(diff), [diff])

  const request = useMemo(() => {
    const lang = languageFromPath(diff.filePath)
    const texts = [...sides.deletion, ...sides.addition].map((segment) => segment.text)
    return {
      cacheKey: diffCacheKey(diff.filePath, undefined, lang, texts),
      name: diff.filePath,
      lang,
      deletion: sides.deletion,
      addition: sides.addition
    }
  }, [diff.filePath, sides])

  const highlighted = useDiffHighlight(request)

  const words = useMemo(
    () => computeWordDiff(
      diff.diffHunks,
      sides.deletionRow,
      sides.additionRow,
      sides.deletionText.length,
      sides.additionText.length
    ),
    [diff.diffHunks, sides]
  )

  // Word marks are applied to the plain text too, so they need not await the tokenizer.
  const deletionLines = useMemo(() => {
    const source = highlighted?.deletion ?? sides.deletionText.map(plainLine)
    return source.map((line, index) => applyChangeRanges(line, words.deletion[index]))
  }, [highlighted, sides.deletionText, words])

  const additionLines = useMemo(() => {
    const source = highlighted?.addition ?? sides.additionText.map(plainLine)
    return source.map((line, index) => applyChangeRanges(line, words.addition[index]))
  }, [highlighted, sides.additionText, words])

  return useMemo(() => ({
    sides,
    highlighted: highlighted?.highlighted === true,
    cacheKey: request.cacheKey,
    lineFor: (cell: DiffCell) => cell.sideRow === NO_ROW
      ? undefined
      : (cell.side === 'deletion' ? deletionLines : additionLines)[cell.sideRow]
  }), [additionLines, deletionLines, highlighted, request.cacheKey, sides])
}
