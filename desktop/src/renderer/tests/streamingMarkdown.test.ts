import { describe, it, expect } from 'vitest'
import { repairStreamingMarkdown } from '../components/conversation/streamingMarkdown'

describe('repairStreamingMarkdown', () => {
  it('closes an open code span and drops a lone opening backtick', () => {
    expect(repairStreamingMarkdown('使用 `config-lo')).toBe('使用 `config-lo`')
    expect(repairStreamingMarkdown('run ``a`b')).toBe('run ``a`b``')
    expect(repairStreamingMarkdown('使用 `')).toBe('使用 ')
    expect(repairStreamingMarkdown('done `a` and `b`')).toBe('done `a` and `b`')
    expect(repairStreamingMarkdown('escaped \\`tick')).toBe('escaped \\`tick')
  })

  it('leaves an open fenced block and its content alone', () => {
    const open = 'intro\n\n```ts\nconst a = `x\n'
    expect(repairStreamingMarkdown(open)).toBe(open)
    expect(repairStreamingMarkdown('```\n`a\n```\n\nnow `b')).toBe('```\n`a\n```\n\nnow `b`')
  })

  it('only repairs the last paragraph', () => {
    expect(repairStreamingMarkdown('stray ` tick\n\nnext `co')).toBe('stray ` tick\n\nnext `co`')
  })

  it('closes emphasis and drops a bare trailing marker', () => {
    expect(repairStreamingMarkdown('this is **bol')).toBe('this is **bol**')
    expect(repairStreamingMarkdown('this is *ita')).toBe('this is *ita*')
    expect(repairStreamingMarkdown('this is **')).toBe('this is ')
    expect(repairStreamingMarkdown('**bold `co')).toBe('**bold `co`**')
    expect(repairStreamingMarkdown('a * b')).toBe('a * b')
    expect(repairStreamingMarkdown('* item one')).toBe('* item one')
    expect(repairStreamingMarkdown('`a*b` and *c')).toBe('`a*b` and *c*')
  })

  it('holds back partial links and images until they complete', () => {
    expect(repairStreamingMarkdown('see [the do')).toBe('see ')
    expect(repairStreamingMarkdown('see [the docs](https://exa')).toBe('see ')
    expect(repairStreamingMarkdown('see [the docs](https://example.com) now')).toBe('see [the docs](https://example.com) now')
    expect(repairStreamingMarkdown('- [ ] task')).toBe('- [ ] task')
    expect(repairStreamingMarkdown('text\n![alt](http')).toBe('text\n')
    expect(repairStreamingMarkdown('`arr[0` is')).toBe('`arr[0` is')
  })
})
