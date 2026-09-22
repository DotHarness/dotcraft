import { useMemo, useState } from 'react'
import { diffLines } from 'diff'
import { WrapText } from 'lucide-react'
import { useT } from '../../../contexts/LocaleContext'
import { useFileEditorStore, type FileReview as Review } from '../../../stores/fileEditorStore'
import type { FileDiff } from '../../../types/toolCall'
import { UnifiedDiffBody } from '../diff/UnifiedDiffBody'
import { useDiffModel } from '../diff/useDiffModel'
import { Button } from '../../ui/Button'

export function canPreviewFileReview(text: string): boolean {
  return (
    new TextEncoder().encode(text).byteLength <= 256 * 1024 && text.split('\n', 5001).length <= 5000
  )
}

export function FileReview({
  tabId,
  path,
  review,
  wordWrap
}: {
  tabId: string
  path: string
  review: Review
  wordWrap: boolean
}): JSX.Element {
  const t = useT()
  const [wrap, setWrap] = useState(wordWrap)
  const [pending, setPending] = useState(false)
  const diff = useMemo((): FileDiff | undefined => {
    if (!canPreviewFileReview(review.oldText) || !canPreviewFileReview(review.newText))
      return undefined
    const changes = diffLines(review.oldText, review.newText, { timeout: 1000 })
    if (!changes) return undefined
    const lines = changes.flatMap((change) => {
      const parts = change.value.split('\n')
      if (parts.at(-1) === '') parts.pop()
      return parts.map((content) => ({
        content,
        type: change.added
          ? ('add' as const)
          : change.removed
            ? ('remove' as const)
            : ('context' as const)
      }))
    })
    return {
      filePath: path,
      turnId: tabId,
      turnIds: [tabId],
      status: 'written',
      isNewFile: false,
      additions: lines.filter((line) => line.type === 'add').length,
      deletions: lines.filter((line) => line.type === 'remove').length,
      originalContent: review.oldText,
      currentContent: review.newText,
      diffHunks: [
        {
          oldStart: 1,
          newStart: 1,
          oldLines: lines.filter((line) => line.type !== 'add').length,
          newLines: lines.filter((line) => line.type !== 'remove').length,
          lines
        }
      ]
    }
  }, [path, tabId, review.oldText, review.newText])
  const decide = async (choice: 'edit' | 'reject' | 'accept') => {
    setPending(true)
    try {
      await useFileEditorStore.getState().resolveReview(tabId, choice)
    } finally {
      setPending(false)
    }
  }
  return (
    <section className="dc-file-review" aria-label={t('viewer.conflictTitle')}>
      <div className="dc-file-review__body dc-code">
        {diff ? (
          <ReviewBody diff={diff} wrap={wrap} />
        ) : (
          <p className="dc-file-editor__centered">{t('viewer.reviewTooLarge')}</p>
        )}
      </div>
      <div className="dc-file-review__footer">
        <div className="dc-file-review__actions">
          <Button
            className="dc-file-review__wrap"
            size="toolbar"
            variant="outline"
            aria-label={t(wrap ? 'viewer.disableWordWrap' : 'viewer.enableWordWrap')}
            aria-pressed={wrap}
            onClick={() => setWrap(!wrap)}
          >
            <WrapText size={16} aria-hidden />
          </Button>
          <Button
            size="toolbar"
            variant="outline"
            disabled={pending}
            onClick={() => void decide('edit')}
          >
            {t('viewer.edit')}
          </Button>
          <Button
            className="dc-file-review__reject"
            size="toolbar"
            variant="outline"
            disabled={pending}
            onClick={() => void decide('reject')}
          >
            {t('viewer.reject')}
          </Button>
          <Button
            className="dc-file-review__accept"
            size="toolbar"
            variant="outline"
            disabled={pending}
            onClick={() => void decide('accept')}
          >
            {t('viewer.accept')}
          </Button>
        </div>
      </div>
    </section>
  )
}

function ReviewBody({ diff, wrap }: { diff: FileDiff; wrap: boolean }): JSX.Element {
  const model = useDiffModel(diff)
  return <UnifiedDiffBody diff={diff} model={model} wordWrap={wrap} />
}
