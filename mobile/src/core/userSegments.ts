import { splitMessageComments, type MessageComment } from './messageComments'

export type UserSegment =
  | { type: 'text'; value: string }
  | { type: 'file'; path: string; display: string }
  | { type: 'skill'; name: string }

function markerAt(text: string, from: number): { start: number; end: number; segment: UserSegment } | null {
  for (let index = from; index < text.length; index += 1) {
    const marker = text[index]
    if ((marker !== '@' && marker !== '$') || (index > 0 && !/\s/.test(text[index - 1]))) continue
    let end = index + 1
    const allowed = marker === '@' ? /\S/ : /[a-z0-9_-]/i
    while (end < text.length && allowed.test(text[end])) end += 1
    const value = text.slice(index + 1, end)
    if (!value) continue
    return { start: index, end, segment: marker === '@' ? { type: 'file', path: value, display: value } : { type: 'skill', name: value } }
  }
  return null
}

function parseUserText(text: string): UserSegment[] {
  const out: UserSegment[] = []
  let cursor = 0
  while (cursor < text.length) {
    const next = markerAt(text, cursor)
    if (!next) {
      out.push({ type: 'text', value: text.slice(cursor) })
      break
    }
    if (next.start > cursor) out.push({ type: 'text', value: text.slice(cursor, next.start) })
    out.push(next.segment)
    cursor = next.end
  }
  return out
}

type InputPart = { type?: string; text?: string; path?: string; displayPath?: string; name?: string; rawText?: string; argsText?: string }

export function userSegments(text: string, parts: unknown): UserSegment[] {
  const comments: MessageComment[] = []
  const plain = (value: string) => {
    const split = splitMessageComments(value)
    comments.push(...split.comments)
    return parseUserText(split.text)
  }
  const segments = !Array.isArray(parts) || parts.length === 0 ? plain(text) : nativeSegments(parts as InputPart[], plain)
  return [...segments, ...comments.flatMap((comment, index): UserSegment[] => [
    ...(segments.length > 0 || index > 0 ? [{ type: 'text' as const, value: '\n' }] : []),
    { type: 'file', path: comment.path, display: comment.label },
    ...(comment.comment ? [{ type: 'text' as const, value: ` ${comment.comment}` }] : []),
  ])]
}

function nativeSegments(parts: InputPart[], plain: (value: string) => UserSegment[]): UserSegment[] {
  return parts.flatMap((part): UserSegment[] => {
    switch (part.type) {
      case 'text':
        return plain(part.text ?? '')
      case 'fileRef':
        return part.path ? [{ type: 'file', path: part.path, display: part.displayPath ?? part.path }] : []
      case 'skillRef':
        return part.name ? [{ type: 'skill', name: part.name }] : []
      case 'commandRef': {
        const command = part.name ? `/${part.name}${part.argsText ? ` ${part.argsText}` : ''}` : (part.rawText ?? '')
        return command ? [{ type: 'text', value: command }] : []
      }
      default:
        return []
    }
  })
}

export type ImageSource = { uri: string } | { path: string }

export function userImages(parts: unknown): ImageSource[] {
  if (!Array.isArray(parts)) return []
  return (parts as { type?: string; url?: unknown; path?: unknown }[]).flatMap((part): ImageSource[] => {
    if (part.type === 'image' && typeof part.url === 'string' && part.url.startsWith('data:image/')) return [{ uri: part.url }]
    if (part.type === 'localImage' && typeof part.path === 'string' && part.path) return [{ path: part.path }]
    return []
  })
}
