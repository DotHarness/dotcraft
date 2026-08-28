// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest'
import { collectTextNodes, rangesIn } from '../find/decorate'

function mount(html: string): HTMLElement {
  const host = document.createElement('div')
  host.innerHTML = html
  document.body.append(host)
  return host
}

afterEach(() => { document.body.replaceChildren() })

describe('collectTextNodes', () => {
  it('skips gutter numbers so a search for a number does not match every line', () => {
    const host = mount(`
      <div><span data-line-num>12</span><span data-line="1">total = 12</span></div>
      <div><span data-line-num>13</span><span data-line="2">count = 1</span></div>
    `)

    const text = collectTextNodes(host).map((node) => node.data).join('|')
    expect(text).not.toContain('13')
    expect(text).toContain('total = 12')
  })

  it('skips diff markers and other chrome marked as unsearchable', () => {
    const host = mount('<div><span data-find-skip>+</span><span data-line="1">added</span></div>')

    expect(collectTextNodes(host).map((node) => node.data)).toEqual(['added'])
  })

  it('skips script, style, and editable content', () => {
    const host = mount([
      '<script>const secret = 1</script>',
      '<style>.a{color:red}</style>',
      '<div contenteditable="true">draft</div>',
      '<span data-line="1">visible</span>'
    ].join(''))

    expect(collectTextNodes(host).map((node) => node.data)).toEqual(['visible'])
  })
})

describe('rangesIn', () => {
  it('finds a match that syntax highlighting split across several runs', () => {
    const host = mount('<span data-line="1"><span>get</span><span>Total</span><span>()</span></span>')

    const ranges = rangesIn(host, 'gettotal')
    expect(ranges).toHaveLength(1)
    expect(ranges[0]?.toString()).toBe('getTotal')
  })

  it('finds every occurrence, in document order', () => {
    const host = mount([
      '<span data-line="1">alpha beta</span>',
      '<span data-line="2">beta gamma</span>'
    ].join(''))

    expect(rangesIn(host, 'beta').map((range) => range.toString())).toEqual(['beta', 'beta'])
  })

})
