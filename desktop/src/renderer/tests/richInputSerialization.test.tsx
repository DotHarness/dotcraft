import { describe, expect, it } from 'vitest'
import { COMMAND_REF_CLASS, FILE_REF_CLASS, SKILL_REF_CLASS } from '../components/conversation/richInputConstants'
import {
  buildEditorFragmentFromSegments,
  collectComposerDraftSegments,
  parseComposerTextWithCatalog,
  parseLegacyComposerText,
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

function makeCommandRef(command: string): HTMLSpanElement {
  const s = document.createElement('span')
  s.className = COMMAND_REF_CLASS
  s.setAttribute('data-command', command)
  s.textContent = command
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

  it('serializes command refs to slash command text', () => {
    const root = document.createElement('div')
    root.appendChild(makeCommandRef('/create-skill'))
    root.appendChild(document.createTextNode(' with args'))
    expect(serializeEditor(root)).toBe('/create-skill with args')
  })

  it('drops command refs that do not fit entirely before the cut', () => {
    const root = document.createElement('div')
    root.appendChild(document.createTextNode('x'))
    root.appendChild(makeCommandRef('/toolong'))
    root.appendChild(document.createTextNode('tail'))
    truncateEditorDomToSerializedLength(root, 5)
    expect(serializeEditor(root)).toBe('x')
    expect(root.querySelectorAll(`.${COMMAND_REF_CLASS}`).length).toBe(0)
  })

  it('collects mixed text and ref segments from editor DOM', () => {
    const root = document.createElement('div')
    root.appendChild(document.createTextNode('Check '))
    root.appendChild(makeFileRef('src/foo.ts'))
    root.appendChild(document.createTextNode(' then '))
    root.appendChild(makeCommandRef('/code-review'))
    root.appendChild(document.createTextNode(' with '))
    root.appendChild(makeSkillRef('memory'))

    expect(collectComposerDraftSegments(root)).toEqual([
      { type: 'text', value: 'Check ' },
      { type: 'file', relativePath: 'src/foo.ts' },
      { type: 'text', value: ' then ' },
      { type: 'command', command: '/code-review' },
      { type: 'text', value: ' with ' },
      { type: 'skill', skillName: 'memory' }
    ])
  })

  it('rebuilds editor DOM from structured segments without changing serialized text', () => {
    const root = document.createElement('div')
    const segments: ComposerDraftSegment[] = [
      { type: 'text', value: 'Check ' },
      { type: 'file', relativePath: 'src/foo.ts' },
      { type: 'text', value: ' then ' },
      { type: 'command', command: '/code-review' },
      { type: 'text', value: ' and ' },
      { type: 'skill', skillName: 'memory' }
    ]

    root.appendChild(buildEditorFragmentFromSegments(segments))

    expect(root.querySelector(`.${FILE_REF_CLASS}`)).not.toBeNull()
    expect(root.querySelector(`.${COMMAND_REF_CLASS}`)).not.toBeNull()
    expect(root.querySelector(`.${SKILL_REF_CLASS}`)).not.toBeNull()
    // Visual treatment now comes from the shared inline-reference chip classes
    // (tokens.css .dc-ref*), not inline styles.
    const commandRef = root.querySelector(`.${COMMAND_REF_CLASS}`) as HTMLElement
    expect(commandRef.classList.contains('dc-ref')).toBe(true)
    expect(commandRef.classList.contains('dc-ref-command')).toBe(true)
    expect(serializeEditor(root)).toBe('Check @src/foo.ts then /code-review and $memory')
  })

  it('keeps default fragment output unchanged when spacer insertion is not requested', () => {
    const root = document.createElement('div')
    const segments: ComposerDraftSegment[] = [
      { type: 'file', relativePath: 'src/foo.ts' },
      { type: 'command', command: '/code-review' },
      { type: 'skill', skillName: 'memory' }
    ]

    root.appendChild(buildEditorFragmentFromSegments(segments))

    expect(Array.from(root.childNodes)).toHaveLength(3)
    expect(root.childNodes[0]).toBe(root.querySelector(`.${FILE_REF_CLASS}`))
    expect(root.childNodes[1]).toBe(root.querySelector(`.${COMMAND_REF_CLASS}`))
    expect(root.childNodes[2]).toBe(root.querySelector(`.${SKILL_REF_CLASS}`))
  })

  it('adds non-breaking space text nodes after ref segments when requested', () => {
    const root = document.createElement('div')
    const segments: ComposerDraftSegment[] = [
      { type: 'file', relativePath: 'src/foo.ts' },
      { type: 'command', command: '/code-review' },
      { type: 'skill', skillName: 'memory' }
    ]

    root.appendChild(buildEditorFragmentFromSegments(segments, { addSpacers: true }))

    const children = Array.from(root.childNodes)
    expect(children).toHaveLength(6)
    expect(children[0]).toBe(root.querySelector(`.${FILE_REF_CLASS}`))
    expect(children[1]?.nodeType).toBe(Node.TEXT_NODE)
    expect(children[1]?.textContent).toBe('\u00a0')
    expect(children[2]).toBe(root.querySelector(`.${COMMAND_REF_CLASS}`))
    expect(children[3]?.nodeType).toBe(Node.TEXT_NODE)
    expect(children[3]?.textContent).toBe('\u00a0')
    expect(children[4]).toBe(root.querySelector(`.${SKILL_REF_CLASS}`))
    expect(children[5]?.nodeType).toBe(Node.TEXT_NODE)
    expect(children[5]?.textContent).toBe('\u00a0')
  })

  it('parses legacy draft text into file, command, and skill segments conservatively', () => {
    expect(parseLegacyComposerText('Check @src/foo.ts /code-review $memory now')).toEqual([
      { type: 'text', value: 'Check ' },
      { type: 'file', relativePath: 'src/foo.ts' },
      { type: 'text', value: ' ' },
      { type: 'command', command: '/code-review' },
      { type: 'text', value: ' ' },
      { type: 'skill', skillName: 'memory' },
      { type: 'text', value: ' now' }
    ])
  })

  it('parses underscore skill names as a single legacy draft segment', () => {
    expect(parseLegacyComposerText('$browser_use ok')).toEqual([
      { type: 'skill', skillName: 'browser_use' },
      { type: 'text', value: ' ok' }
    ])
  })

  it('does not misclassify plain text email or slash path as refs in legacy parsing', () => {
    expect(parseLegacyComposerText('email user@domain.com and path /usr/bin ok')).toEqual([
      { type: 'text', value: 'email user@domain.com and path /usr/bin ok' }
    ])
  })

  it('parses only catalog-matched skill markers into skill segments', () => {
    expect(parseComposerTextWithCatalog('$memory $unknown', {
      skills: [{ name: 'memory', available: true }]
    })).toEqual([
      { type: 'skill', skillName: 'memory' },
      { type: 'text', value: ' $unknown' }
    ])
  })

  it('picks the earliest matching ref when $skill precedes @file', () => {
    expect(parseComposerTextWithCatalog('$memory @src/foo.ts', {
      skills: [{ name: 'memory', available: true }]
    })).toEqual([
      { type: 'skill', skillName: 'memory' },
      { type: 'text', value: ' ' },
      { type: 'file', relativePath: 'src/foo.ts' }
    ])
  })

  it('parses slash commands before slash skills when both catalogs are present', () => {
    expect(parseComposerTextWithCatalog('/code-review /memory /unknown', {
      commands: [{ name: '/code-review', aliases: ['/cr'] }],
      skills: [{ name: 'memory', available: true }]
    })).toEqual([
      { type: 'command', command: '/code-review' },
      { type: 'text', value: ' ' },
      { type: 'skill', skillName: 'memory' },
      { type: 'text', value: ' /unknown' }
    ])
  })
})
