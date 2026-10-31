import type { ComposerContextRecord, FileAnnotationContext, LineCommentContext } from './composerContext'

const SECTION_HEADING = '# Diff comments:'
const BLOCK_HEADING = '## User Comment'
const SECTION_START = `${SECTION_HEADING}\n\n${BLOCK_HEADING} `
const BLOCK_LINE = /^## User Comment \d+$/
const HUNK_FIELD = 'Diff hunk:'
const HUNK_OPEN = '```diff'
const HUNK_CLOSE = '```'
const ESCAPABLE_LINE = /^\\*## User Comment /

export function isLineCommentContext(context: ComposerContextRecord): context is LineCommentContext {
  return context.kind === 'diffAnnotation' || context.kind === 'fileAnnotation'
}

export function formatMessageComments(comments: readonly LineCommentContext[]): string {
  return [SECTION_HEADING, ...comments.map((comment, index) => formatComment(comment, index + 1))].join('\n\n')
}

export function splitMessageComments(text: string): { text: string; comments: LineCommentContext[] } {
  const start = sectionStart(text)
  if (start < 0) return { text, comments: [] }
  const comments = parseBlocks(text.slice(start + SECTION_HEADING.length))
  return comments.length > 0 ? { text: text.slice(0, start).trimEnd(), comments } : { text, comments: [] }
}

function formatComment(comment: LineCommentContext, number: number): string {
  const fields = comment.kind === 'diffAnnotation'
    ? [
        `File: ${comment.path}`,
        `Side: ${comment.side === 'left' ? 'L' : 'R'}`,
        `Lines: ${comment.startLine === comment.endLine ? comment.startLine : `${comment.startLine}-${comment.endLine}`}`,
        ...(comment.diffHunk?.trim() ? [HUNK_FIELD, HUNK_OPEN, comment.diffHunk, HUNK_CLOSE] : [])
      ]
    : [
        `Artifact path: ${comment.path}`,
        `Artifact type: ${comment.targetKind === 'range' || comment.targetKind === 'object' ? 'workbook' : 'presentation'}`,
        `Annotation target: ${comment.target}`,
        `Artifact annotation: ${describeTarget(comment)}`
      ]
  const body = comment.comment.trim().split('\n').map((line) => (ESCAPABLE_LINE.test(line) ? `\\${line}` : line))
  return [`${BLOCK_HEADING} ${number}`, ...fields, 'Comment:', ...body].join('\n')
}

function describeTarget({ targetKind, location }: FileAnnotationContext): string {
  switch (targetKind) {
    case 'range': {
      const size = location.rowCount && location.columnCount
        ? ` with ${location.rowCount} rows x ${location.columnCount} columns`
        : ''
      return `range ${location.sheet}!${location.range}${size}`
    }
    case 'object':
      return `object ${location.elementId} on ${location.sheet}`
    case 'element':
      return `element selection on Slide ${location.slide}; selected element ids: ${location.elementId}`
    case 'slide':
      return `slide selection on Slide ${location.slide}`
  }
}

function sectionStart(text: string): number {
  let index = text.lastIndexOf(SECTION_START)
  while (index > 0 && text[index - 1] !== '\n') index = text.lastIndexOf(SECTION_START, index - 1)
  return index
}

function parseBlocks(section: string): LineCommentContext[] {
  const blocks: string[][] = []
  for (const line of section.split('\n')) {
    if (BLOCK_LINE.test(line)) blocks.push([])
    else blocks.at(-1)?.push(line)
  }
  return blocks.flatMap((lines, index) => {
    const comment = parseBlock(lines, `comment-${index + 1}`)
    return comment ? [comment] : []
  })
}

function parseBlock(lines: string[], id: string): LineCommentContext | null {
  const bodyAt = lines.indexOf('Comment:')
  if (bodyAt < 0) return null
  const hunkAt = lines.indexOf(HUNK_FIELD)
  const hunkEnd = lines[hunkAt + 1] === HUNK_OPEN ? lines.indexOf(HUNK_CLOSE, hunkAt + 2) : -1
  const hasHunk = hunkAt >= 0 && hunkEnd >= 0 && hunkEnd < bodyAt
  const fields = lines.slice(0, hasHunk ? hunkAt : bodyAt)
  const field = (name: string) => fields.find((line) => line.startsWith(`${name}: `))?.slice(name.length + 2).trim()
  const comment = lines.slice(bodyAt + 1)
    .map((line) => (/^\\+## User Comment /.test(line) ? line.slice(1) : line))
    .join('\n')
    .trim()

  const artifactPath = field('Artifact path')
  if (artifactPath) {
    const target = field('Annotation target')
    const located = parseTarget(field('Artifact annotation') ?? '')
    return target && located ? { id, kind: 'fileAnnotation', path: artifactPath, target, ...located, comment } : null
  }

  const path = field('File')
  const side = field('Side')
  const range = /^(\d+)(?:-(\d+))?$/.exec(field('Lines') ?? '')
  if (!path || (side !== 'L' && side !== 'R') || !range) return null
  return {
    id,
    kind: 'diffAnnotation',
    path,
    side: side === 'L' ? 'left' : 'right',
    startLine: Number(range[1]),
    endLine: Number(range[2] ?? range[1]),
    ...(hasHunk ? { diffHunk: lines.slice(hunkAt + 2, hunkEnd).join('\n') } : {}),
    comment
  }
}

function parseTarget(text: string): Pick<FileAnnotationContext, 'targetKind' | 'location'> | null {
  const range = /^range (.+)!(\S+?)(?: with (\d+) rows x (\d+) columns)?$/.exec(text)
  if (range) {
    return {
      targetKind: 'range',
      location: {
        sheet: range[1],
        range: range[2],
        ...(range[3] && range[4] ? { rowCount: Number(range[3]), columnCount: Number(range[4]) } : {})
      }
    }
  }
  const object = /^object (.+?) on (.+)$/.exec(text)
  if (object) return { targetKind: 'object', location: { sheet: object[2], elementId: object[1] } }
  const element = /^element selection on Slide (\d+); selected element ids: (.+)$/.exec(text)
  if (element) return { targetKind: 'element', location: { slide: Number(element[1]), elementId: element[2] } }
  const slide = /^slide selection on Slide (\d+)$/.exec(text)
  return slide ? { targetKind: 'slide', location: { slide: Number(slide[1]) } } : null
}
