import { describe, expect, it } from 'vitest'
import { hideStreamingVisualizationTail, parseInlineVisualizations, stripInlineVisualizationDirectives } from '../components/conversation/inlineVisualizationParser'

describe('inline visualization directives', () => {
  it('parses exact standalone directives outside fences', () => {
    const markdown = 'Before\n::dotcraft-inline-vis{file="alpha-chart.html"}\n```\n::dotcraft-inline-vis{file="hidden.html"}\n```\nAfter'
    expect(parseInlineVisualizations(markdown).map(item => item.file)).toEqual(['alpha-chart.html'])
  })

  it('rejects inline, unsafe, extra-attribute, and wrong-quote forms', () => {
    const markdown = [
      'text ::dotcraft-inline-vis{file="inline.html"}',
      '::dotcraft-inline-vis{file="../unsafe.html"}',
      '::dotcraft-inline-vis{file="ok.html" title="x"}',
      "::dotcraft-inline-vis{file='wrong.html'}"
    ].join('\n')
    expect(parseInlineVisualizations(markdown)).toEqual([])
  })

  it('removes internal directives from copied text', () => {
    expect(stripInlineVisualizationDirectives('Before\n::dotcraft-inline-vis{file="chart.html"}\nAfter')).toBe('Before\nAfter')
  })

  it('hides a possibly incomplete streaming directive tail', () => {
    expect(hideStreamingVisualizationTail('Visible\n::dotcraft-inline-vis{fi')).toBe('Visible')
  })

  it('keeps directives from other products as ordinary text', () => {
    const markdown = '::other-inline-vis{file="chart.html"}'
    expect(parseInlineVisualizations(markdown)).toEqual([])
    expect(stripInlineVisualizationDirectives(markdown)).toBe(markdown)
  })
})
