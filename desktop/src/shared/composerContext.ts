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
  diffHunk?: string
  comment: string
}

export type FileAnnotationTargetKind = 'range' | 'object' | 'slide' | 'element'

export type FileAnnotationLocation = {
  sheet?: string
  range?: string
  rowCount?: number
  columnCount?: number
  slide?: number
  elementId?: string
}

export type FileAnnotationContext = ContextBase & {
  kind: 'fileAnnotation'
  path: string
  target: string
  targetKind: FileAnnotationTargetKind
  location: FileAnnotationLocation
  comment: string
}

export type LineCommentContext = DiffAnnotationContext | FileAnnotationContext

export type PageReferenceContext = ContextBase & {
  kind: 'pageReference'
  url: string
  title: string
  selectionKind: 'text' | 'element' | 'region'
  text: string
  comment: string
  image?: ContextImage
}

export type VisualizationChangesContext = ContextBase & {
  kind: 'visualizationChanges'
  path: string
  title: string
  text: string
}

export type ComposerContextRecord = PastedTextContext | ResponseAnnotationContext | DiffAnnotationContext | FileAnnotationContext | PageReferenceContext | VisualizationChangesContext

export type ThreadReferencesContext = ContextBase & {
  kind: 'threadReferences'
  text: string
}

export const PASTED_TEXT_THRESHOLD = 5_000
export const PASTED_TEXT_RESTORE_LIMIT = 25_000

export function canRestorePastedText(context: PastedTextContext): boolean {
  return context.characterCount !== undefined && context.characterCount >= PASTED_TEXT_THRESHOLD
    && context.characterCount <= PASTED_TEXT_RESTORE_LIMIT
}
