const FENCE = /^ {0,3}(`{3,}|~{3,})(.*)$/
const BLANK_LINE = /^[ \t]*$/
const LIST_MARKER = /^([ \t]*)[-+*](?=[ \t]|$)/
const TASK_BOX = /^\[[ xX]\]/
const PARTIAL_IMAGE = /(^|\n)[^\S\n]*!\[[^\]\n]*(?:\](?:\([^)\n]*)?)?\s*$/

interface Fence { marker: string; length: number }

export function repairStreamingMarkdown(text: string): string {
  const tailStart = findOpenTail(text)
  if (tailStart == null) return text
  const head = text.slice(0, tailStart)
  let tail = text.slice(tailStart).replace(PARTIAL_IMAGE, '$1')
  tail = holdBackPartialLink(tail)
  tail = closeCodeSpan(tail)
  tail = closeEmphasis(tail, '*')
  tail = closeEmphasis(tail, '**')
  return head + tail
}

function findOpenTail(text: string): number | null {
  let fence: Fence | null = null
  let tailStart = 0
  let offset = 0
  for (const line of text.split('\n')) {
    const next = offset + line.length + 1
    const match = FENCE.exec(line)
    if (fence) {
      if (match && match[1][0] === fence.marker && match[1].length >= fence.length && match[2].trim() === '') {
        fence = null
        tailStart = next
      }
    } else if (match && !(match[1][0] === '`' && match[2].includes('`'))) {
      fence = { marker: match[1][0], length: match[1].length }
    } else if (BLANK_LINE.test(line)) {
      tailStart = next
    }
    offset = next
  }
  return fence ? null : Math.min(tailStart, text.length)
}

function isEscaped(text: string, index: number): boolean {
  let slashes = 0
  for (let i = index - 1; i >= 0 && text[i] === '\\'; i--) slashes++
  return slashes % 2 === 1
}

/** Code-span ranges; an unclosed span runs to the end with `open` set. */
function codeSpans(text: string): { ranges: Array<[number, number]>; open: { start: number; length: number } | null } {
  const ranges: Array<[number, number]> = []
  let open: { start: number; length: number } | null = null
  for (let i = 0; i < text.length;) {
    if (text[i] !== '`') { i++; continue }
    let end = i + 1
    while (text[end] === '`') end++
    const length = end - i
    if (open) {
      if (open.length === length) {
        ranges.push([open.start, end])
        open = null
      }
    } else if (!isEscaped(text, i)) {
      open = { start: i, length }
    }
    i = end
  }
  if (open) ranges.push([open.start, text.length])
  return { ranges, open }
}

function maskForScan(text: string): string {
  const chars = text.split('')
  for (const [start, end] of codeSpans(text).ranges) chars.fill(' ', start, end)
  let lineStart = 0
  for (const line of chars.join('').split('\n')) {
    const marker = LIST_MARKER.exec(line)
    if (marker) chars[lineStart + marker[1].length] = ' '
    lineStart += line.length + 1
  }
  return chars.join('')
}

function holdBackPartialLink(text: string): string {
  const masked = maskForScan(text)
  const lineStart = masked.lastIndexOf('\n') + 1
  const listPrefix = /^\s*(?:[-+*]|\d+[.)])\s+/.exec(text.slice(lineStart))?.[0].length
  for (let open = masked.indexOf('['); open !== -1; open = masked.indexOf('[', open + 1)) {
    if (isEscaped(masked, open)) continue
    if (listPrefix != null && open === lineStart + listPrefix && TASK_BOX.test(masked.slice(open))) {
      open += 2
      continue
    }
    const close = matchingBracket(masked, open, '[', ']')
    if (close == null || close === masked.length - 1) return text.slice(0, open)
    const after = masked[close + 1]
    if (after === '(' || after === '[') {
      const end = matchingBracket(masked, close + 1, after, after === '(' ? ')' : ']')
      if (end == null) return text.slice(0, open)
      open = end
    } else {
      open = close
    }
  }
  return text
}

function matchingBracket(text: string, start: number, opener: string, closer: string): number | null {
  let depth = 0
  for (let i = start; i < text.length; i++) {
    if (text[i] === '\\') { i++; continue }
    if (text[i] === opener) depth++
    else if (text[i] === closer && --depth === 0) return i
  }
  return null
}

function closeCodeSpan(text: string): string {
  const { open } = codeSpans(text)
  if (!open) return text
  const content = text.slice(open.start + open.length)
  if (content.trim() === '') return text.slice(0, open.start)
  return text + '`'.repeat(open.length)
}

function closeEmphasis(text: string, marker: '*' | '**'): string {
  const masked = maskForScan(text)
  const positions: number[] = []
  for (let i = 0; i <= masked.length - marker.length;) {
    if (masked.startsWith(marker, i) && !isEscaped(masked, i) &&
      (marker === '**' || (masked[i - 1] !== '*' && masked[i + 1] !== '*'))) {
      positions.push(i)
      i += marker.length
    } else {
      i++
    }
  }
  if (positions.length % 2 === 0) return text
  const opener = positions[positions.length - 1]
  const rest = text.slice(opener + marker.length)
  if (rest.trim() === '') return text.slice(0, opener) + rest
  if (/^\s/.test(rest) || rest.includes('\n')) return text
  const trimmed = text.trimEnd()
  return trimmed + marker + text.slice(trimmed.length)
}
