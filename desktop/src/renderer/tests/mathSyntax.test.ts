import { describe, expect, it } from 'vitest'
import type { Nodes } from 'mdast'
import { fromMarkdown } from 'mdast-util-from-markdown'
import { gfmFromMarkdown } from 'mdast-util-gfm'
import { mathFromMarkdown } from 'mdast-util-math'
import { gfm } from 'micromark-extension-gfm'
import { mathSyntax } from '../components/conversation/mathSyntax'

function math(markdown: string): Array<[string, string]> {
  const found: Array<[string, string]> = []
  const visit = (node: Nodes): void => {
    if (node.type === 'math' || node.type === 'inlineMath') found.push([node.type, node.value])
    if ('children' in node) node.children.forEach(visit)
  }
  visit(fromMarkdown(markdown, {
    extensions: [gfm(), mathSyntax()],
    mdastExtensions: [gfmFromMarkdown(), mathFromMarkdown()]
  }))
  return found
}

describe('mathSyntax', () => {
  it('reads display math fenced by \\[ \\] or $$, including inside list items', () => {
    expect(math('Edge point:\n\\[\nE=\\frac38(A+B)\n\\]\n')).toEqual([['math', 'E=\\frac38(A+B)']])
    expect(math('$$\nx^2\n$$')).toEqual([['math', 'x^2']])
    expect(math('- **Edge**:\n  \\[\n  w=\\frac58\n  \\]\n- next')).toEqual([['math', 'w=\\frac58']])
  })

  it('reads inline math in \\( \\), \\[ \\] and $$ but leaves a single $ as text', () => {
    expect(math('neighbours \\(n\\), then \\[P\'=(1-w)P\\] and $$a_1$$')).toEqual([
      ['inlineMath', 'n'],
      ['inlineMath', "P'=(1-w)P"],
      ['inlineMath', 'a_1']
    ])
    expect(math('It costs $5 and $10, or $$$ in total.')).toEqual([])
  })

  it('keeps commands and escaped backslashes inside a formula', () => {
    expect(math('\\(\\overline{P}\\,\\cos\\frac{2\\pi}{n}\\)')).toEqual([['inlineMath', '\\overline{P}\\,\\cos\\frac{2\\pi}{n}']])
    expect(math('\\\\(not math\\)')).toEqual([])
  })

  it('leaves code and unclosed delimiters untouched', () => {
    expect(math('`\\(x\\)` and\n```\n\\[\ny\n\\]\n```')).toEqual([])
    expect(math('streaming \\(E=\\frac38')).toEqual([])
  })

  it('reads formulas in table cells', () => {
    expect(math('| a | b |\n|---|---|\n| \\(x\\) | $$y$$ |')).toEqual([['inlineMath', 'x'], ['inlineMath', 'y']])
  })
})
