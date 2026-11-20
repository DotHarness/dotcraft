import type { KeyboardEvent } from 'react'
import { addToast } from '../../stores/toastStore'

type Translate = (key: string) => string

const handlers = new Map<string, () => void>()

export function registerComposerSend(threadId: string, send: () => void): () => void {
  handlers.set(threadId, send)
  return () => {
    if (handlers.get(threadId) === send) handlers.delete(threadId)
  }
}

export function sendComposer(threadId: string, t: Translate): void {
  const send = handlers.get(threadId)
  if (send) send()
  else addToast(t('composer.context.addedNotSent'), 'info')
}

export function isSendNowShortcut(event: KeyboardEvent): boolean {
  return event.key === 'Enter' && (event.ctrlKey || event.metaKey) && !event.shiftKey && !event.altKey
}
