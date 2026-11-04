import type { DragEvent } from 'react'

export function droppedWebLink(dataTransfer: Pick<DataTransfer, 'types' | 'getData'>): string | null {
  if (Array.from(dataTransfer.types).includes('Files')) return null
  const url = dataTransfer.getData('text/uri-list')
    .split(/\r?\n/)
    .map((line) => line.trim())
    .find((line) => line && !line.startsWith('#'))
  return url && /^https?:\/\//i.test(url) ? url : null
}

export function insertDroppedLink(event: DragEvent<HTMLElement>, url: string): void {
  const editor = event.currentTarget.querySelector<HTMLElement>('[contenteditable="true"]')
  if (!editor) return
  editor.focus()
  const range = document.caretRangeFromPoint(event.clientX, event.clientY)
  const selection = window.getSelection()
  if (range && selection && editor.contains(range.startContainer)) {
    selection.removeAllRanges()
    selection.addRange(range)
  }
  document.execCommand('insertText', false, url)
}
