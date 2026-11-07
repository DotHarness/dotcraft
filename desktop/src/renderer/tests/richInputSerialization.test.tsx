import { describe, expect, it } from 'vitest'
import { FILE_REF_CLASS, SKILL_REF_CLASS } from '../components/conversation/richInputConstants'
import {
  buildEditorFragmentFromSegments,
  collectComposerDraftSegments,
  parseComposerTextWithCatalog,
  serializeEditor,
  truncateEditorDomToSerializedLength
} from '../components/conversation/richInputSerialization'
import type { ComposerDraftSegment } from '../types/composerDraft'

function makeFileRef(relativePath: string): HTMLSpanElement {
  const s = document.createElement('span')
  s.className = FILE_REF_CLASS
  s.setAttribute('data-relative-path', relativePath)
  s.textContent = '📄 x'
  return s
}

function makeSkillRef(skillName: string): HTMLSpanElement {
  const s = document.createElement('span')
  s.className = SKILL_REF_CLASS
  s.setAttribute('data-skill', skillName)
  s.textContent = skillName
  return s
}

describe('truncateEditorDomToSerializedLength', () => {
  it('keeps file-ref spans that fit entirely before the cut', () => {
    const root = document.createElement('div')
    root.appendChild(document.createTextNode('aa'))
    root.appendChild(makeFileRef('b'))
    root.appendChild(document.createTextNode('ccccc'))
    // serialized: "aa" + "@b" + "ccccc" = 9 chars
    truncateEditorDomToSerializedLength(root, 5)
    expect(serializeEditor(root).length).toBe(5)
    expect(serializeEditor(root)).toBe('aa@bc')
    expect(root.querySelectorAll(`.${FILE_REF_CLASS}`).length).toBe(1)
    expect((root.querySelector(`.${FILE_REF_CLASS}`) as HTMLElement).getAttribute('data-relative-path')).toBe('b')
  })

  it('drops a file ref that does not fit entirely instead of flattening to text', () => {
    const root = document.createElement('div')
    root.appendChild(document.createTextNode('hello'))
    root.appendChild(makeFileRef('toolong'))
    root.appendChild(document.createTextNode('tail'))
    // "hello" = 5, "@toolong" = 9, total before tail = 14
    truncateEditorDomToSerializedLength(root, 7)
    expect(serializeEditor(root)).toBe('hello')
    expect(root.querySelectorAll(`.${FILE_REF_CLASS}`).length).toBe(0)
  })

  it('does not mutate when already under the limit', () => {
    const root = document.createElement('div')
    const span = makeFileRef('x')
    root.appendChild(span)
    truncateEditorDomToSerializedLength(root, 100)
    expect(root.querySelectorAll(`.${FILE_REF_CLASS}`).length).toBe(1)
    expect(serializeEditor(root)).toBe('@x')
  })

  it('collects mixed text and ref segments from editor DOM', () => {
    const root = document.createElement('div')
    root.appendChild(document.createTextNode('Check '))
    root.appendChild(makeFileRef('src/foo.ts'))
    root.appendChild(document.createTextNode(' with '))
    root.appendChild(makeSkillRef('memory'))

    expect(collectComposerDraftSegments(root)).toEqual([
      { type: 'text', value: 'Check ' },
      { type: 'file', relativePath: 'src/foo.ts' },
      { type: 'text', value: ' with ' },
      { type: 'skill', skillName: 'memory' }
    ])
  })

  it('rebuilds editor DOM from structured segments without changing serialized text', () => {
    const root = document.createElement('div')
    const segments: ComposerDraftSegment[] = [
      { type: 'text', value: 'Check ' },
      { type: 'file', relativePath: 'src/foo.ts' },
      { type: 'text', value: ' and ' },
      { type: 'skill', skillName: 'memory' }
    ]

    root.appendChild(buildEditorFragmentFromSegments(segments))

    expect(root.querySelector(`.${FILE_REF_CLASS}`)).not.toBeNull()
    expect(root.querySelector(`.${SKILL_REF_CLASS}`)).not.toBeNull()
    expect(serializeEditor(root)).toBe('Check @src/foo.ts and $memory')
  })

  it('keeps default fragment output unchanged when spacer insertion is not requested', () => {
    const root = document.createElement('div')
    const segments: ComposerDraftSegment[] = [
      { type: 'file', relativePath: 'src/foo.ts' },
      { type: 'skill', skillName: 'memory' }
    ]

    root.appendChild(buildEditorFragmentFromSegments(segments))

    expect(Array.from(root.childNodes)).toHaveLength(2)
    expect(root.childNodes[0]).toBe(root.querySelector(`.${FILE_REF_CLASS}`))
    expect(root.childNodes[1]).toBe(root.querySelector(`.${SKILL_REF_CLASS}`))
  })

  it('adds non-breaking space text nodes after ref segments when requested', () => {
    const root = document.createElement('div')
    const segments: ComposerDraftSegment[] = [
      { type: 'file', relativePath: 'src/foo.ts' },
      { type: 'skill', skillName: 'memory' }
    ]

    root.appendChild(buildEditorFragmentFromSegments(segments, { addSpacers: true }))

    const children = Array.from(root.childNodes)
    expect(children).toHaveLength(4)
    expect(children[0]).toBe(root.querySelector(`.${FILE_REF_CLASS}`))
    expect(children[1]?.nodeType).toBe(Node.TEXT_NODE)
    expect(children[1]?.textContent).toBe('\u00a0')
    expect(children[2]).toBe(root.querySelector(`.${SKILL_REF_CLASS}`))
    expect(children[3]?.nodeType).toBe(Node.TEXT_NODE)
    expect(children[3]?.textContent).toBe('\u00a0')
  })

  it('parses only catalog-matched skill markers into skill segments', () => {
    expect(parseComposerTextWithCatalog('$memory $unknown', {
      skills: [{ name: 'memory' }]
    })).toEqual([
      { type: 'skill', skillName: 'memory' },
      { type: 'text', value: ' $unknown' }
    ])
  })

  it('picks the earliest matching ref when $skill precedes @file', () => {
    expect(parseComposerTextWithCatalog('$memory @src/foo.ts', {
      skills: [{ name: 'memory' }]
    })).toEqual([
      { type: 'skill', skillName: 'memory' },
      { type: 'text', value: ' ' },
      { type: 'file', relativePath: 'src/foo.ts' }
    ])
  })

})
