// Ranges go to the CSS Custom Highlight API rather than inserted `<mark>` elements,
// so find never mutates the text nodes React owns.

export const FIND_HIGHLIGHT = 'dc-find-match'
export const FIND_ACTIVE_HIGHLIGHT = 'dc-find-active'

// Gutters are the important entry: without them, searching "12" would match every
// twelfth line number in the file.
const SKIP_SELECTOR = [
  'script',
  'style',
  'textarea',
  '[contenteditable="true"]',
  '[data-find-skip]',
  '[data-line-num]',
  '[data-column-number]'
].join(', ')

// jsdom has no highlight registry, so tests exercise the model layer and skip painting.
function registry(): HighlightRegistry | undefined {
  const css = (globalThis as { CSS?: { highlights?: HighlightRegistry } }).CSS
  return css?.highlights !== undefined && typeof Highlight !== 'undefined' ? css.highlights : undefined
}

export function canDecorate(): boolean {
  return registry() !== undefined
}

export function collectTextNodes(root: Node): Text[] {
  const doc = root.ownerDocument ?? (root as Document)
  const walker = doc.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
    acceptNode(node: Node): number {
      const parent = (node as Text).parentElement
      if (parent === null) return NodeFilter.FILTER_REJECT
      return parent.closest(SKIP_SELECTOR) === null
        ? NodeFilter.FILTER_ACCEPT
        : NodeFilter.FILTER_REJECT
    }
  })

  const nodes: Text[] = []
  for (let node = walker.nextNode(); node !== null; node = walker.nextNode()) {
    if (node.nodeType === Node.TEXT_NODE) nodes.push(node as Text)
  }
  return nodes
}

// Text nodes are concatenated first, so a match spanning two syntax-highlight tokens
// is found as one range rather than missed at the node boundary.
export function rangesIn(root: Element, query: string): Range[] {
  if (query.length === 0) return []
  const nodes = collectTextNodes(root)
  if (nodes.length === 0) return []

  const spans: Span[] = []
  let text = ''
  for (const node of nodes) {
    const value = node.data
    spans.push({ node, start: text.length, end: text.length + value.length })
    text += value
  }

  const haystack = text.toLowerCase()
  const needle = query.toLowerCase()
  const ranges: Range[] = []
  let cursor = 0
  // Matches come out in ascending order, so the span search only moves forward;
  // rescanning from the start for each would be quadratic on a common query.
  let spanCursor = 0

  while (cursor <= haystack.length - needle.length) {
    const start = haystack.indexOf(needle, cursor)
    if (start === -1) break
    const end = start + needle.length
    while (spanCursor < spans.length && spans[spanCursor].end <= start) spanCursor++
    const from = spans[spanCursor]
    let endCursor = spanCursor
    while (endCursor < spans.length && spans[endCursor].end < end) endCursor++
    const to = spans[endCursor]
    if (from !== undefined && to !== undefined) {
      const range = document.createRange()
      range.setStart(from.node, start - from.start)
      range.setEnd(to.node, end - to.start)
      ranges.push(range)
    }
    cursor = end
  }

  return ranges
}

interface Span {
  node: Text
  start: number
  end: number
}

export function applyHighlights(ranges: Range[], active: Range | undefined): void {
  const highlights = registry()
  if (highlights === undefined) return
  // The active range gets only the active highlight, so the two styles cannot both
  // apply and leave the winner up to registration order.
  const rest = active === undefined ? ranges : ranges.filter((range) => range !== active)
  highlights.set(FIND_HIGHLIGHT, new Highlight(...rest))
  highlights.set(FIND_ACTIVE_HIGHLIGHT, active === undefined ? new Highlight() : new Highlight(active))
}

export function clearHighlights(): void {
  const highlights = registry()
  if (highlights === undefined) return
  highlights.delete(FIND_HIGHLIGHT)
  highlights.delete(FIND_ACTIVE_HIGHLIGHT)
}
