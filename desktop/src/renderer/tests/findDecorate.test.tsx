// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { collectTextNodes, rangesIn, revealRange } from '../find/decorate'

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

describe('revealRange', () => {
  it('centers distinct occurrences from the same segment in its scroll view', () => {
    const host = mount('<div style="overflow-y: auto"><p>target before target after</p></div>')
    const scroller = host.firstElementChild as HTMLElement
    const ranges = rangesIn(scroller, 'target')
    setScrollGeometry(scroller, { clientHeight: 100, scrollHeight: 500, scrollTop: 0 })
    vi.spyOn(scroller, 'getBoundingClientRect').mockReturnValue(rect(0, 100))
    setRangeRect(ranges[0]!, rect(20, 10))
    setRangeRect(ranges[1]!, rect(260, 10))

    revealRange(ranges[0]!)
    const firstScrollTop = scroller.scrollTop
    scroller.scrollTop = 0
    revealRange(ranges[1]!)

    expect(firstScrollTop).toBe(0)
    expect(scroller.scrollTop).toBe(215)
  })

  it('clamps centered matches to the scroll view bounds', () => {
    const host = mount('<div style="overflow-y: auto"><span>target</span></div>')
    const scroller = host.firstElementChild as HTMLElement
    const [range] = rangesIn(scroller, 'target')
    setScrollGeometry(scroller, { clientHeight: 100, scrollHeight: 500, scrollTop: 200 })
    vi.spyOn(scroller, 'getBoundingClientRect').mockReturnValue(rect(0, 100))
    const targetRect = setRangeRect(range!, rect(-500, 10))

    revealRange(range!)
    expect(scroller.scrollTop).toBe(0)

    targetRect.mockReturnValue(rect(900, 10))
    revealRange(range!)
    expect(scroller.scrollTop).toBe(400)
  })

  it('falls back to element scrolling without a vertical scroll view', () => {
    const host = mount('<span>target</span>')
    const anchor = host.firstElementChild as HTMLElement
    const [range] = rangesIn(host, 'target')
    const scrollIntoView = vi.fn()
    Object.defineProperty(anchor, 'scrollIntoView', { configurable: true, value: scrollIntoView })

    revealRange(range!)

    expect(scrollIntoView).toHaveBeenCalledWith({ block: 'center' })
  })
})

function setScrollGeometry(
  element: HTMLElement,
  geometry: { clientHeight: number; scrollHeight: number; scrollTop: number }
): void {
  Object.defineProperties(element, {
    clientHeight: { configurable: true, value: geometry.clientHeight },
    scrollHeight: { configurable: true, value: geometry.scrollHeight },
    scrollTop: { configurable: true, value: geometry.scrollTop, writable: true }
  })
}

function setRangeRect(range: Range, value: DOMRect): ReturnType<typeof vi.fn> {
  const getBoundingClientRect = vi.fn(() => value)
  Object.defineProperty(range, 'getBoundingClientRect', {
    configurable: true,
    value: getBoundingClientRect
  })
  return getBoundingClientRect
}

function rect(top: number, height: number): DOMRect {
  return {
    x: 0,
    y: top,
    top,
    right: 10,
    bottom: top + height,
    left: 0,
    width: 10,
    height,
    toJSON: () => ({})
  }
}
