import { describe, expect, it } from 'vitest'
import { assistantMessageCopyText, splitAssistantMessage } from '../components/conversation/assistantMessageSegments'

const CHART_PATH = '/workspace/.craft/visualizations/thread-test/alpha-chart.html'
const reference = `visualize${JSON.stringify({ path: CHART_PATH })}`

const kinds = (text: string, streaming = false) =>
  splitAssistantMessage(text, { streaming, visualizations: streaming ? 'strip' : 'render' }).map(segment => segment.kind)

describe('assistant message segments', () => {
  it('never shows a partial reference while streaming', () => {
    expect(splitAssistantMessage('Intro\nvisualize{"pa', { streaming: true, visualizations: 'strip' }))
      .toEqual([{ kind: 'markdown', start: 0, text: 'Intro\n' }])
  })

  it('ignores references inside fences', () => {
    const text = ['```markdown', reference, '```', '~~~', reference, '~~~'].join('\n')

    expect(kinds(text)).toEqual(['markdown'])
  })

  it('keeps visualization references in source order', () => {
    const text = ['Look:', reference, 'Done.'].join('\n')

    expect(kinds(text)).toEqual(['markdown', 'visualization', 'markdown'])
    expect(kinds(text, true)).toEqual(['markdown'])
  })

  it('copies the prose without references', () => {
    const text = ['Your payment is $1,432 a month.', '', reference, 'Rates assume 30 years.'].join('\n')

    expect(assistantMessageCopyText(text)).toBe('Your payment is $1,432 a month.\n\nRates assume 30 years.')
    expect(assistantMessageCopyText('Plain **answer**\n')).toBe('Plain **answer**\n')
  })
})
