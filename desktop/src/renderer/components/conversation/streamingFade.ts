import styles from './streamingFade.module.css'

// react-markdown keys siblings by tag and order, so words already shown keep their
// element and never replay the fade.

type HastNode = {
  type: string
  tagName?: string
  value?: string
  properties?: Record<string, unknown>
  children?: HastNode[]
}

const SKIPPED_TAGS = new Set(['pre', 'code', 'svg', 'math', 'script', 'style'])
const FADED_TAGS = new Set(['li', 'hr', 'tr'])
const WHITESPACE = /^\s*$/

const wordSegmenter = typeof Intl.Segmenter === 'function'
  ? new Intl.Segmenter(undefined, { granularity: 'word' })
  : null

export function rehypeStreamingFade(): (tree: HastNode) => void {
  return (tree) => fadeChildren(tree)
}

function fadeChildren(node: HastNode): void {
  if (!node.children) return
  node.children = node.children.flatMap((child) => {
    if (child.type === 'text') return WHITESPACE.test(child.value ?? '') ? [child] : wrapWords(child.value ?? '')
    if (child.type !== 'element') return [child]
    // A class on inline code would make it render as a block, so it fades in a wrapper.
    if (child.tagName === 'code' && node.tagName !== 'pre') return [fadeSpan([child])]
    if (FADED_TAGS.has(child.tagName ?? '')) addFadeClass(child)
    if (!SKIPPED_TAGS.has(child.tagName ?? '') && !isMath(child)) fadeChildren(child)
    return [child]
  })
}

function wrapWords(text: string): HastNode[] {
  return splitWords(text).map((word) => fadeSpan([{ type: 'text', value: word }]))
}

function fadeSpan(children: HastNode[]): HastNode {
  return { type: 'element', tagName: 'span', properties: { className: [styles.fade] }, children }
}

export function splitWords(text: string): string[] {
  if (!wordSegmenter) return text.match(/\s*\S+\s*/g) ?? [text]
  const words: string[] = []
  for (const { segment, isWordLike } of wordSegmenter.segment(text)) {
    if ((!isWordLike || WHITESPACE.test(segment)) && words.length > 0) words[words.length - 1] += segment
    else words.push(segment)
  }
  return words
}

function addFadeClass(node: HastNode): void {
  const properties = (node.properties ??= {})
  const current = properties.className
  properties.className = [...(Array.isArray(current) ? current : current ? [current] : []), styles.fade]
}

function isMath(node: HastNode): boolean {
  const className = node.properties?.className
  const classes = Array.isArray(className) ? className : [className]
  return classes.some((name) => typeof name === 'string' && (name.startsWith('katex') || name.startsWith('math-')))
}
