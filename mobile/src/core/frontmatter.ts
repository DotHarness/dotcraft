export interface FrontmatterEntry {
  key: string
  value: string | string[]
}

export interface Frontmatter {
  entries: FrontmatterEntry[]
  end: number
}

const KEY = /^("[^"]*"|'[^']*'|[^\s:#-][^:]*?|-[^\s:][^:]*?)[ \t]*:(?:[ \t]+(.*))?$/
const ITEM = /^-(?:[ \t]+(.*))?$/
const BLOCK_SCALAR = /^[|>][+-]?$/

function unquote(value: string): string {
  const text = value.trim()
  if (text.length >= 2 && (text[0] === '"' || text[0] === "'") && text.endsWith(text[0])) return text.slice(1, -1)
  return text
}

function scalar(value: string): string | string[] {
  const text = value.trim()
  if (text.startsWith('[') && text.endsWith(']')) {
    return text.slice(1, -1).split(',').map(unquote).filter((item) => item.length > 0)
  }
  return unquote(text)
}

export function parseFrontmatter(text: string): Frontmatter | null {
  const lines = text.split('\n')
  if (lines[0]?.trimEnd() !== '---') return null
  const close = lines.findIndex((line, index) => index > 0 && line.trimEnd() === '---')
  if (close < 0) return null

  const entries: FrontmatterEntry[] = []
  const parents: { indent: number; key: string; entry: FrontmatterEntry }[] = []
  let block: { indent: number; entry: FrontmatterEntry; folded: boolean; lines: string[] } | null = null
  const finishBlock = () => {
    if (!block) return
    block.entry.value = block.lines.join(block.folded ? ' ' : '\n').trim()
    block = null
  }

  for (const raw of lines.slice(1, close)) {
    const line = raw.replace(/\r$/, '')
    const content = line.trim()
    const indent = line.length - line.trimStart().length
    if (block) {
      if (!content || indent > block.indent) {
        block.lines.push(content)
        continue
      }
      finishBlock()
    }
    if (!content || content.startsWith('#')) continue

    const item = ITEM.exec(content)
    if (item) {
      const last = entries[entries.length - 1]
      if (last && (Array.isArray(last.value) || last.value === '')) {
        last.value = [...(Array.isArray(last.value) ? last.value : []), unquote(item[1] ?? '')]
      }
      continue
    }

    const pair = KEY.exec(content)
    if (!pair) {
      const last = entries[entries.length - 1]
      if (last && typeof last.value === 'string' && last.value) last.value = `${last.value} ${content}`
      continue
    }
    while (parents.length > 0 && parents[parents.length - 1]!.indent >= indent) parents.pop()
    const parent = parents[parents.length - 1]
    const container = parent ? entries.indexOf(parent.entry) : -1
    if (container >= 0 && parent!.entry.value === '') entries.splice(container, 1)
    const key = [...parents.map((entry) => entry.key), unquote(pair[1]!)].join('.')
    const value = pair[2]?.trim() ?? ''
    const entry: FrontmatterEntry = { key, value: BLOCK_SCALAR.test(value) ? '' : scalar(value) }
    entries.push(entry)
    if (BLOCK_SCALAR.test(value)) block = { indent, entry, folded: value.startsWith('>'), lines: [] }
    else if (!value) parents.push({ indent, key: unquote(pair[1]!), entry })
  }
  finishBlock()

  if (entries.length === 0) return null
  const end = lines.slice(0, close + 1).reduce((sum, line) => sum + line.length + 1, 0)
  return { entries, end: Math.min(end, text.length) }
}
