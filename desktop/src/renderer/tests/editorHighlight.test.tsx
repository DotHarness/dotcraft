import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { EditorState } from '@codemirror/state'
import { EditorView } from '@codemirror/view'
import { markdown } from '@codemirror/lang-markdown'
import { editorHighlight, editorTokenField } from '../components/detail/viewers/editorHighlight'
import type { FileHighlightRequest, FileHighlightResult, HighlighterPool } from '../highlight'

describe('editor Shiki adapter', () => {
  let view: EditorView
  const requests: {
    request: FileHighlightRequest
    complete: (result: FileHighlightResult) => void
  }[] = []
  const release = vi.fn()
  const requestFile = vi.fn((_subscriber, request, complete) => {
    requests.push({ request, complete })
    return undefined
  })
  const pool = { requestFile, release } as unknown as HighlighterPool
  const tokens = (text: string): FileHighlightResult => ({
    highlighted: true,
    lines: text
      .split('\n')
      .map((line) => [
        { text: line, style: { '--dc-token-light': '#000', '--dc-token-dark': '#fff' } }
      ])
  })
  const ranges = () => {
    const result: number[][] = []
    view.state.field(editorTokenField).between(0, view.state.doc.length, (from, to) => {
      result.push([from, to])
    })
    return result
  }
  const mount = (doc: string, preview = false) => {
    view = new EditorView({
      parent: document.body,
      state: EditorState.create({
        doc,
        extensions: [...(preview ? [markdown()] : []), editorHighlight(pool, 'file.cs', preview)]
      })
    })
  }
  beforeEach(() => {
    vi.useFakeTimers()
    requests.length = 0
    vi.clearAllMocks()
    Range.prototype.getClientRects = () => [] as unknown as DOMRectList
    Range.prototype.getBoundingClientRect = () => new DOMRect()
  })
  afterEach(() => {
    view?.destroy()
    vi.useRealTimers()
  })

  it('maps existing tokens while coalescing input and drops stale document results', () => {
    mount('first\nsecond')
    vi.advanceTimersByTime(20)
    requests[0]!.complete(tokens('first\nsecond'))
    expect(ranges()).toEqual([
      [0, 5],
      [6, 12]
    ])
    view.dispatch({ changes: { from: 0, insert: 'a' }, selection: { anchor: 1 } })
    expect(ranges()).toEqual([
      [1, 6],
      [7, 13]
    ])
    vi.advanceTimersByTime(20)
    view.dispatch({ changes: { from: 1, insert: 'b' }, selection: { anchor: 2 } })
    view.dispatch({ changes: { from: 2, insert: 'c' }, selection: { anchor: 3 } })
    requests[1]!.complete(tokens('afirst\nsecond'))
    expect(ranges()).toEqual([
      [3, 8],
      [9, 15]
    ])
    vi.advanceTimersByTime(20)
    expect(requests).toHaveLength(3)
    requests[2]!.complete(tokens('abcfirst\nsecond'))
    expect(ranges()).toEqual([
      [0, 8],
      [9, 15]
    ])
    expect(view.state.selection.main.anchor).toBe(3)
    expect(release).toHaveBeenCalledTimes(2)
  })

  it('defers token application during composition and keeps Chinese input and selection intact', () => {
    mount('中文')
    vi.advanceTimersByTime(20)
    const composing = vi.spyOn(view, 'composing', 'get').mockReturnValue(true)
    view.dispatch({ selection: { anchor: 2 } })
    requests[0]!.complete(tokens('中文'))
    expect(ranges()).toEqual([])
    vi.advanceTimersByTime(20)
    expect(ranges()).toEqual([])
    composing.mockReturnValue(false)
    vi.advanceTimersByTime(20)
    expect(ranges()).toEqual([[0, 2]])
    expect(view.state.doc.toString()).toBe('中文')
    expect(view.state.selection.main.anchor).toBe(2)
  })

  it('uses shared language results at fenced-code offsets without colouring document prose', () => {
    const doc = '# Heading\n\n```ts\nconst a = 1\n```\n\n```cs\nvar b = 2;\n```'
    mount(doc, true)
    vi.advanceTimersByTime(20)
    expect(requests.map(({ request }) => request.lang)).toEqual(['ts', 'cs'])
    for (const { request, complete } of requests) complete(tokens(request.contents))
    expect(ranges()).toEqual([
      [doc.indexOf('const'), doc.indexOf('const') + 'const a = 1'.length],
      [doc.indexOf('var'), doc.indexOf('var') + 'var b = 2;'.length]
    ])
  })
})
