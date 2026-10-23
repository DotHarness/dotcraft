import { describe, expect, it } from 'vitest'
import { GENERATIVE_UI_LIMITS } from '../components/conversation/generativeUi/generativeUiProtocol'
import { readGenerativeUiProps } from '../components/conversation/generativeUi/generativeUiSchema'
import { validateGenerativeUiTree } from '../components/conversation/generativeUi/generativeUiTree'

const node = (type: string, props: Record<string, unknown> = {}, children: unknown[] = []) => ({ type, props, children })

describe('generative UI snapshot validation', () => {
  it('accepts a well-formed tree with callback references', () => {
    const tree = [node('Card', { title: 'Totals' }, ['Hello', node('Button', { label: 'Add', onClick: { $callback: 'r.0:onClick' } })])]
    expect(validateGenerativeUiTree(tree)).toEqual({ ok: true, tree })
  })

  it('rejects malformed trees as invalid payload', () => {
    const invalid: Array<[string, unknown]> = [
      ['a tree that is not a list', { type: 'Card' }],
      ['a node with extra fields', [{ ...node('Card'), style: 'color:red' }]],
      ['a lowercase element name', [node('div')]],
      ['non-JSON props', [node('Text', { value: new Date(0) })]],
      ['non-finite numbers', [node('Slider', { value: Number.NaN, min: 0, max: 1 })]],
      ['a non-list children field', [{ type: 'Card', props: {}, children: 'text' }]]
    ]
    for (const [label, tree] of invalid) {
      expect(validateGenerativeUiTree(tree), label).toMatchObject({ ok: false, stage: 'invalid_payload' })
    }
  })

  it('rejects trees past the depth and size limits', () => {
    let deep: unknown = 'leaf'
    for (let level = 0; level <= GENERATIVE_UI_LIMITS.maxDepth; level++) deep = node('Box', {}, [deep])
    expect(validateGenerativeUiTree([deep])).toMatchObject({ ok: false, stage: 'payload_too_large' })

    const wide = Array.from({ length: GENERATIVE_UI_LIMITS.maxNodes + 1 }, () => node('Text'))
    expect(validateGenerativeUiTree(wide)).toMatchObject({ ok: false, stage: 'payload_too_large' })

    const large = [node('Markdown', { content: 'x'.repeat(GENERATIVE_UI_LIMITS.maxSnapshotBytes) })]
    expect(validateGenerativeUiTree(large)).toMatchObject({ ok: false, stage: 'payload_too_large' })
  })
})

describe('generative UI component props', () => {
  it('rejects props that break a component schema', () => {
    expect(readGenerativeUiProps('Slider', { min: 10, max: 5 })).toMatchObject({ ok: false })
    expect(readGenerativeUiProps('Image', { src: 'javascript:alert(1)', alt: 'x' })).toMatchObject({ ok: false })
    expect(readGenerativeUiProps('Image', { src: 'https://example.com/a.png', alt: 'x' })).toMatchObject({ ok: false })
    expect(readGenerativeUiProps('Image', { src: 'data:image/svg+xml;base64,PHN2Zz4=', alt: 'x' })).toMatchObject({ ok: false })
    expect(readGenerativeUiProps('Button', { onClick: 'alert(1)' })).toMatchObject({ ok: false })
    expect(readGenerativeUiProps('Chart', { type: 'radar', data: [{ x: 1 }], xKey: 'x', series: [{ key: 'x' }] })).toMatchObject({ ok: false })
  })

  it('accepts valid props, dropping unknown ones and treating null as unset', () => {
    expect(readGenerativeUiProps('Image', { src: 'data:image/png;base64,iVBORw0KGgo=', alt: 'Chart' })).toMatchObject({ ok: true })
    expect(readGenerativeUiProps('Text', { value: 'Hi', tone: 'secondary', style: { color: 'red' } })).toEqual({ ok: true, props: { value: 'Hi', tone: 'secondary' } })
    expect(readGenerativeUiProps('Input', { label: 'Usage', value: '30', error: null })).toEqual({ ok: true, props: { label: 'Usage', value: '30' } })
  })
})
