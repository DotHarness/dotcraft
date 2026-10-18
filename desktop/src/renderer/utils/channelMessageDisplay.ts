import { translate, type AppLocale } from '../../shared/locales'
import type { ConversationItem } from '../types/conversation'
import { getFilename } from './toolCallDisplay'

export interface ChannelMessageAttachment {
  name: string
  sentAsFile: boolean
}

export interface ChannelMessageDisplay {
  label: string
  text: string | null
  attachments: ChannelMessageAttachment[]
  error: string | null
}

interface ChannelMessageResult {
  displayName: string
  label: string
  text: string | null
  parts: Record<string, unknown>[]
  error: string | null
}

export function getChannelMessageDisplay(
  item: ConversationItem,
  running: boolean,
  locale: AppLocale
): ChannelMessageDisplay {
  const args = item.arguments
  const requested = readAttachmentArguments(args?.attachments)
  const result = running ? null : readResult(item.structuredResult)
  const failed = !running && item.success === false
  return {
    label: formatLabel(result, running, failed, locale),
    text: result?.text ?? nonBlank(args?.text),
    attachments: result ? deliveredAttachments(result.parts, requested) : requested,
    error: failed ? result?.error ?? nonBlank(item.errorMessage) ?? nonBlank(item.result) : null
  }
}

export function formatChannelMessageRunningLabel(locale: AppLocale): string {
  return translate(locale, 'toolCall.channelMessage.sending')
}

function formatLabel(
  result: ChannelMessageResult | null,
  running: boolean,
  failed: boolean,
  locale: AppLocale
): string {
  if (running) return formatChannelMessageRunningLabel(locale)
  if (!result) {
    return translate(locale, failed ? 'toolCall.channelMessage.failedGeneric' : 'toolCall.channelMessage.sentGeneric')
  }
  const vars = { displayName: result.displayName, label: result.label }
  if (failed) return translate(locale, 'toolCall.channelMessage.failed', vars)
  return translate(locale, result.label ? 'toolCall.channelMessage.sentWithLabel' : 'toolCall.channelMessage.sent', vars)
}

function readResult(value: unknown): ChannelMessageResult | null {
  if (!isRecord(value)) return null
  const displayName = nonBlank(value.displayName) ?? nonBlank(value.channelName)
  if (!displayName) return null
  const error = isRecord(value.error) ? nonBlank(value.error.message) : null
  return {
    displayName,
    label: nonBlank(value.label) ?? '',
    text: nonBlank(value.text),
    parts: Array.isArray(value.parts) ? value.parts.filter(isRecord) : [],
    error
  }
}

function deliveredAttachments(
  parts: Record<string, unknown>[],
  requested: ChannelMessageAttachment[]
): ChannelMessageAttachment[] {
  return parts
    .filter((part) => part.kind !== 'text')
    .map((part, index) => ({
      name: nonBlank(part.name) ?? requested[index]?.name ?? String(part.kind ?? ''),
      sentAsFile: part.downgradedFrom === 'audio' || part.downgradedFrom === 'video'
    }))
}

function readAttachmentArguments(value: unknown): ChannelMessageAttachment[] {
  if (!Array.isArray(value)) return []
  return value.filter(isRecord).flatMap((attachment) => {
    const name = nonBlank(attachment.name) ?? (nonBlank(attachment.path) ? getFilename(String(attachment.path)) : null)
    return name ? [{ name, sentAsFile: false }] : []
  })
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value != null && !Array.isArray(value)
}

function nonBlank(value: unknown): string | null {
  return typeof value === 'string' && value.trim() ? value : null
}
