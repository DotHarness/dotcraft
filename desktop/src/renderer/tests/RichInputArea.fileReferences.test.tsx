import { createRef } from 'react'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { RichInputArea, type RichInputAreaHandle } from '../components/conversation/RichInputArea'
import { buildComposerInputParts } from '../utils/composeInputParts'

const path = 'src/file with spaces.ts'

function loseSelection(): void {
  screen.getByRole('button', { name: 'Outside' }).focus()
  window.getSelection()?.removeAllRanges()
}

function fixture(disabled = false) {
  const ref = createRef<RichInputAreaHandle>()
  const onContentChange = vi.fn()
  render(
    <>
      <RichInputArea ref={ref} disabled={disabled} onSubmit={vi.fn()} onContentChange={onContentChange} />
      <button>Outside</button>
    </>
  )
  return { ref, onContentChange }
}

describe('file references at the saved composer selection', () => {
  it('produces the same structured draft and submitted input as manual @ selection', () => {
    const { ref } = fixture()
    act(() => {
      ref.current!.setPlainText('Check @file')
      ref.current!.setSelectionRange({ start: 11, end: 11 })
      ref.current!.insertFileTag(path)
    })
    const manual = ref.current!.getSegments()
    const manualParts = buildComposerInputParts({ text: ref.current!.getText(), segments: manual })

    act(() => {
      ref.current!.setPlainText('Check ')
      ref.current!.setSelectionRange({ start: 6, end: 6 })
      loseSelection()
      ref.current!.insertFileTagAtSelection(path)
    })

    expect(ref.current!.getSegments()).toEqual(manual)
    expect(buildComposerInputParts({
      text: ref.current!.getText(),
      segments: ref.current!.getSegments()
    })).toEqual(manualParts)
    expect(manualParts.inputParts).toContainEqual({ type: 'fileRef', path, displayPath: path })
  })

  it('captures a moved caret on blur and inserts in the middle instead of appending', () => {
    const { ref, onContentChange } = fixture()
    act(() => {
      ref.current!.setPlainText('beforeafter')
      const editor = screen.getByRole('textbox')
      const range = document.createRange()
      range.setStart(editor.firstChild!, 6)
      range.collapse(true)
      window.getSelection()?.removeAllRanges()
      window.getSelection()?.addRange(range)
      fireEvent.blur(editor)
      loseSelection()
      ref.current!.insertFileTagAtSelection(path)
    })

    expect(ref.current!.getSegments()).toEqual([
      { type: 'text', value: 'before ' },
      { type: 'file', relativePath: path },
      { type: 'text', value: '\u00a0after' }
    ])
    expect(document.activeElement).toBe(screen.getByRole('textbox'))
    const selection = window.getSelection()!
    expect(selection.anchorNode?.nodeType).toBe(Node.TEXT_NODE)
    expect(selection.anchorNode?.textContent).toBe('\u00a0')
    expect(selection.anchorOffset).toBe(1)
    expect(onContentChange).toHaveBeenCalled()
  })

  it.each(['', 'Check ', 'Check\n'])('does not add a leading space after %j', (text) => {
    const { ref } = fixture()
    act(() => {
      ref.current!.setPlainText(text)
      loseSelection()
      ref.current!.insertFileTagAtSelection(path)
    })
    expect(ref.current!.getText()).toBe(text + '@' + path + '\u00a0')
  })

  it('replaces the saved selected text and leaves the surrounding text intact', () => {
    const { ref } = fixture()
    act(() => {
      ref.current!.setPlainText('See old end')
      ref.current!.setSelectionRange({ start: 4, end: 7 })
      loseSelection()
      ref.current!.insertFileTagAtSelection(path)
    })
    expect(ref.current!.getSegments()).toEqual([
      { type: 'text', value: 'See ' },
      { type: 'file', relativePath: path },
      { type: 'text', value: '\u00a0 end' }
    ])
  })

  it('uses the newly inserted caret for another reference after losing focus again', () => {
    const { ref } = fixture()
    act(() => {
      ref.current!.setPlainText('See end')
      ref.current!.setSelectionRange({ start: 4, end: 4 })
      loseSelection()
      ref.current!.insertFileTagAtSelection(path)
      loseSelection()
      ref.current!.insertFileTagAtSelection('src/second.ts')
    })
    expect(ref.current!.getSegments()).toEqual([
      { type: 'text', value: 'See ' },
      { type: 'file', relativePath: path },
      { type: 'text', value: '\u00a0' },
      { type: 'file', relativePath: 'src/second.ts' },
      { type: 'text', value: '\u00a0end' }
    ])
  })

  it('inserts beside an existing chip without replacing or entering it', () => {
    const { ref } = fixture()
    act(() => {
      ref.current!.setContent({ segments: [{ type: 'file', relativePath: 'first.ts' }] })
      ref.current!.setSelectionRange({ start: 1, end: 1 })
      loseSelection()
      ref.current!.insertFileTagAtSelection(path)
    })
    expect(ref.current!.getSegments()).toEqual([
      { type: 'file', relativePath: 'first.ts' },
      { type: 'file', relativePath: path },
      { type: 'text', value: '\u00a0' }
    ])
  })

  it('appends when the editor has content but no valid selection has been saved', () => {
    const { ref } = fixture()
    const editor = screen.getByRole('textbox')
    editor.textContent = 'Check'
    fireEvent.input(editor)
    act(() => {
      loseSelection()
      ref.current!.insertFileTagAtSelection(path)
    })
    expect(ref.current!.getText()).toBe('Check @' + path + '\u00a0')
  })

  it('does not change a disabled editor', () => {
    const { ref } = fixture(true)
    act(() => {
      ref.current!.setPlainText('Keep')
      ref.current!.insertFileTagAtSelection(path)
    })
    expect(ref.current!.getText()).toBe('Keep')
  })
})
