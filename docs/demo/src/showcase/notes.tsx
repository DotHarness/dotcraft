import { useSyncExternalStore } from 'react'
import { Info } from 'lucide-react'
import { lang } from './params'

const NOTE_MS = 7000
const QUOTE_LIMIT = 140

const copy = {
  en: {
    send: (text: string) => `In DotCraft, this sends “${text}” as your next message.`,
    prefill: (text: string) => `In DotCraft, this puts “${text}” in the message box for you to finish.`,
    copyImage: 'In DotCraft, this copies the view to the clipboard as an image.',
    addChanges: 'In DotCraft, your design changes go into the message box, ready to send.',
    saveAgent: (name: string) => `In DotCraft, this saves “${name}” so you can pick it for any conversation.`
  },
  zh: {
    send: (text: string) => `在 DotCraft 中，这会把“${text}”作为下一条消息发送。`,
    prefill: (text: string) => `在 DotCraft 中，这会把“${text}”填进输入框，等你补充完再发送。`,
    copyImage: '在 DotCraft 中，这会把视图以图片形式复制到剪贴板。',
    addChanges: '在 DotCraft 中，你的设计改动会放进输入框，随消息一起发送。',
    saveAgent: (name: string) => `在 DotCraft 中，这会保存“${name}”，之后任何对话都能选用它。`
  }
}

export const noteCopy = copy[lang]

export function quote(text: string): string {
  const flat = text.replace(/\s+/g, ' ').trim()
  return flat.length > QUOTE_LIMIT ? `${flat.slice(0, QUOTE_LIMIT - 1)}…` : flat
}

let current: { id: number; text: string } | null = null
let sequence = 0
let timer: ReturnType<typeof setTimeout> | undefined
const listeners = new Set<() => void>()

function emit(): void {
  for (const listener of listeners) listener()
}

export function showNote(text: string): void {
  current = { id: ++sequence, text }
  clearTimeout(timer)
  timer = setTimeout(() => {
    current = null
    emit()
  }, NOTE_MS)
  emit()
}

function useNote(): { id: number; text: string } | null {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener)
      return () => listeners.delete(listener)
    },
    () => current
  )
}

export function ShowcaseNote({ floating = false }: { floating?: boolean }): JSX.Element {
  const note = useNote()
  return (
    <div className={floating ? 'showcase-note-region showcase-note-region--floating' : 'showcase-note-region'} role="status" aria-live="polite">
      {note && (
        <div key={note.id} className="showcase-note">
          <Info size={14} aria-hidden className="showcase-note__icon" />
          <span>{note.text}</span>
        </div>
      )}
    </div>
  )
}
