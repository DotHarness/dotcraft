import { describe, expect, it } from 'vitest'
import { EditorState } from '@codemirror/state'
import { fileNavigationRange } from '../components/detail/viewers/fileNavigation'

const state = EditorState.create({ doc: 'first\nsecond\nthird\n' })

describe('file navigation positions', () => {
  it('keeps inclusive ranges separate from an empty caret position', () => {
    expect(fileNavigationRange(state, { line: 2, endLine: 3, column: 4 })).toEqual({
      line: 2, endLine: 3, anchor: state.doc.line(2).from + 3
    })
  })
  it('bounds lines, ranges and columns to the current document', () => {
    expect(fileNavigationRange(state, { line: 2, endLine: 999, column: 999 })).toEqual({
      line: 2, endLine: 4, anchor: state.doc.line(2).to
    })
    expect(fileNavigationRange(state, { line: 999, endLine: 1000 })).toEqual({
      line: 4, endLine: 4, anchor: state.doc.length
    })
  })
  it('ignores invalid start lines and falls back for invalid optional bounds', () => {
    for (const line of [0, -1, 1.5, NaN, Infinity, Number.MAX_SAFE_INTEGER + 1]) {
      expect(fileNavigationRange(state, { line }), String(line)).toBeUndefined()
    }
    expect(fileNavigationRange(state, undefined)).toBeUndefined()
    expect(fileNavigationRange(state, { line: 3, endLine: 2, column: -1 })).toEqual({
      line: 3, endLine: 3, anchor: state.doc.line(3).from
    })
  })
})
