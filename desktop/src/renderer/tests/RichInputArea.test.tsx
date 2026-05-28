import { createRef } from 'react'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { RichInputArea, type RichInputAreaHandle } from '../components/conversation/RichInputArea'
import {
  COMMAND_REF_CLASS,
  FILE_REF_CLASS,
  SKILL_REF_CLASS
} from '../components/conversation/richInputConstants'

function linearize(root: Node): string {
  let out = ''
  const walk = (node: Node): void => {
    if (node.nodeType === Node.TEXT_NODE) {
      out += node.textContent ?? ''
      return
    }
    if (node.nodeType !== Node.ELEMENT_NODE) return
    const el = node as HTMLElement
    if (
      el.classList.contains(FILE_REF_CLASS) ||
      el.classList.contains(COMMAND_REF_CLASS) ||
      el.classList.contains(SKILL_REF_CLASS) ||
      el.tagName === 'BR'
    ) {
      out += ' '
      return
    }
    for (const child of Array.from(node.childNodes)) {
      walk(child)
    }
  }
  walk(root)
  return out
}

function getSelectionRange(root: HTMLElement): { start: number; end: number } | null {
  const selection = window.getSelection()
  if (!selection || selection.rangeCount === 0) return null
  const range = selection.getRangeAt(0)
  if (!root.contains(range.startContainer) || !root.contains(range.endContainer)) return null

  const beforeStart = document.createRange()
  beforeStart.selectNodeContents(root)
  beforeStart.setEnd(range.startContainer, range.startOffset)
  const beforeEnd = document.createRange()
  beforeEnd.selectNodeContents(root)
  beforeEnd.setEnd(range.endContainer, range.endOffset)

  const startContainer = document.createElement('div')
  startContainer.appendChild(beforeStart.cloneContents())
  const endContainer = document.createElement('div')
  endContainer.appendChild(beforeEnd.cloneContents())
  return {
    start: linearize(startContainer).length,
    end: linearize(endContainer).length
  }
}

describe('RichInputArea selection helpers', () => {
  it('places the caret at the end after setPlainText and setSelectionRange', () => {
    const ref = createRef<RichInputAreaHandle>()

    render(
      <RichInputArea
        ref={ref}
        onSubmit={vi.fn()}
      />
    )

    act(() => {
      ref.current?.setPlainText('hello world')
      ref.current?.setSelectionRange({ start: 11, end: 11 })
    })

    const textbox = screen.getByRole('textbox')
    expect(ref.current?.getSelectionRange()).toEqual({ start: 11, end: 11 })
    expect(getSelectionRange(textbox)).toEqual({ start: 11, end: 11 })
  })

  it('round-trips linear selection offsets for content with tags', () => {
    const ref = createRef<RichInputAreaHandle>()

    render(
      <RichInputArea
        ref={ref}
        onSubmit={vi.fn()}
      />
    )

    act(() => {
      ref.current?.setContent({
        segments: [
          { type: 'text', value: 'A' },
          { type: 'file', relativePath: 'src/foo.ts' },
          { type: 'text', value: 'B' },
          { type: 'command', command: '/review' },
          { type: 'text', value: 'C' },
          { type: 'skill', skillName: 'memory' }
        ]
      })
      ref.current?.setSelectionRange({ start: 4, end: 4 })
    })

    const textbox = screen.getByRole('textbox')
    expect(ref.current?.getSelectionRange()).toEqual({ start: 4, end: 4 })
    expect(getSelectionRange(textbox)).toEqual({ start: 4, end: 4 })
  })

  it('clamps oversized selection ranges to the end of the content', () => {
    const ref = createRef<RichInputAreaHandle>()

    render(
      <RichInputArea
        ref={ref}
        onSubmit={vi.fn()}
      />
    )

    act(() => {
      ref.current?.setPlainText('abc')
      ref.current?.setSelectionRange({ start: 99, end: 120 })
    })

    expect(ref.current?.getSelectionRange()).toEqual({ start: 3, end: 3 })
  })

  it('emits onSelectionChange when the selection is updated programmatically', () => {
    const ref = createRef<RichInputAreaHandle>()
    const onSelectionChange = vi.fn()

    render(
      <RichInputArea
        ref={ref}
        onSubmit={vi.fn()}
        onSelectionChange={onSelectionChange}
      />
    )

    act(() => {
      ref.current?.setPlainText('hello world')
      ref.current?.setSelectionRange({ start: 5, end: 5 })
    })

    expect(onSelectionChange).toHaveBeenCalledWith({ start: 11, end: 11 })
    expect(onSelectionChange).toHaveBeenCalledWith({ start: 5, end: 5 })
  })

  it('disables browser spellcheck and autocorrect helpers on the editor', () => {
    render(
      <RichInputArea
        onSubmit={vi.fn()}
      />
    )

    const textbox = screen.getByRole('textbox')
    expect(textbox).toHaveAttribute('spellcheck', 'false')
    expect(textbox).toHaveAttribute('autocorrect', 'off')
    expect(textbox).toHaveAttribute('autocapitalize', 'off')
  })
})

