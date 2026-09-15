import { createRef } from 'react'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { RichInputArea, type RichInputAreaHandle } from '../components/conversation/RichInputArea'
import { RICH_REFS_CLIPBOARD_MIME } from '../components/conversation/richInputConstants'

describe('RichInputArea paste selection', () => {
  it.each(['', 'existing '])('keeps the caret in text after consecutive pastes at the end of %j', (initial) => {
    const ref = createRef<RichInputAreaHandle>()
    render(<RichInputArea ref={ref} onSubmit={vi.fn()} />)
    act(() => {
      ref.current!.setPlainText(initial)
      ref.current!.setSelectionRange({ start: initial.length, end: initial.length })
    })
    for (const text of ['first', 'second']) {
      fireEvent.paste(screen.getByRole('textbox'), {
        clipboardData: { items: [], getData: (type: string) => type === 'text/plain' ? text : '' }
      })
      const selection = window.getSelection()!
      expect(selection.anchorNode?.nodeType).toBe(Node.TEXT_NODE)
      expect(selection.anchorOffset).toBe(text.length)
      expect(selection.isCollapsed).toBe(true)
    }
    expect(ref.current!.getText()).toBe(`${initial}firstsecond`)
    expect(ref.current!.getSelectionRange()).toEqual({ start: initial.length + 11, end: initial.length + 11 })
  })

  it.each([
    { name: 'plain text', text: 'hello', rich: '' },
    { name: 'multiline text', text: 'hello\nworld\n', rich: '' },
    { name: 'catalog reference', text: '$memory', rich: '' },
    {
      name: 'rich clipboard reference', text: '',
      rich: JSON.stringify({ version: 1, segments: [{ type: 'file', relativePath: 'src/main.ts' }] })
    }
  ])('leaves an editable text caret after pasting $name over a selection', ({ text, rich }) => {
    const ref = createRef<RichInputAreaHandle>()
    render(<RichInputArea ref={ref} onSubmit={vi.fn()} refCatalog={{ skills: [{ name: 'memory', available: true }] }} />)
    act(() => {
      ref.current!.setPlainText('before REPLACE after')
      ref.current!.setSelectionRange({ start: 7, end: 14 })
    })
    const textbox = screen.getByRole('textbox')
    expect(fireEvent.paste(textbox, {
      clipboardData: {
        items: [],
        getData: (type: string) => type === RICH_REFS_CLIPBOARD_MIME ? rich : type === 'text/plain' ? text : ''
      }
    })).toBe(false)

    const selection = window.getSelection()!
    expect(selection.isCollapsed).toBe(true)
    // Chromium IME commits need a text-node caret instead of an element boundary.
    expect(selection.anchorNode?.nodeType).toBe(Node.TEXT_NODE)
    const anchor = selection.anchorNode as Text
    expect(selection.anchorOffset).toBe(anchor.length)
    expect(ref.current!.getText()).not.toContain('REPLACE')
    const pasted = ref.current!.getText()
    expect(pasted.startsWith('before ')).toBe(true)
    expect(pasted.endsWith(' after')).toBe(true)

    anchor.insertData(selection.anchorOffset, '，')
    fireEvent.input(textbox, { data: '，', inputType: 'insertText' })
    expect(ref.current!.getText()).toBe(`${pasted.slice(0, -6)}， after`)
  })
})
