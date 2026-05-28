import type { FileDiff } from '../types/toolCall'
import { computeDiffHunks } from './diffExtractor'
import { extractPartialJsonStringValue } from './toolCallDisplay'

type FileToolName = 'WriteFile' | 'EditFile'

interface StreamingDiffOptions {
  toolName: FileToolName
  turnId: string
  argumentsPreview: string
  filePath: string | null
  baselineContent?: string | null
}

function extractPartialJsonIntValue(json: string, key: string): number | null {
  const keyPattern = `"${key}"`
  const keyIndex = json.indexOf(keyPattern)
  if (keyIndex < 0) return null
  const colonIndex = json.indexOf(':', keyIndex + keyPattern.length)
  if (colonIndex < 0) return null

  let i = colonIndex + 1
  while (i < json.length && /\s/.test(json[i])) i += 1
  if (i >= json.length) return null

  let sign = 1
  if (json[i] === '-') {
    sign = -1
    i += 1
  }

  const start = i
  while (i < json.length && /[0-9]/.test(json[i])) i += 1
  if (i === start) return null

  const value = Number.parseInt(json.slice(start, i), 10)
  if (!Number.isFinite(value)) return null
  return sign * value
}

function replaceSingleOccurrence(source: string, oldText: string, newText: string): string {
  if (!oldText) return source
  const index = source.indexOf(oldText)
  if (index < 0) return source.replace(oldText, newText)
  return source.slice(0, index) + newText + source.slice(index + oldText.length)
}

function applyLineRangeEdit(
  baseline: string,
  startLineRaw: number | null,
  endLineRaw: number | null,
  replacementText: string
): string {
  if (startLineRaw == null || endLineRaw == null) return baseline

  const startLine = Math.max(1, Math.floor(startLineRaw))
  const endLine = Math.max(startLine, Math.floor(endLineRaw))
  const hasTrailingNewline = baseline.endsWith('\n')
  const baselineLines = baseline.split('\n')
  if (hasTrailingNewline) {
    baselineLines.pop()
  }

  const replacementLines = replacementText.split('\n')
  if (replacementLines[replacementLines.length - 1] === '') {
    replacementLines.pop()
  }

  const startIndex = Math.min(baselineLines.length, startLine - 1)
  const deleteCount = Math.max(0, Math.min(baselineLines.length, endLine) - startIndex)
  baselineLines.splice(startIndex, deleteCount, ...replacementLines)

  const joined = baselineLines.join('\n')
  return hasTrailingNewline ? `${joined}\n` : joined
}

function buildFileDiff(
  filePath: string,
  turnId: string,
  originalContent: string,
  currentContent: string
): FileDiff {
  const { hunks, additions, deletions } = computeDiffHunks(originalContent, currentContent)
  return {
    filePath,
    turnId,
    turnIds: [turnId],
    additions,
    deletions,
    diffHunks: hunks,
    status: 'written',
    isNewFile: originalContent === '',
    originalContent,
    currentContent
  }
}

function buildEditFileDiffWithBaseline(
  filePath: string,
  turnId: string,
  baselineContent: string,
  argumentsPreview: string
): FileDiff | null {
  const startLine = extractPartialJsonIntValue(argumentsPreview, 'startLine')
  const endLine = extractPartialJsonIntValue(argumentsPreview, 'endLine')
  const rangeReplacement = extractPartialJsonStringValue(argumentsPreview, 'newText')
    ?? extractPartialJsonStringValue(argumentsPreview, 'content')

  if (startLine != null && endLine != null && rangeReplacement != null) {
    const nextContent = applyLineRangeEdit(baselineContent, startLine, endLine, rangeReplacement)
    return buildFileDiff(filePath, turnId, baselineContent, nextContent)
  }

  const oldText = extractPartialJsonStringValue(argumentsPreview, 'oldText') ?? ''
  const newText = extractPartialJsonStringValue(argumentsPreview, 'newText')
    ?? extractPartialJsonStringValue(argumentsPreview, 'content')
    ?? ''

  if (!oldText && !newText) return null

  const nextContent = oldText
    ? replaceSingleOccurrence(baselineContent, oldText, newText)
    : baselineContent
  return buildFileDiff(filePath, turnId, baselineContent, nextContent)
}

function buildEditFileFallbackDiff(
  filePath: string,
  turnId: string,
  argumentsPreview: string
): FileDiff | null {
  const oldText = extractPartialJsonStringValue(argumentsPreview, 'oldText') ?? ''
  const newText = extractPartialJsonStringValue(argumentsPreview, 'newText')
    ?? extractPartialJsonStringValue(argumentsPreview, 'content')
    ?? ''
  if (!oldText && !newText) return null
  return buildFileDiff(filePath, turnId, oldText, newText)
}

export function extractStreamingFilePath(argumentsPreview: string): string | null {
  return extractPartialJsonStringValue(argumentsPreview, 'path')
}

export function computeStreamingFileDiff(options: StreamingDiffOptions): FileDiff | null {
  const { toolName, turnId, argumentsPreview, baselineContent } = options
  const filePath = options.filePath ?? extractStreamingFilePath(argumentsPreview)
  if (!filePath) return null

  if (toolName === 'WriteFile') {
    const content = extractPartialJsonStringValue(argumentsPreview, 'content')
    if (content == null) return null
    const originalContent = baselineContent ?? ''
    return buildFileDiff(filePath, turnId, originalContent, content)
  }

  if (baselineContent != null) {
    const withBaseline = buildEditFileDiffWithBaseline(filePath, turnId, baselineContent, argumentsPreview)
    if (withBaseline) return withBaseline
  }

  return buildEditFileFallbackDiff(filePath, turnId, argumentsPreview)
}
