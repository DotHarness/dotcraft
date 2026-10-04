import type { ClipboardEvent } from 'react'

const TEX_SOURCE = 'annotation[encoding="application/x-tex"]'
const PARAGRAPHS = new Set(['P', 'PRE', 'BLOCKQUOTE', 'UL', 'OL', 'TABLE', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6'])
const LINES = new Set(['LI', 'TR'])
const CONTAINERS = new Set(['DIV', 'UL', 'OL', 'LI', 'BLOCKQUOTE', 'TABLE', 'THEAD', 'TBODY', 'TR'])

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
    const block = document.createElement('p')
    block.textContent = `\\[${texSource(display)}\\]`
    display.replaceWith(block)
  }
  for (const inline of fragment.querySelectorAll('.katex')) {
    inline.replaceWith(`\\(${texSource(inline)}\\)`)
  }

  const container = document.createElement('div')
  container.append(fragment)
  event.clipboardData.setData('text/plain', plainText(container))
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

// Line breaks between blocks take the largest one asked for and never touch the text itself,
// so code keeps its blank lines.
function plainText(root: Node): string {
  let text = ''
  let breaks = 0

  const write = (value: string): void => {
    if (value === '') return
    if (text !== '') text += '\n'.repeat(breaks)
    breaks = 0
    text += value
  }

  const visit = (node: Node): void => {
    if (node.nodeType === Node.TEXT_NODE) {
      const value = (node as Text).data
      const parent = node.parentElement
      if (parent && CONTAINERS.has(parent.tagName) && value.trim() === '') return
      write(value)
      return
    }
    if (!(node instanceof Element)) return
    if (node.tagName === 'BR') {
      write('\n')
      return
    }
    const required = PARAGRAPHS.has(node.tagName) ? 2 : LINES.has(node.tagName) ? 1 : 0
    breaks = Math.max(breaks, required)
    node.childNodes.forEach(visit)
    if ((node.tagName === 'TD' || node.tagName === 'TH') && node.nextElementSibling) write('\t')
    breaks = Math.max(breaks, required)
  }

  visit(root)
  return text
}
