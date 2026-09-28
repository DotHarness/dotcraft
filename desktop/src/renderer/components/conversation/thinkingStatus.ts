import { fromMarkdown } from 'mdast-util-from-markdown'
import { toString } from 'mdast-util-to-string'

export function extractThinkingStatus(reasoning: string): string | undefined {
  const lines = reasoning.trimEnd().split(/\r?\n/)
  let lastLine: string | undefined
  for (let index = lines.length - 1; index >= 0; index--) {
    const line = lines[index].trim()
    if (line && !isCommentOnly(line)) {
      lastLine = line
      break
    }
  }
  if (!lastLine) return undefined

  try {
    const tree = fromMarkdown(lastLine)
    const first = tree.children[0]
    if (first?.type === 'paragraph' && first.children.length === 1 && first.children[0].type === 'strong') {
      const text = toString(first.children[0]).trim()
      return extractThinkingStatus(text) ?? (text || undefined)
    }
    return toString(tree).trim() || undefined
  } catch {
    return undefined
  }
}

function isCommentOnly(line: string): boolean {
  let remaining = line
  while (remaining.startsWith('<!--')) {
    const end = remaining.indexOf('-->')
    if (end === -1) return true
    remaining = remaining.slice(end + 3).trimStart()
  }
  return !remaining || '<!--'.startsWith(remaining)
}
