// `react-markdown` hands components React elements rather than source, so the copy
// button, the mermaid branch, and the highlighter share these extractors.
import { isValidElement, type ReactElement, type ReactNode } from 'react'

export function extractText(node: ReactNode): string {
  if (typeof node === 'string') return node
  if (typeof node === 'number') return String(node)
  if (!node) return ''
  if (Array.isArray(node)) return node.map(extractText).join('')
  if (typeof node === 'object' && 'props' in (node as ReactElement)) {
    return extractText((node as ReactElement<{ children?: ReactNode }>).props.children)
  }
  return ''
}

/** The fence's info string, from the `language-*` class react-markdown sets. */
export function getCodeBlockLanguage(node: ReactNode): string | null {
  if (Array.isArray(node)) {
    for (const child of node) {
      const language = getCodeBlockLanguage(child)
      if (language) return language
    }
    return null
  }

  if (!isValidElement<{ className?: string; children?: ReactNode }>(node)) return null

  const className = node.props.className ?? ''
  const match = /(?:^|\s)language-([^\s]+)/.exec(className)
  return match?.[1]?.toLowerCase() ?? getCodeBlockLanguage(node.props.children)
}

export function isMermaidLanguage(language: string): boolean {
  return language === 'mermaid' || language === 'mmd'
}
