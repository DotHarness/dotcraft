import { fromMarkdown } from 'mdast-util-from-markdown'
import { toString } from 'mdast-util-to-string'

const HEADING_START = /^(#{1,6}\s|\*\*|__)/

export function extractThinkingStatus(reasoning: string, complete = false): string | undefined {
  return latestHeading(reasoning) ?? latestParagraphLead(reasoning, complete)
}

function latestHeading(reasoning: string): string | undefined {
  const lines = reasoning.split(/\r?\n/)
  for (let index = lines.length - 1; index >= 0; index--) {
    const heading = headingOf(lines[index].trim())
    if (heading) return heading
  }
  return undefined
}

function headingOf(line: string): string | undefined {
  if (!HEADING_START.test(line)) return undefined
  try {
    const [first] = fromMarkdown(line).children
    if (first?.type === 'heading') return toString(first).trim() || undefined
    if (first?.type === 'paragraph' && first.children.length === 1 && first.children[0].type === 'strong') {
      const text = toString(first.children[0]).trim()
      return headingOf(text) ?? (text || undefined)
    }
  } catch {
    return undefined
  }
  return undefined
}

function latestParagraphLead(reasoning: string, complete: boolean): string | undefined {
  const paragraphs = reasoning.split(/\r?\n[\t ]*\r?\n/)
  for (let index = paragraphs.length - 1; index >= 0; index--) {
    const paragraph = paragraphs[index].trimStart()
    const newline = paragraph.search(/\r?\n/)
    if (newline === -1 && index === paragraphs.length - 1 && !complete) continue
    const lead = plainText(newline === -1 ? paragraph : paragraph.slice(0, newline))
    if (lead) return lead
  }
  return undefined
}

function plainText(line: string): string | undefined {
  const text = line.replace(/<!--.*?(?:-->|$)/g, '').trim()
  if (!text) return undefined
  try {
    return toString(fromMarkdown(text)).trim() || undefined
  } catch {
    return undefined
  }
}
