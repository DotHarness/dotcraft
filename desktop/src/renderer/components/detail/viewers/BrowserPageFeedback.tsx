import { useEffect, useRef, useState } from 'react'
import { MessageSquarePlus, X } from 'lucide-react'
import { createPortal } from 'react-dom'
import type { BrowserPageReference } from '../../../../shared/viewer/browserFeedback'
import { ANNOTATION_BUBBLE_PATH } from '../../../../shared/viewer/browserSelectionScript'
import { useLayerPresence } from '../../../contexts/LayerContext'
import { useT } from '../../../contexts/LocaleContext'
import { useComposerContextStore } from '../../../stores/composerContextStore'
import { ContextCommentEditor } from '../../conversation/ContextCommentEditor'
import { ActionTooltip } from '../../ui/ActionTooltip'
import { Button } from '../../ui/Button'
import { IconButton } from '../../ui/IconButton'
import { EDITOR_HEIGHT, EDITOR_WIDTH, placeCommentEditor } from './browserFeedbackPlacement'

function readAccent(): string | undefined {
  return getComputedStyle(document.documentElement).getPropertyValue('--accent').trim() || undefined
}

export function BrowserPageFeedback({
  tabId,
  threadId,
  body,
  disabled,
  run,
}: {
  tabId: string
  threadId: string | null
  body: React.RefObject<HTMLDivElement | null>
  disabled: boolean
  run: (operation: () => Promise<unknown>) => void
}): JSX.Element {
  const t = useT()
  const [selecting, setSelecting] = useState(false)
  const [reference, setReference] = useState<BrowserPageReference | null>(null)
  const api = window.api.workspace.viewer.browser
  const threadIdRef = useRef(threadId)
  threadIdRef.current = threadId
  useLayerPresence(reference !== null)
  useEffect(() => {
    const cancelSelection = api.cancelSelection
    setReference(null)
    setSelecting(false)
    const unsubscribe = api.onFeedback((event) => {
      if (
        event.type === 'selection' &&
        event.reference.tabId === tabId &&
        event.reference.threadId === threadIdRef.current
      ) {
        setReference(event.reference)
        setSelecting(false)
      }
    })
    return () => {
      unsubscribe()
      void cancelSelection({ tabId }).catch(() => {})
    }
  }, [tabId])
  useEffect(() => {
    if (!selecting) return
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return
      event.preventDefault()
      setSelecting(false)
      run(() => api.cancelSelection({ tabId }))
    }
    window.addEventListener('keydown', onKeyDown, true)
    return () => window.removeEventListener('keydown', onKeyDown, true)
  }, [selecting, tabId])
  function select() {
    if (selecting) {
      setSelecting(false)
      run(() => api.cancelSelection({ tabId }))
      return
    }
    setReference(null)
    setSelecting(true)
    run(async () => {
      try {
        await api.select({ tabId, kind: 'element', accent: readAccent() })
      } finally {
        setSelecting(false)
      }
    })
  }
  function save(comment: string) {
    if (!reference || !threadId) return
    const selected = reference
    run(async () => {
      const image = selected.imageDataUrl
        ? await window.api.workspace.saveImageToTemp({
            dataUrl: selected.imageDataUrl,
            fileName: 'page-selection.png',
          })
        : null
      useComposerContextStore.getState().addContext(threadId, {
        kind: 'pageReference',
        id: reference.id,
        url: reference.url,
        title: reference.title,
        selectionKind: reference.kind,
        text: reference.text,
        comment,
        ...(reference.imageDataUrl
          ? {
              image: {
                tempPath: image!.path,
                dataUrl: reference.imageDataUrl,
                fileName: 'page-selection.png',
                mimeType: 'image/png',
              },
            }
          : {}),
      })
      setReference(null)
    })
  }
  const bounds = reference?.bounds
  const width = body.current?.clientWidth ?? 0
  const height = body.current?.clientHeight ?? 0
  const placement = placeCommentEditor(
    bounds ?? { x: 8, y: 8, width: 0, height: 0 },
    { width, height },
    { width: EDITOR_WIDTH, height: EDITOR_HEIGHT },
  )
  return (
    <>
      <ActionTooltip label={t('viewer.browser.annotate')} placement="bottom">
        <Button
          variant="ghost"
          size="toolbar"
          className="dc-browser-annotate"
          aria-pressed={selecting}
          aria-label={t(selecting ? 'viewer.browser.annotating' : 'viewer.browser.annotate')}
          disabled={disabled || !threadId}
          onClick={select}
          iconLeft={<MessageSquarePlus size={16} aria-hidden />}
        >
          {t('viewer.browser.annotating')}
        </Button>
      </ActionTooltip>
      {reference &&
        body.current &&
        createPortal(
          <div className="dc-browser-page-feedback" style={{ left: body.current.getBoundingClientRect().left, top: body.current.getBoundingClientRect().top, width, height }}>
            {reference.previewDataUrl && (
              <img
                className="dc-browser-page-feedback__preview"
                src={reference.previewDataUrl}
                alt=""
              />
            )}
            {bounds && (
              <div
                className="dc-browser-page-feedback__selection"
                style={{
                  left: bounds.x,
                  top: bounds.y,
                  width: bounds.width,
                  height: bounds.height,
                }}
              >
                <span className="dc-browser-page-feedback__marker" aria-hidden="true">
                  <svg viewBox="0 0 26 25">
                    <path d={ANNOTATION_BUBBLE_PATH} fill="var(--accent)" stroke="var(--bg-primary)" strokeWidth={1.65} />
                  </svg>
                </span>
              </div>
            )}
            <div
              className="dc-browser-page-feedback__editor"
              style={{ left: placement.left, top: placement.top }}
            >
              <ContextCommentEditor
                key={reference.id}
                onSave={save}
                onCancel={() => setReference(null)}
              />
            </div>
            <div className="dc-browser-page-feedback__close">
              <IconButton
                size={24}
                radius={6}
                icon={<X size={14} />}
                label={t('common.cancel')}
                tooltipLabel={t('common.cancel')}
                onClick={() => setReference(null)}
              />
            </div>
          </div>,
          document.body,
        )}
    </>
  )
}
