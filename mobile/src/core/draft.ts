import type { InputPart } from '@dotcraft/sdk/contracts'

export type ReferenceKind = 'command' | 'skill'

export interface InlineReference {
  kind: ReferenceKind
  name: string
  start: number
}

export type DraftPiece = { type: 'text'; value: string } | { type: 'reference'; reference: InlineReference }

export interface PhotoAttachment {
  id: string
  dataUrl: string
}

export interface FileAttachment {
  id: string
  name: string
  dataBase64: string
}

export interface MessageDraft {
  text: string
  references: InlineReference[]
  photos: PhotoAttachment[]
  files: FileAttachment[]
}

export interface ReferenceEntry {
  kind: ReferenceKind
  name: string
  description: string
}

export interface PickerMatch {
  trigger: '/' | '$'
  query: string
  start: number
  end: number
}

export const EMPTY_DRAFT: MessageDraft = { text: '', references: [], photos: [], files: [] }

const NAME_CHAR = /[\w.:-]/

export function plainMessage(text: string): MessageDraft {
  return { ...EMPTY_DRAFT, text }
}

export function isEmptyDraft(draft: MessageDraft): boolean {
  return draft.text.trim().length === 0 && draft.photos.length === 0 && draft.files.length === 0
}

export function pickerMatch(text: string, cursor: number = text.length): PickerMatch | null {
  const at = Math.min(Math.max(cursor, 0), text.length)
  const found = /(^|\s)([/$])([\w.:-]*)$/.exec(text.slice(0, at))
  if (!found) return null
  let end = at
  while (end < text.length && NAME_CHAR.test(text[end])) end += 1
  return { trigger: found[2] as PickerMatch['trigger'], query: found[3], start: found.index + found[1].length, end }
}

export function matchingEntries(entries: ReferenceEntry[], match: PickerMatch): ReferenceEntry[] {
  const query = match.query.toLowerCase()
  const rank = (entry: ReferenceEntry) => (entry.kind === 'command' ? 0 : 2) + (entry.name.toLowerCase().startsWith(query) ? 0 : 1)
  return entries
    .filter((entry) => (match.trigger === '/' || entry.kind === 'skill') && entry.name.toLowerCase().includes(query))
    .sort((left, right) => rank(left) - rank(right))
}

export function referenceToken(reference: Pick<InlineReference, 'kind' | 'name'>): string {
  return `${reference.kind === 'command' ? '/' : '$'}${reference.name}`
}

function endOf(reference: InlineReference): number {
  return reference.start + referenceToken(reference).length
}

export function referencePicker(draft: MessageDraft, cursor: number): PickerMatch | null {
  const match = pickerMatch(draft.text, cursor)
  if (!match) return null
  return draft.references.some((reference) => reference.start < match.end && endOf(reference) > match.start) ? null : match
}

export interface DraftEdit {
  draft: MessageDraft
  cursor: number
}

function shifted(references: InlineReference[], from: number, delta: number): InlineReference[] {
  return references.map((reference) => (reference.start >= from ? { ...reference, start: reference.start + delta } : reference))
}

export function chooseEntry(draft: MessageDraft, match: PickerMatch, entry: ReferenceEntry): DraftEdit {
  const token = referenceToken(entry)
  const after = draft.text.slice(match.end)
  const inserted = /^\s/.test(after) ? token : `${token} `
  const references = [
    ...shifted(draft.references, match.end, inserted.length - (match.end - match.start)),
    { kind: entry.kind, name: entry.name, start: match.start },
  ].sort((left, right) => left.start - right.start)
  return {
    draft: { ...draft, text: draft.text.slice(0, match.start) + inserted + after, references },
    cursor: match.start + token.length + 1,
  }
}

export function editText(draft: MessageDraft, next: string): DraftEdit {
  const previous = draft.text
  const limit = Math.min(previous.length, next.length)
  let prefix = 0
  while (prefix < limit && previous[prefix] === next[prefix]) prefix += 1
  let suffix = 0
  while (suffix < limit - prefix && previous[previous.length - 1 - suffix] === next[next.length - 1 - suffix]) suffix += 1
  let from = prefix
  let to = previous.length - suffix
  const inserted = next.slice(prefix, next.length - suffix)
  const hit = draft.references.filter((reference) =>
    from < to ? reference.start < to && endOf(reference) > from : reference.start < from && endOf(reference) > from,
  )
  for (const reference of hit) {
    from = Math.min(from, reference.start)
    to = Math.max(to, endOf(reference))
  }
  const kept = draft.references.filter((reference) => !hit.includes(reference))
  return {
    draft: {
      ...draft,
      text: previous.slice(0, from) + inserted + previous.slice(to),
      references: shifted(kept, to, inserted.length - (to - from)),
    },
    cursor: from + inserted.length,
  }
}

export function draftPieces(draft: MessageDraft): DraftPiece[] {
  const pieces: DraftPiece[] = []
  let at = 0
  for (const reference of draft.references) {
    if (reference.start > at) pieces.push({ type: 'text', value: draft.text.slice(at, reference.start) })
    pieces.push({ type: 'reference', reference })
    at = endOf(reference)
  }
  if (at < draft.text.length) pieces.push({ type: 'text', value: draft.text.slice(at) })
  return pieces
}

function bodyParts(draft: MessageDraft): InputPart[] {
  const pieces = draftPieces(draft)
  return pieces.flatMap((piece, index): InputPart[] => {
    if (piece.type === 'reference') {
      const { kind, name } = piece.reference
      return [kind === 'command' ? { type: 'commandRef', name, rawText: `/${name}` } : { type: 'skillRef', name }]
    }
    let value = piece.value
    const previous = pieces[index - 1]
    const next = pieces[index + 1]
    if (!previous) value = value.trimStart()
    else if (!/^\s/.test(value)) value = ` ${value}`
    if (!next) value = value.trimEnd()
    else if (!/\s$/.test(value)) value = `${value} `
    return value.trim() ? [{ type: 'text', text: value }] : []
  })
}

export function inputParts(
  draft: MessageDraft,
  filePaths: { path: string; name: string }[] = [],
  photos?: { path: string; fileName: string; mimeType: string }[],
): InputPart[] {
  const parts: InputPart[] = []
  filePaths.forEach(({ path, name }, index) => {
    parts.push({ type: 'fileRef', path, displayPath: name })
    if (index < filePaths.length - 1) parts.push({ type: 'text', text: '\n' })
  })
  const body = bodyParts(draft)
  if (parts.length > 0 && body.length > 0) parts.push({ type: 'text', text: '\n\n' })
  parts.push(...body)
  if (photos) for (const photo of photos) parts.push({ type: 'localImage', path: photo.path, mimeType: photo.mimeType, fileName: photo.fileName })
  else for (const photo of draft.photos) parts.push({ type: 'image', url: photo.dataUrl })
  return parts
}

export function visibleText(parts: InputPart[]): string {
  return parts
    .map((part) => {
      switch (part.type) {
        case 'text':
          return part.text ?? ''
        case 'commandRef':
          return part.rawText ?? `/${part.name ?? ''}`
        case 'skillRef':
          return `$${part.name ?? ''}`
        case 'fileRef':
          return `@${part.displayPath ?? part.path ?? ''}`
        default:
          return ''
      }
    })
    .join('')
    .trim()
}

export function draftTitle(draft: MessageDraft): string {
  return visibleText(bodyParts(draft)) || draft.files[0]?.name || ''
}
