import type { ConversationTurn } from '../types/conversation'

export interface ModelReviewComment {
  id: string
  threadId: string
  turnId: string
  itemId: string
  path: string
  side: 'right'
  startLine: number
  endLine: number
  title: string
  body: string
  priority?: number
}

export function parseModelReviewComments(turns: ConversationTurn[]): ModelReviewComment[] {
  const comments: ModelReviewComment[] = []
  for (const turn of turns) {
    if (turn.status !== 'completed') continue
    for (const item of turn.items) {
      if (item.type !== 'agentMessage' || item.phase === 'commentary') continue
      for (const match of (item.text ?? '').matchAll(/::code-comment\{((?:[^"}]|"(?:\\.|[^"\\])*")*)\}/g)) {
        const fields: Record<string, string> = {}
        for (const attribute of match[1].matchAll(/(\w+)\s*=\s*("(?:\\.|[^"\\])*"|[^\s]+)/g)) {
          try { fields[attribute[1]] = attribute[2].startsWith('"') ? JSON.parse(attribute[2]) : attribute[2] } catch { /* Ignore malformed attributes. */ }
        }
        const startLine = Number(fields.start ?? 1)
        const endLine = Number(fields.end ?? startLine)
        if (!fields.file || !fields.body || !Number.isInteger(startLine) || startLine < 1 || !Number.isInteger(endLine) || endLine < startLine) continue
        const priority = Number(fields.priority)
        comments.push({ id: `${item.id}:${match.index}`, threadId: turn.threadId, turnId: turn.id, itemId: item.id,
          path: fields.file, side: 'right', startLine, endLine, title: fields.title ?? '', body: fields.body,
          ...(Number.isInteger(priority) && priority >= 0 && priority <= 3 ? { priority } : {}) })
      }
    }
  }
  return comments
}

export function modelReviewCommentsForFile(comments: ModelReviewComment[], filePath: string, workspacePath: string): ModelReviewComment[] {
  const normalize = (value: string): string => {
    const absolute = /^(?:[a-z]:[\\/]|\/)/i.test(value) ? value : `${workspacePath}/${value}`
    const segments: string[] = []
    for (const segment of absolute.replace(/\\/g, '/').split('/')) {
      if (segment === '.') continue
      if (segment === '..') segments.pop()
      else segments.push(segment)
    }
    const normalized = segments.join('/')
    return /^[a-z]:/i.test(normalized) ? normalized.toLowerCase() : normalized
  }
  const target = normalize(filePath)
  return comments.filter((comment) => normalize(comment.path) === target)
}

export function modelReviewCommentsAtLine(comments: ModelReviewComment[], side: 'left' | 'right', line: number): ModelReviewComment[] {
  return side === 'left' ? [] : comments.filter((comment) => line >= comment.startLine && line <= comment.endLine)
}
