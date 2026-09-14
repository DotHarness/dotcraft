import type { ComposerContextRecord } from '../../shared/composerContext'
import type { ConversationItem, InputPart, UserMessageImageRef } from '../types/conversation'

export function projectInputParts(input: InputPart[]): {
  parts: InputPart[]; contexts: ComposerContextRecord[]; images: UserMessageImageRef[]; imageDataUrls: string[]
} {
  const contexts: ComposerContextRecord[] = []
  const images: UserMessageImageRef[] = []
  const imageDataUrls: string[] = []
  const parts: InputPart[] = []
  for (const part of input) {
    if (part.type === 'contextRef') contexts.push(structuredClone(part.context))
    else if (part.type === 'localImage') images.push({ path: part.path, fileName: part.fileName, mimeType: part.mimeType })
    else if (part.type === 'image') imageDataUrls.push(part.url)
    else parts.push(part)
  }
  return { parts, contexts, images, imageDataUrls }
}

export function createOptimisticUserMessage(input: InputPart[], text: string, clientUserMessageId: string, sentAsGoal = false): ConversationItem {
  const projected = projectInputParts(input)
  const now = new Date().toISOString()
  return {
    id: `local-${clientUserMessageId}`, clientUserMessageId, type: 'userMessage', status: 'completed',
    text, nativeInputParts: input, images: projected.images,
    imageDataUrls: projected.imageDataUrls.length ? projected.imageDataUrls : undefined,
    sentAsGoal: sentAsGoal || undefined, createdAt: now, completedAt: now
  }
}
