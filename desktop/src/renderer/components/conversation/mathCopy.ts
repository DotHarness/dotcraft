import type { ClipboardEvent } from 'react'

const TEX_SOURCE = 'annotation[encoding="application/x-tex"]'
const BLOCKS = new Set(['P', 'DIV', 'PRE', 'BLOCKQUOTE', 'LI', 'UL', 'OL', 'TABLE', 'TR', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6'])

// Rendered KaTeX copies as glyph soup, so a selection that touches a formula copies its TeX.
export function copySelectionWithMathSource(event: ClipboardEvent<HTMLElement>): void {
  const selection = window.getSelection()
  if (!selection || selection.rangeCount === 0 || selection.isCollapsed) return

  const range = selection.getRangeAt(0).cloneRange()
  const startFormula = enclosingFormula(range.startContainer)
  if (startFormula) range.setStartBefore(startFormula)
  const endFormula = enclosingFormula(range.endContainer)
  if (endFormula) range.setEndAfter(endFormula)

  const fragment = range.cloneContents()
  if (!fragment.querySelector('.katex')) return
  for (const display of fragment.querySelectorAll('.katex-display')) {
    const block = document.createElement('div')
    block.textContent = `\\[${texSource(display)}\\]`
    display.replaceWith(block)
  }
  for (const inline of fragment.querySelectorAll('.katex')) {
    inline.replaceWith(`\\(${texSource(inline)}\\)`)
  }

  const container = document.createElement('div')
  container.append(fragment)
  event.clipboardData.setData('text/plain', plainText(container).replace(/\n{3,}/g, '\n\n').trim())
  event.clipboardData.setData('text/html', container.innerHTML)
  event.preventDefault()
}

function enclosingFormula(node: Node): Element | null {
  const element = node instanceof Element ? node : node.parentElement
  return element?.closest('.katex-display') ?? element?.closest('.katex') ?? null
}

function texSource(formula: Element): string {
  return formula.querySelector(TEX_SOURCE)?.textContent ?? formula.textContent ?? ''
}

function plainText(node: Node): string {
  if (node.nodeType === Node.TEXT_NODE) return (node as Text).data
  if (!(node instanceof Element)) return ''
  if (node.tagName === 'BR') return '\n'
  const text = [...node.childNodes].map(plainText).join('')
  if (node.tagName === 'TD' || node.tagName === 'TH') return node.nextElementSibling ? `${text}\t` : text
  return BLOCKS.has(node.tagName) ? `${text}\n` : text
}
