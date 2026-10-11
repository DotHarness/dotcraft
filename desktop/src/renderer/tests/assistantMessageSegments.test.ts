import { describe, expect, it } from 'vitest'
import { assistantMessageCopyText, splitAssistantMessage } from '../components/conversation/assistantMessageSegments'

const CHART_PATH = '/workspace/.craft/visualizations/thread-test/alpha-chart.html'
const reference = `visualize${JSON.stringify({ path: CHART_PATH })}`
const program = 'DotCraft.render(<Text value="Hi" />)'

const kinds = (text: string, streaming = false) =>
  splitAssistantMessage(text, { streaming, visualizations: streaming ? 'strip' : 'render' }).map(segment =>
    segment.kind === 'generativeUi' ? { kind: segment.kind, index: segment.index, code: segment.code, closed: segment.closed } : { kind: segment.kind })

describe('assistant message segments', () => {
  it('runs a closed block while the message still streams and holds an unclosed one', () => {
    const text = ['Intro', '```dotcraft-ui', program, '```', 'Between', '```dotcraft-ui', 'function App() {'].join('\n')

    expect(kinds(text, true)).toEqual([
      { kind: 'markdown' },
      { kind: 'generativeUi', index: 0, code: program, closed: true },
      { kind: 'markdown' },
      { kind: 'generativeUi', index: 1, code: 'function App() {', closed: false }
    ])
    expect(kinds(text, false).at(-1)).toMatchObject({ kind: 'generativeUi', closed: true })
  })

  it('never shows a partial opening fence or reference while streaming', () => {
    const segments = splitAssistantMessage('Intro\n```dotcr', { streaming: true, visualizations: 'strip' })
    expect(segments).toEqual([{ kind: 'markdown', start: 0, text: 'Intro\n' }])
    expect(splitAssistantMessage('Intro\nvisualize{"pa', { streaming: true, visualizations: 'strip' }))
      .toEqual([{ kind: 'markdown', start: 0, text: 'Intro\n' }])
  })

  it('ignores blocks nested inside other fences or indented as code', () => {
    const text = [
      '````markdown',
      '```dotcraft-ui',
      program,
      '```',
      '````',
      '~~~',
      '```dotcraft-ui',
      '~~~',
      '    ```dotcraft-ui',
      `    ${program}`,
      '    ```'
    ].join('\n')

    expect(kinds(text)).toEqual([{ kind: 'markdown' }])
  })

  it('keeps blocks and visualization references in source order', () => {
    const text = ['Look:', reference, '```dotcraft-ui', program, '```', 'Done.'].join('\n')

    expect(kinds(text)).toEqual([
      { kind: 'markdown' },
      { kind: 'visualization' },
      { kind: 'generativeUi', index: 0, code: program, closed: true },
      { kind: 'markdown' }
    ])
    expect(kinds(text, true).map(segment => segment.kind)).toEqual(['markdown', 'generativeUi', 'markdown'])
  })

  it('copies the prose without program source or references', () => {
    const text = ['Your payment is $1,432 a month.', '', '```dotcraft-ui', program, '```', '', reference, 'Rates assume 30 years.'].join('\n')

    expect(assistantMessageCopyText(text)).toBe('Your payment is $1,432 a month.\n\nRates assume 30 years.')
    expect(assistantMessageCopyText('Plain **answer**\n')).toBe('Plain **answer**\n')
  })
})
