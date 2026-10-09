import { useCallback, type RefObject } from 'react'

type SelectionRange = { start: number; end: number }

interface SelectionReferencesOptions {
  editorRef: RefObject<HTMLDivElement | null>
  disabled?: boolean
  getSelectionRange: () => SelectionRange | null
  linearize: (root: HTMLElement) => string
  replaceRange: (
    kind: 'file' | 'thread',
    value: string,
    range: SelectionRange,
    title?: string,
    leadingSpace?: boolean
  ) => void
}

export function useRichInputSelectionReferences({
  editorRef,
  disabled,
  getSelectionRange,
  linearize,
  replaceRange
}: SelectionReferencesOptions) {
  const insertAtSelection = useCallback(
    (kind: 'file' | 'thread', value: string, title?: string): void => {
      const editor = editorRef.current
      if (!editor || disabled) return
      const text = linearize(editor)
      const saved = getSelectionRange() ?? { start: text.length, end: text.length }
      const start = Math.max(0, Math.min(saved.start, text.length))
      const end = Math.max(start, Math.min(saved.end, text.length))
      const leadingSpace = kind === 'file' && start > 0 && !/\s/.test(text[start - 1]!)
      editor.focus()
      replaceRange(kind, value, { start, end }, title, leadingSpace)
    },
    [disabled, editorRef, getSelectionRange, linearize, replaceRange]
  )

  const insertFileTagAtSelection = useCallback(
    (path: string): void => insertAtSelection('file', path),
    [insertAtSelection]
  )
  const insertThreadTagAtSelection = useCallback(
    (threadId: string, title: string): void => insertAtSelection('thread', threadId, title),
    [insertAtSelection]
  )
  return { insertFileTagAtSelection, insertThreadTagAtSelection }
}
