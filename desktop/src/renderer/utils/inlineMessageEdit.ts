import { splitMessageComments } from '../../shared/messageComments'
import type { ConversationItem, InputPart } from '../types/conversation'
import { stripSystemReminderBlocks } from './systemReminderText'
import { buildThreadReferencesContext, mentionedThreadIds, splitThreadMentions } from './threadReferences'

export function isInlineEditableUserMessage(item: ConversationItem): boolean {
  if (item.type !== 'userMessage') return false
  const parts = inlineEditInput(item)
  return parts.every((part) => part.type === 'text'
    ? splitMessageComments(part.text).comments.length === 0
    : part.type !== 'contextRef' || (part.context.kind !== 'diffAnnotation' && part.context.kind !== 'fileAnnotation'))
}

export function inlineEditInput(item: ConversationItem): InputPart[] {
  if (item.nativeInputParts?.length) return item.nativeInputParts
  const input: InputPart[] = []
  if (item.text) input.push({ type: 'text', text: stripSystemReminderBlocks(item.text) })
  for (const image of item.images ?? []) {
    input.push({ type: 'localImage', path: image.path, fileName: image.fileName, mimeType: image.mimeType })
  }
  for (const url of item.imageDataUrls ?? []) input.push({ type: 'image', url })
  return input
}

export function inlineEditText(input: InputPart[]): string {
  return input.filter((part) => part.type === 'text').map((part) => stripSystemReminderBlocks(part.text)).join('')
}

export function hasInlineEditContent(text: string, input: InputPart[]): boolean {
  return text.trim().length > 0 || input.some((part) =>
    part.type !== 'text' && (part.type !== 'contextRef' || part.context.kind !== 'threadReferences'))
}

export function replaceInlineEditText(input: InputPart[], text: string, threadId: string): InputPart[] {
  const result: InputPart[] = []
  let inserted = false
  for (const part of input) {
    if (part.type === 'contextRef' && part.context.kind === 'threadReferences') continue
    if (part.type !== 'text') result.push(part)
    else if (!inserted) {
      if (text.length > 0) result.push({ type: 'text', text })
      inserted = true
    }
  }
  if (!inserted && text.length > 0) result.push({ type: 'text', text })
  const threadIds = mentionedThreadIds(splitThreadMentions(text)).filter((id) => id !== threadId)
  if (threadIds.length > 0) result.unshift({ type: 'contextRef', context: buildThreadReferencesContext(threadIds) })
  return result
}
