import { resolveConversationLink } from '../../../shared/viewer/linkResolver'
import type { InputPart } from '../../types/conversation'
import { splitThreadMentions } from '../../utils/threadReferences'
import { serializeSkillMarker } from './richInputSerialization'

export type UserMessageSegment =
  | { type: 'text'; value: string }
  | { type: 'urlRef'; url: string; label: string }
  | { type: 'fileRef'; relativePath: string; targetPath?: string }
  | { type: 'commandRef'; commandText: string }
  | { type: 'skillRef'; skillName: string }
  | { type: 'threadRef'; threadId: string; title: string }

interface Match {
  type: 'file' | 'url' | 'command' | 'skill'
  start: number
  end: number
  value: string
  targetPath?: string
  url?: string
  label?: string
}

function findNextMarkdownLink(text: string, from: number): Match | null {
  const pattern = /(?<!!)\[([^\]\n]+)\]\(([^\s()]+)\)/g
  pattern.lastIndex = from
  for (let match = pattern.exec(text); match; match = pattern.exec(text)) {
    const target = match[2]!
    if (/^[#?]/.test(target)) continue
    const resolution = resolveConversationLink({ target, workspacePath: '' })
    if (resolution.kind === 'file') {
      return { type: 'file', start: match.index, end: pattern.lastIndex, value: resolution.absolutePath, targetPath: target }
    }
    if (resolution.kind === 'browser' || resolution.kind === 'external') {
      return { type: 'url', start: match.index, end: pattern.lastIndex, value: target, url: resolution.url, label: match[1]! }
    }
  }
  return null
}

const URL_STOP = /[\s<>"'`\u2013\u2014\u2018\u2019\u201c\u201d\u2026\u3000-\u303f\uff00-\uffef]|\p{Script=Han}|\p{Script=Hiragana}|\p{Script=Katakana}|\p{Script=Hangul}|\p{Extended_Pictographic}/u
const URL_TRAILING = /[.,;:!?'"\u2026\u3001\u3002\uff0c\uff1a\uff1b\uff01\uff1f]/

function trimUrl(value: string): string {
  let end = value.length
  while (end > 0) {
    const char = value[end - 1]!
    if (URL_TRAILING.test(char) || hasUnmatchedCloser(value, end, char)) {
      end -= 1
      continue
    }
    break
  }
  return value.slice(0, end)
}

function hasUnmatchedCloser(value: string, end: number, char: string): boolean {
  const opener = char === ')' ? '(' : char === ']' ? '[' : char === '}' ? '{' : ''
  if (!opener) return false
  let open = 0
  let close = 0
  for (let index = 0; index < end; index += 1) {
    if (value[index] === opener) open += 1
    else if (value[index] === char) close += 1
  }
  return close > open
}

function findNextUrl(text: string, from: number): Match | null {
  const pattern = /https?:\/\//gi
  pattern.lastIndex = from
  for (let start = pattern.exec(text); start; start = pattern.exec(text)) {
    if (start.index > 0 && /[A-Za-z0-9]/.test(text[start.index - 1]!)) {
      pattern.lastIndex = start.index + 1
      continue
    }
    let end = start.index + start[0].length
    while (end < text.length) {
      const char = String.fromCodePoint(text.codePointAt(end)!)
      if (URL_STOP.test(char)) break
      end += char.length
    }
    const value = trimUrl(text.slice(start.index, end))
    if (value.length <= start[0].length) {
      pattern.lastIndex = start.index + 1
      continue
    }
    const resolution = resolveConversationLink({ target: value, workspacePath: '' })
    if (resolution.kind === 'browser' || resolution.kind === 'external') {
      return { type: 'url', start: start.index, end: start.index + value.length, value, url: resolution.url }
    }
    pattern.lastIndex = start.index + 1
  }
  return null
}

function findNextFileRef(text: string, from: number): Match | null {
  let i = from
  while (i < text.length) {
    if (text[i] === '@' && (i === 0 || /\s/.test(text[i - 1]!))) {
      let j = i + 1
      while (j < text.length && !/\s/.test(text[j]!)) {
        j += 1
      }
      const relativePath = text.slice(i + 1, j)
      if (relativePath.length > 0) {
        return { type: 'file', start: i, end: j, value: relativePath }
      }
    }
    i += 1
  }
  return null
}

function findNextCommandRef(text: string, from: number): Match | null {
  let i = from
  while (i < text.length) {
    if (text[i] === '/' && (i === 0 || /\s/.test(text[i - 1]!))) {
      let j = i + 1
      while (j < text.length && !/\s/.test(text[j]!)) {
        j += 1
      }
      const token = text.slice(i, j)
      if (/^\/[a-z0-9][a-z0-9-]*$/i.test(token)) {
        return { type: 'command', start: i, end: j, value: token }
      }
    }
    i += 1
  }
  return null
}

function findNextSkillRef(text: string, from: number): Match | null {
  let i = from
  while (i < text.length) {
    if (text[i] === '$' && (i === 0 || /\s/.test(text[i - 1]!))) {
      let j = i + 1
      while (j < text.length && /[a-z0-9_-]/i.test(text[j]!)) {
        j += 1
      }
      const skillName = text.slice(i + 1, j)
      if (skillName.length > 0) {
        return { type: 'skill', start: i, end: j, value: skillName }
      }
    }
    i += 1
  }

  return null
}

/**
 * Splits user message text into plain text, URL links, @fileRef and Markdown file link segments,
 * slash-command fallback segments, skill marker segments, and thread mentions.
 */
export function parseUserMessageSegments(text: string): UserMessageSegment[] {
  return splitThreadMentions(text).flatMap((piece): UserMessageSegment[] => piece.type === 'thread'
    ? [{ type: 'threadRef', threadId: piece.threadId, title: piece.title }]
    : parseMarkerSegments(piece.value))
}

function parseMarkerSegments(text: string): UserMessageSegment[] {
  const out: UserMessageSegment[] = []
  const source = text
  let cursor = 0

  while (cursor < source.length) {
    const next = [findNextMarkdownLink(source, cursor), findNextUrl(source, cursor), findNextFileRef(source, cursor), findNextCommandRef(source, cursor), findNextSkillRef(source, cursor)]
      .filter((candidate): candidate is Match => candidate != null)
      .sort((a, b) => a.start - b.start)[0]

    if (!next) {
      if (source.length > cursor) {
        out.push({ type: 'text', value: source.slice(cursor) })
      }
      break
    }

    if (next.start > cursor) {
      out.push({ type: 'text', value: source.slice(cursor, next.start) })
    }

    if (next.type === 'file') {
      out.push({ type: 'fileRef', relativePath: next.value, ...(next.targetPath ? { targetPath: next.targetPath } : {}) })
    } else if (next.type === 'url') {
      out.push({ type: 'urlRef', url: next.url!, label: next.label ?? next.value })
    } else if (next.type === 'command') {
      out.push({ type: 'commandRef', commandText: next.value })
    } else {
      out.push({ type: 'skillRef', skillName: next.value })
    }

    cursor = next.end
  }

  return out
}

export function segmentsFromNativeInputParts(parts: InputPart[]): UserMessageSegment[] {
  const out: UserMessageSegment[] = []
  for (const part of parts) {
    switch (part.type) {
      case 'text': {
        out.push(...parseUserMessageSegments(part.text))
        break
      }
      case 'fileRef':
        {
          const displayPath = part.displayPath ?? part.path
          out.push({
            type: 'fileRef',
            relativePath: displayPath,
            ...(displayPath !== part.path ? { targetPath: part.path } : {})
          })
        }
        break
      case 'commandRef':
        {
          const name = typeof part.name === 'string' ? part.name.trim() : ''
          if (!name) {
            if (typeof part.rawText === 'string' && part.rawText.trim() !== '') {
              out.push({ type: 'text', value: part.rawText })
            }
            break
          }

          out.push({ type: 'commandRef', commandText: `/${name}` })

          let argsText = typeof part.argsText === 'string' ? part.argsText.trim() : ''
          if (argsText.length === 0 && typeof part.rawText === 'string') {
            const normalizedRaw = part.rawText.trim()
            const firstWhitespace = normalizedRaw.search(/\s/)
            if (firstWhitespace >= 0) {
              argsText = normalizedRaw.slice(firstWhitespace + 1).trim()
            }
          }
          if (argsText.length > 0) {
            out.push({ type: 'text', value: ` ${argsText}` })
          }
        }
        break
      case 'skillRef':
        out.push({ type: 'skillRef', skillName: part.name })
        break
      default:
        break
    }
  }
  return out
}

export function stringifySkillMarker(skillName: string): string {
  return serializeSkillMarker(skillName)
}
