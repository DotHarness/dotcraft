import { describe, expect, it } from 'vitest'
import { EditorState } from '@codemirror/state'
import { EditorView } from '@codemirror/view'
import { markdown } from '@codemirror/lang-markdown'
import { GFM } from '@lezer/markdown'
import { markdownEditing } from '../components/detail/viewers/markdownEditing'

function preview(doc: string, selection?: number): EditorView {
  const parent = document.createElement('div')
  document.body.appendChild(parent)
  return new EditorView({
    parent,
    state: EditorState.create({
      doc,
      selection: selection == null ? undefined : { anchor: selection },
      extensions: [markdown({ extensions: GFM }), markdownEditing('light', 'D:/notes/issues.md', () => {})]
    })
  })
}

describe('Markdown preview images', () => {
  it('shows a local image in place and its source where the selection is', () => {
    const doc = 'Intro\n\n![shot](images/a.png)\n'
    const view = preview(doc)
    const image = view.dom.querySelector<HTMLImageElement>('img.dc-md-image')
    expect(image?.getAttribute('src')).toBe('dotcraft-viewer://workspace/D%3A/notes/images/a.png')
    expect(image?.alt).toBe('shot')

    view.focus()
    view.dispatch({ selection: { anchor: doc.indexOf('images') } })
    view.contentDOM.dispatchEvent(new FocusEvent('focus'))
    expect(view.dom.querySelector('img.dc-md-image')).toBeNull()
    view.destroy()
  })

  it('leaves a remote image as Markdown source', () => {
    const view = preview('![x](https://example.com/x.png)')
    expect(view.dom.querySelector('img')).toBeNull()
    view.destroy()
  })
})