describe('RichInputArea keyboard submit behavior', () => {
  it('submits on plain Enter but not Shift+Enter', () => {
    const onSubmit = vi.fn()

    render(<RichInputArea onSubmit={onSubmit} />)

    const textbox = screen.getByRole('textbox')
    expect(fireEvent.keyDown(textbox, { key: 'Enter', shiftKey: true })).toBe(true)
    expect(onSubmit).not.toHaveBeenCalled()

    expect(fireEvent.keyDown(textbox, { key: 'Enter' })).toBe(false)
    expect(onSubmit).toHaveBeenCalledTimes(1)
  })

  it('lets Enter confirm active IME composition without submitting or preventing default', () => {
    const onSubmit = vi.fn()

    render(<RichInputArea onSubmit={onSubmit} />)

    const textbox = screen.getByRole('textbox')
    fireEvent.compositionStart(textbox)

    expect(fireEvent.keyDown(textbox, { key: 'Enter' })).toBe(true)
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('treats native composing Enter and keyCode 229 as IME confirmation', () => {
    const onSubmit = vi.fn()

    render(<RichInputArea onSubmit={onSubmit} />)

    const textbox = screen.getByRole('textbox')
    expect(fireEvent.keyDown(textbox, { key: 'Enter', isComposing: true })).toBe(true)
    expect(fireEvent.keyDown(textbox, { key: 'Enter', keyCode: 229 })).toBe(true)
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('ignores the Enter immediately after composition ends, then allows the next Enter to submit', () => {
    const onSubmit = vi.fn()

    render(<RichInputArea onSubmit={onSubmit} />)

    const textbox = screen.getByRole('textbox')
    fireEvent.compositionStart(textbox)
    fireEvent.compositionEnd(textbox)

    expect(fireEvent.keyDown(textbox, { key: 'Enter' })).toBe(true)
    expect(onSubmit).not.toHaveBeenCalled()

    expect(fireEvent.keyDown(textbox, { key: 'Enter' })).toBe(false)
    expect(onSubmit).toHaveBeenCalledTimes(1)
  })
})

describe('RichInputArea catalog-aware paste parsing', () => {
  it('keeps unmatched slash and skill tokens as plain text when pasted', () => {
    const ref = createRef<RichInputAreaHandle>()

    render(
      <RichInputArea
        ref={ref}
        onSubmit={vi.fn()}
        refCatalog={{
          commands: [{ name: '/code-review', aliases: ['/cr'] }],
          skills: [{ name: 'memory', available: true }]
        }}
      />
    )

    act(() => {
      ref.current?.setPlainText('')
      ref.current?.setSelectionRange({ start: 0, end: 0 })
    })

    const textbox = screen.getByRole('textbox')
    fireEvent.paste(textbox, {
      clipboardData: {
        items: [],
        getData: (type: string) => type === 'text/plain' ? '$unknown /unknown' : ''
      }
    })

    expect(textbox.querySelector(`.${SKILL_REF_CLASS}`)).toBeNull()
    expect(textbox.querySelector(`.${COMMAND_REF_CLASS}`)).toBeNull()
    expect(ref.current?.getText()).toBe('$unknown /unknown')
  })

  it('turns catalog-matched pasted tokens into command and skill tags', () => {
    const ref = createRef<RichInputAreaHandle>()

    render(
      <RichInputArea
        ref={ref}
        onSubmit={vi.fn()}
        refCatalog={{
          commands: [{ name: '/code-review', aliases: ['/cr'] }],
          skills: [{ name: 'memory', available: true }]
        }}
      />
    )

    act(() => {
      ref.current?.setPlainText('')
      ref.current?.setSelectionRange({ start: 0, end: 0 })
    })

    const textbox = screen.getByRole('textbox')
    fireEvent.paste(textbox, {
      clipboardData: {
        items: [],
        getData: (type: string) => type === 'text/plain' ? '/code-review $memory' : ''
      }
    })

    expect(textbox.querySelector(`.${COMMAND_REF_CLASS}`)).not.toBeNull()
    expect(textbox.querySelector(`.${SKILL_REF_CLASS}`)).not.toBeNull()
    expect(ref.current?.getSegments()).toEqual([
      { type: 'command', command: '/code-review' },
      { type: 'text', value: '\u00a0 ' },
      { type: 'skill', skillName: 'memory' },
      { type: 'text', value: '\u00a0' }
    ])
  })
})
