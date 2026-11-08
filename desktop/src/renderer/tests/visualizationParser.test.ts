import { describe, expect, it } from 'vitest'
import { isValidVisualizationPath } from '../components/conversation/visualizationParser'
import { assistantMessageCopyText, parseVisualizationContentReferences, splitAssistantMessage } from '../components/conversation/assistantMessageSegments'

const CHART_PATH = 'Sales_报告 v2.HTML'
const wrapped = (json: string): string => `${String.fromCharCode(0xe200)}visualize${String.fromCharCode(0xe202)}${json}${String.fromCharCode(0xe201)}`

describe('visualization content references', () => {
  it('parses both reference forms on their own lines outside fences', () => {
    const markdown = [
      'Before',
      `visualize${JSON.stringify({ path: CHART_PATH, title: 'Sales by region' })}`,
      '```',
      `visualize${JSON.stringify({ path: 'hidden.html' })}`,
      '```',
      `  ${wrapped(JSON.stringify({ path: 'beta.html', mode: 'wide' }))}  \r`,
      'After'
    ].join('\n')

    expect(parseVisualizationContentReferences(markdown).map(({ path, title, mode }) => ({ path, title, mode }))).toEqual([
      { path: CHART_PATH, title: 'Sales by region', mode: undefined },
      { path: 'beta.html', title: undefined, mode: 'wide' }
    ])
  })

  it('keeps misplaced references and examples as ordinary text', () => {
    for (const markdown of [
      'text visualize{"path":"chart.html"}',
      'visualize{"path":"chart.html"} text',
      'visualize["chart.html"]',
      '```text\nvisualize{"path":"chart_1.html"}\n```',
      'visualize{"path":'
    ]) {
      expect(parseVisualizationContentReferences(markdown), markdown).toEqual([])
      expect(assistantMessageCopyText(markdown)).toBe(markdown)
    }
  })

  it('ignores unknown fields without relaxing recognized fields', () => {
    const markdown = `visualize${JSON.stringify({ path: CHART_PATH, extra: { chart: true }, caption: 1 })}`
    expect(parseVisualizationContentReferences(markdown)).toEqual([{ path: CHART_PATH }])
  })

  it('identifies complete invalid references for a repair notice without mounting while streaming', () => {
    for (const markdown of [
      'visualize{"path":"chart.html",}',
      "visualize{'path':'chart.html'}",
      'visualize{"path":"chart.html","mode":"full"}',
      'visualize{"path":"chart.html","title":" "}',
      'visualize{"path":"chart.html","title":1}',
      `visualize{"path":"chart.html","title":"${'a'.repeat(201)}"}`,
      wrapped('{"path":"chart.html","title":null}'),
      'visualize{}',
      'visualize{"path":"../other/chart.html"}',
      `visualize${JSON.stringify({ path: 'folder\\chart.html' })}`
    ]) {
      expect(parseVisualizationContentReferences(markdown), markdown).toEqual([])
      expect(splitAssistantMessage(markdown, { streaming: false, visualizations: 'render' }))
        .toEqual([{ kind: 'invalidVisualization', start: 0, text: markdown }])
      expect(splitAssistantMessage(`${markdown}\n`, { streaming: true, visualizations: 'strip' })).toEqual([])
      expect(assistantMessageCopyText(markdown)).toBe('')
    }
  })

  it('accepts HTML file names without accepting directories', () => {
    for (const path of ['chart.html', 'chart_1.html', CHART_PATH]) {
      expect(isValidVisualizationPath(path), path).toBe(true)
    }
    for (const path of ['', 'chart.htm', 'chart.html\n', 'chart\0.html', '../chart.html', 'folder/chart.html', 'folder\\chart.html']) {
      expect(isValidVisualizationPath(path), path).toBe(false)
    }
  })
})
