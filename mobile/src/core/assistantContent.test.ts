import { describe, expect, it } from 'vitest'
import { copyableAssistantText, splitAssistantText } from './assistantContent'

const WRAPPED = '\uE200visualize\uE202{"path":"/w/b.html"}\uE201'

describe('assistant content', () => {
  it('turns top-level Generative UI blocks into parts and drops visualization references', () => {
    const text = ['Intro', '', '```dotcraft-ui', 'DotCraft.render(<App />)', '```', '', 'visualize{"path":"/w/a.html"}', WRAPPED, 'Outro'].join('\n')
    expect(splitAssistantText(text, false)).toEqual([
      { kind: 'markdown', text: 'Intro\n' },
      { kind: 'interactive', source: 'DotCraft.render(<App />)' },
      { kind: 'markdown', text: '\nOutro' },
    ])
  })

  it('keeps fenced content and hides a reference still streaming', () => {
    const fenced = ['````md', '```dotcraft-ui', 'visualize{"path":"/w/a.html"}', '```', '````'].join('\n')
    expect(splitAssistantText(fenced, false)).toEqual([{ kind: 'markdown', text: fenced }])
    expect(splitAssistantText('Done\nvisualize{"pa', true)).toEqual([{ kind: 'markdown', text: 'Done' }])
  })

  it('copies only the prose', () => {
    expect(copyableAssistantText('Intro\n\n```dotcraft-ui\nx\n```\n\nOutro')).toBe('Intro\n\nOutro')
  })
})
