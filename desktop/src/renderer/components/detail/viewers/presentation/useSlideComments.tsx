import { useMemo, useState, type ReactNode } from 'react'
import type { FileAnnotationContext } from '../../../../../shared/composerContext'
import { slideTarget, type PresentationSlide } from '../../../../../shared/viewer/presentation'
import { PreviewCommentAction, PreviewCommentCard, PreviewCommentMarker, useFileAnnotations, type PreviewBox, type VisibleSpan } from '../PreviewComments'

export interface SlideSelection {
  slide: number
  elementId: string | null
}

export function annotationSelection(annotation: FileAnnotationContext): SlideSelection | null {
  const slide = annotation.location.slide
  if (!slide) return null
  if (annotation.targetKind === 'element' && annotation.location.elementId) return { slide, elementId: annotation.location.elementId }
  if (annotation.targetKind === 'slide') return { slide, elementId: null }
  return null
}

export function commentCounts(annotations: FileAnnotationContext[]): Map<number, number> {
  const counts = new Map<number, number>()
  for (const annotation of annotations) {
    const target = annotationSelection(annotation)
    if (target) counts.set(target.slide, (counts.get(target.slide) ?? 0) + 1)
  }
  return counts
}

export function selectionAnnotation(selection: SlideSelection, labels: Map<string, string>) {
  const label = selection.elementId ? labels.get(selection.elementId) : undefined
  if (selection.elementId && !label) return null
  return selection.elementId
    ? { target: slideTarget(selection.slide, label), targetKind: 'element' as const, location: { slide: selection.slide, elementId: selection.elementId } }
    : { target: slideTarget(selection.slide), targetKind: 'slide' as const, location: { slide: selection.slide } }
}

interface SlideCommentsOptions {
  absolutePath: string
  slide: PresentationSlide | undefined
  labels: Map<string, string>
  selection: SlideSelection | null
  showAction: boolean
  boxFor: (selection: SlideSelection) => PreviewBox
  visible: VisibleSpan
}

interface SlideComments {
  counts: Map<number, number>
  overlays: ReactNode[]
  openCard: (id: string | null) => void
  findSaved: (selection: SlideSelection) => FileAnnotationContext | undefined
}

export function useSlideComments({ absolutePath, slide, labels, selection, showAction, boxFor, visible }: SlideCommentsOptions): SlideComments {
  const { threadId, saved, drafts, start } = useFileAnnotations(absolutePath)
  const [openId, setOpenId] = useState<string | null>(null)
  const counts = useMemo(() => commentCounts(saved), [saved])
  const number = slide?.number ?? 0
  const findSaved = (target: SlideSelection) =>
    saved.find((entry) => {
      const place = annotationSelection(entry)
      return place?.slide === target.slide && place.elementId === target.elementId
    })
  const result = { counts, overlays: [] as ReactNode[], openCard: setOpenId, findSaved }
  if (!threadId || !slide) return result

  const placed = (annotation: FileAnnotationContext) => {
    const target = annotationSelection(annotation)
    if (target?.slide !== number) return null
    return boxFor(target)
  }
  const selected = selection?.slide === number ? selectionAnnotation(selection, labels) : null
  const cardOpenForSelection =
    selected !== null &&
    (drafts.some((entry) => entry.target === selected.target) || saved.some((entry) => entry.id === openId && entry.target === selected.target))

  for (const annotation of saved) {
    const box = placed(annotation)
    if (!box) continue
    result.overlays.push(
      annotation.id === openId ? (
        <PreviewCommentCard key={`card:${annotation.id}`} box={box} visible={visible} threadId={threadId} annotation={annotation} saved onClose={() => setOpenId(null)} />
      ) : (
        <PreviewCommentMarker key={`marker:${annotation.id}`} box={box} annotation={annotation} onOpen={() => setOpenId(annotation.id)} />
      )
    )
  }
  for (const annotation of drafts) {
    const box = placed(annotation)
    if (!box) continue
    result.overlays.push(
      <PreviewCommentCard key={`draft:${annotation.id}`} box={box} visible={visible} threadId={threadId} annotation={annotation} saved={false} onClose={() => undefined} />
    )
  }
  if (showAction && selection && selected && !cardOpenForSelection) {
    result.overlays.push(
      <PreviewCommentAction
        key="action"
        box={boxFor(selection)}
        onComment={() => {
          const id = start(selected)
          if (id && saved.some((entry) => entry.id === id)) setOpenId(id)
        }}
      />
    )
  }
  return result
}
