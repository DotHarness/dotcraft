import { ContextPreview } from './ContextPreview'
import { useEffect, useRef, useState, type RefObject } from 'react'
import { FileText } from 'lucide-react'
import {
  canRestorePastedText,
  type PastedTextContext,
} from '../../../shared/composerContext'
import { useComposerContextStore } from '../../stores/composerContextStore'
import { useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'
import { AttachmentRemoveButton } from './AttachmentRemoveButton'
import { ComposerFeedbackAttachments } from './ComposerFeedbackAttachments'
import type { RichInputAreaHandle } from './RichInputArea'
import type { usePastedText } from './usePastedText'

interface Props {
  threadId: string
  editorRef: RefObject<RichInputAreaHandle | null>
  pastedText: ReturnType<typeof usePastedText>
}

export function ComposerContextAttachments({
  threadId,
  editorRef,
  pastedText,
}: Props): JSX.Element {
  const t = useT()
  const contexts = useComposerContextStore((state) =>
    state.getContexts(threadId),
  )
  const [selected, setSelected] = useState<string | null>(null)
  const [restoring, setRestoring] = useState<string | null>(null)
  const active = contexts.find(
    (entry): entry is PastedTextContext =>
      entry.id === selected && entry.kind === 'pastedText',
  )
  const owner = useRef(threadId)
  owner.current = threadId
  useEffect(() => {
    setSelected(null)
    setRestoring(null)
  }, [threadId])
  function remove(id: string) {
    useComposerContextStore.getState().removeContext(threadId, id)
  }
  async function restore(context: PastedTextContext) {
    if (restoring) return
    const editor = editorRef.current
    setRestoring(context.id)
    try {
      const { text } = await window.api.workspace.restorePastedText({
        path: context.path,
      })
      if (owner.current !== threadId || editorRef.current !== editor || !editor)
        return
      if (
        !useComposerContextStore
          .getState()
          .getContexts(threadId)
          .some((entry) => entry.id === context.id)
      )
        return
      const segments = editor.getSegments()
      editor.setContent({
        segments: [
          ...segments,
          { type: 'text', value: `${editor.getText() ? '\n\n' : ''}${text}` },
        ],
      })
      remove(context.id)
      setSelected(null)
      requestAnimationFrame(() => {
        if (editorRef.current === editor) editor.focus()
      })
    } catch (error) {
      addToast(String(error instanceof Error ? error.message : error), 'error')
    } finally {
      setRestoring(null)
    }
  }
  return (
    <>
      <ComposerFeedbackAttachments
        threadId={threadId}
        contexts={contexts.filter((context) => context.kind !== 'pastedText')}
      />
      {contexts
        .filter((context) => context.kind === 'pastedText')
        .map((context) => (
          <div className="dc-context-attachment" key={context.id}>
            <span className="dc-context-attachment__icon">
              <FileText size={20} />
            </span>
            <div className="dc-context-attachment__copy">
              <button
                type="button"
                className="dc-context-attachment__title"
                onClick={() => setSelected(context.id)}
                aria-label={t('composer.context.previewPaste')}
              >
                {contextTitle(context) || t('composer.context.pastedText')}
              </button>
              {canRestorePastedText(context) ? (
                <button
                  type="button"
                  className="dc-context-attachment__restore"
                  disabled={restoring === context.id}
                  onClick={() => {
                    void restore(context)
                  }}
                >
                  {t('composer.context.restore')}
                </button>
              ) : (
                <span className="dc-context-attachment__subtitle">
                  {t('composer.context.pastedText')}
                </span>
              )}
            </div>
            <AttachmentRemoveButton label={t('composer.context.remove')} onRemove={() => remove(context.id)} />
          </div>
        ))}
      {pastedText.pendingPastes.map((paste) => (
        <div className="dc-context-attachment" key={paste.id}>
          <span className="dc-context-attachment__icon">
            <FileText size={20} />
          </span>
          <div className="dc-context-attachment__copy">
            <span className="dc-context-attachment__title">
              {paste.text.replace(/\s+/g, ' ').slice(0, 80) ||
                t('composer.context.pastedText')}
            </span>
            {paste.status === 'failed' ? (
              <button
                className="dc-context-attachment__restore"
                type="button"
                onClick={() => pastedText.retry(paste)}
              >
                {t('common.retry')}
              </button>
            ) : (
              <span className="dc-context-attachment__subtitle" role="status">
                {t('composer.context.adding')}
              </span>
            )}
          </div>
          <AttachmentRemoveButton label={t('composer.context.remove')} onRemove={() => pastedText.discard(paste.id)} />
        </div>
      ))}
      {active && (
        <ContextPreview
          key={active.id}
          context={active}
          onClose={() => setSelected(null)}
          onRemove={() => {
            remove(active.id)
            setSelected(null)
          }}
          onRestore={
            canRestorePastedText(active)
              ? () => {
                  void restore(active)
                }
              : undefined
          }
          restoring={restoring === active.id}
        />
      )}
    </>
  )
}

function contextTitle(context: PastedTextContext) {
  return context.preview.replace(/\s+/g, ' ').slice(0, 80)
}
