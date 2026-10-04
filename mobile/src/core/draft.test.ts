import { describe, expect, it } from 'vitest'
import { awaitsPlanConfirmation } from './chatState'
import {
  chooseEntry,
  draftPieces,
  editText,
  EMPTY_DRAFT,
  inputParts,
  matchingEntries,
  pickerMatch,
  referencePicker,
  type MessageDraft,
  type ReferenceEntry,
} from './draft'

const ENTRIES: ReferenceEntry[] = [
  { kind: 'skill', name: 'release-notes', description: '' },
  { kind: 'command', name: 'code-review', description: '' },
  { kind: 'skill', name: 'browser', description: '' },
  { kind: 'command', name: 'release-check', description: '' },
]

function typed(text: string): MessageDraft {
  return { ...EMPTY_DRAFT, text }
}

describe('picker', () => {
  it('opens only for a slash or dollar at the start of a word', () => {
    expect(pickerMatch('/')).toEqual({ trigger: '/', query: '', start: 0, end: 1 })
    expect(pickerMatch('please run /rev')).toEqual({ trigger: '/', query: 'rev', start: 11, end: 15 })
    expect(pickerMatch('use $bro')).toEqual({ trigger: '$', query: 'bro', start: 4, end: 8 })
    expect(pickerMatch('src/foo')).toBeNull()
    expect(pickerMatch('costs 5$')).toBeNull()
    expect(pickerMatch('/code-review now')).toBeNull()
  })

  it('reads the word at the cursor, including what follows it', () => {
    expect(pickerMatch('run /re later', 7)).toEqual({ trigger: '/', query: 're', start: 4, end: 7 })
    expect(pickerMatch('run /release later', 7)).toEqual({ trigger: '/', query: 're', start: 4, end: 12 })
  })

  it('offers commands and skills for a slash and only skills for a dollar, prefix matches first', () => {
    const names = (text: string) => matchingEntries(ENTRIES, pickerMatch(text)!).map((entry) => `${entry.kind}:${entry.name}`)
    expect(names('/')).toEqual(['command:code-review', 'command:release-check', 'skill:release-notes', 'skill:browser'])
    expect(names('/re')).toEqual(['command:release-check', 'command:code-review', 'skill:release-notes'])
    expect(names('$')).toEqual(['skill:release-notes', 'skill:browser'])
    expect(names('$code')).toEqual([])
  })
})

function typeInto(draft: MessageDraft, next: string): MessageDraft {
  return editText(draft, next).draft
}

function choose(draft: MessageDraft, entry: ReferenceEntry, cursor = draft.text.length): MessageDraft {
  return chooseEntry(draft, referencePicker(draft, cursor)!, entry).draft
}

describe('inline references', () => {
  it('replaces the typed query with the reference at the cursor and puts the cursor after it', () => {
    const draft = typed('run /re now')
    const { draft: chosen, cursor } = chooseEntry(draft, pickerMatch(draft.text, 7)!, ENTRIES[3])
    expect(chosen.text).toBe('run /release-check now')
    expect(chosen.references).toEqual([{ kind: 'command', name: 'release-check', start: 4 }])
    expect(cursor).toBe(19)
    expect(draftPieces(chosen)).toEqual([
      { type: 'text', value: 'run ' },
      { type: 'reference', reference: { kind: 'command', name: 'release-check', start: 4 } },
      { type: 'text', value: ' now' },
    ])
  })

  it('writes a skill as $name even when the slash picker chose it, and adds a space after a reference at the end', () => {
    const chosen = choose(typed('ask /bro'), ENTRIES[2])
    expect(chosen.text).toBe('ask $browser ')
    expect(chosen.references).toEqual([{ kind: 'skill', name: 'browser', start: 4 }])
  })

  it('removes the whole reference when Backspace reaches it and keeps the others in place', () => {
    let draft = choose(typed('ask $b'), ENTRIES[2])
    draft = typeInto(draft, `${draft.text}then /co`)
    draft = choose(draft, ENTRIES[1])
    expect(draft.text).toBe('ask $browser then /code-review ')
    const backspaced = editText(draft, 'ask $browse then /code-review ')
    expect(backspaced.draft.text).toBe('ask  then /code-review ')
    expect(backspaced.draft.references).toEqual([{ kind: 'command', name: 'code-review', start: 10 }])
    expect(backspaced.cursor).toBe(4)
  })

  it('removes a reference that any edit cuts into, and keeps typing right after one as plain text', () => {
    const draft = choose(typed('$rel'), ENTRIES[0])
    expect(typeInto(draft, '$release-xnotes ')).toEqual({ ...EMPTY_DRAFT, text: 'x ', references: [] })
    const after = typeInto({ ...draft, text: '$release-notes' }, '$release-notes!')
    expect(after.references).toEqual([{ kind: 'skill', name: 'release-notes', start: 0 }])
    const before = typeInto(draft, 'go $release-notes ')
    expect(before.references).toEqual([{ kind: 'skill', name: 'release-notes', start: 3 }])
  })

  it('does not open the picker on a reference already in the text', () => {
    const draft = typeInto(choose(typed('/rev'), ENTRIES[1]), '/code-review')
    expect(referencePicker(draft, draft.text.length)).toBeNull()
  })
})

describe('draft to input parts', () => {
  it('sends references between the literal text, in typed order', () => {
    let draft = choose(typed('please /rev'), ENTRIES[1])
    draft = typeInto(draft, `${draft.text}the diff with $rel`)
    draft = choose(draft, ENTRIES[0])
    draft = typeInto(draft, `${draft.text}tonight `)
    expect(inputParts(draft)).toEqual([
      { type: 'text', text: 'please ' },
      { type: 'commandRef', name: 'code-review', rawText: '/code-review' },
      { type: 'text', text: ' the diff with ' },
      { type: 'skillRef', name: 'release-notes' },
      { type: 'text', text: ' tonight' },
    ])
  })

  it('puts uploaded files first and photos last', () => {
    const draft: MessageDraft = { ...typed('compare these'), photos: [{ id: 'p1', dataUrl: 'data:image/jpeg;base64,AAAA' }] }
    expect(
      inputParts(draft, [
        { path: 'D:/p/.craft/attachments/a/log.txt', name: 'log.txt' },
        { path: 'D:/p/.craft/attachments/b/spec.pdf', name: 'spec.pdf' },
      ]),
    ).toEqual([
      { type: 'fileRef', path: 'D:/p/.craft/attachments/a/log.txt', displayPath: 'log.txt' },
      { type: 'text', text: '\n' },
      { type: 'fileRef', path: 'D:/p/.craft/attachments/b/spec.pdf', displayPath: 'spec.pdf' },
      { type: 'text', text: '\n\n' },
      { type: 'text', text: 'compare these' },
      { type: 'image', url: 'data:image/jpeg;base64,AAAA' },
    ])
  })
})

describe('plan confirmation', () => {
  const idle = { running: false, busy: false, waitingOnApproval: false, waitingOnInput: false }

  it('follows the server flag while the chat is in plan mode and idle', () => {
    expect(awaitsPlanConfirmation({ ...idle, waitingOnPlanConfirmation: true }, 'plan')).toBe(true)
    expect(awaitsPlanConfirmation({ ...idle, waitingOnPlanConfirmation: true }, 'agent')).toBe(false)
    expect(awaitsPlanConfirmation({ ...idle, waitingOnPlanConfirmation: false }, 'plan')).toBe(false)
    expect(awaitsPlanConfirmation({ ...idle, running: true, activeTurnId: 't2', waitingOnPlanConfirmation: true }, 'plan')).toBe(false)
  })
})
