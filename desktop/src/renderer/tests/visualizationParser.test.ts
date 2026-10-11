import { describe, expect, it } from 'vitest'
import { isValidVisualizationPath } from '../components/conversation/visualizationParser'
import { assistantMessageCopyText, parseVisualizationContentReferences } from '../components/conversation/assistantMessageSegments'

const CHART_PATH = '/workspace/.craft/visualizations/thread-test/alpha-chart.html'
const wrapped = (json: string): string => `${String.fromCharCode(0xe200)}visualize${String.fromCharCode(0xe202)}${json}${String.fromCharCode(0xe201)}`

describe('visualization content references', () => {
  it('parses both reference forms on their own lines outside fences', () => {
    const markdown = [
      'Before',
      `visualize${JSON.stringify({ path: CHART_PATH, title: 'Sales by region' })}`,
      '```',
      `visualize${JSON.stringify({ path: '/w/hidden.html' })}`,
      '```',
      `  ${wrapped(JSON.stringify({ path: 'C:\\work\\beta.html', mode: 'wide' }))}  \r`,
      'After'
    ].join('\n')

    expect(parseVisualizationContentReferences(markdown).map(({ path, title, mode }) => ({ path, title, mode }))).toEqual([
      { path: CHART_PATH, title: 'Sales by region', mode: undefined },
      { path: 'C:\\work\\beta.html', title: undefined, mode: 'wide' }
    ])
  })

  it.each([
    'text visualize{"path":"/w/chart.html"}',
    'visualize{"path":"/w/chart.html"} text',
    'visualize{"path":"/w/chart.html",}',
    "visualize{'path':'/w/chart.html'}",
    'visualize{"path":"/w/chart.html","extra":1}',
    'visualize{"path":"/w/chart.html","mode":"full"}',
    'visualize{"path":"/w/chart.html","title":" "}',
    'visualize{"path":"/w/chart.html","title":1}',
    `visualize{"path":"/w/chart.html","title":"${'a'.repeat(201)}"}`,
    'visualize["/w/chart.html"]',
    'visualize{}',
    'visualize{"path":"chart.html"}',
    'visualize{"path":"/w/../chart.html"}',
    'visualize{"path":"/w/Chart.html"}'
  ])('keeps %s as ordinary text', markdown => {
    expect(parseVisualizationContentReferences(markdown)).toEqual([])
    expect(assistantMessageCopyText(markdown)).toBe(markdown)
  })

  it.each([
    ['/w/chart.html', true],
    ['C:\\w\\sales-by-region.html', true],
    ['C:/w/chart.html', true],
    ['\\\\server\\share\\chart.html', true],
    ['chart.html', false],
    ['w/chart.html', false],
    ['/w/./chart.html', false],
    ['/w/chart.htm', false],
    ['/w/chart_1.html', false]
  ])('validates path %s', (path, expected) => {
    expect(isValidVisualizationPath(path)).toBe(expected)
  })
})
