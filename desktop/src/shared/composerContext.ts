export type ContextImage = {
  tempPath: string
  dataUrl?: string
  fileName: string
  mimeType: string
}

type ContextBase = { id: string }

export type PastedTextContext = ContextBase & {
  kind: 'pastedText'
  path: string
  fileName: string
  preview: string
  characterCount?: number
}

export type ResponseAnnotationContext = ContextBase & {
  kind: 'responseAnnotation'
  threadId: string
  turnId: string
  itemId: string
  selectedText: string
  comment: string
}

export type DiffAnnotationContext = ContextBase & {
  kind: 'diffAnnotation'
  path: string
  side: 'left' | 'right'
  startLine: number
  endLine: number
  selectedText: string
  comment: string
}

export type PageReferenceContext = ContextBase & {
  kind: 'pageReference'
  url: string
  title: string
  selectionKind: 'text' | 'element' | 'region'
  text: string
  comment: string
  image?: ContextImage
}

export type ComposerContextRecord = PastedTextContext | ResponseAnnotationContext | DiffAnnotationContext | PageReferenceContext

export const PASTED_TEXT_THRESHOLD = 5_000
export const PASTED_TEXT_RESTORE_LIMIT = 25_000

export function canRestorePastedText(context: PastedTextContext): boolean {
  return context.characterCount !== undefined && context.characterCount >= PASTED_TEXT_THRESHOLD
    && context.characterCount <= PASTED_TEXT_RESTORE_LIMIT
}
