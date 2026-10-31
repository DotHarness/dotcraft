import { useCallback, useEffect, useState } from 'react'
import type { ReadPresentationResult } from '../../../../../shared/viewer/presentation'
import type { FileViewerTab } from '../../../../../shared/viewer/types'
import { useT } from '../../../../contexts/LocaleContext'
import { useViewerTabStore } from '../../../../stores/viewerTabStore'
import { Skeleton } from '../../../ui/Skeleton'
import { FileChangedNotice } from '../FileChangedNotice'
import type { FilePreviewProps } from '../filePreview'
import { UnavailableNotice } from '../UnavailableNotice'
import { useViewerFileVersion } from '../useViewerFileVersion'
import { PresentationDeck } from './PresentationDeck'
import './presentation.css'
import './slide-content.css'

type PresentationData =
  | { status: 'loading'; result: ReadPresentationResult | null }
  | { status: 'ready'; result: ReadPresentationResult }
  | { status: 'error'; result: null }

function usePresentationData(absolutePath: string, version: number, attempt: number): PresentationData {
  const [data, setData] = useState<PresentationData>({ status: 'loading', result: null })
  useEffect(() => {
    let disposed = false
    setData((current) => ({ status: 'loading', result: current.result }))
    window.api.workspace.viewer
      .readPresentation({ absolutePath })
      .then((result) => {
        if (disposed) return
        setData(result.slides.length ? { status: 'ready', result } : { status: 'error', result: null })
      })
      .catch(() => {
        if (!disposed) setData({ status: 'error', result: null })
      })
    return () => {
      disposed = true
    }
  }, [absolutePath, version, attempt])
  return data
}

export function PresentationViewer({ absolutePath, relativePath, tabId }: FilePreviewProps): JSX.Element {
  const t = useT()
  const { version, stale, refresh } = useViewerFileVersion(absolutePath)
  const [attempt, setAttempt] = useState(0)
  const data = usePresentationData(absolutePath, version, attempt)
  const threadId = useViewerTabStore((state) => state.currentThreadId)
  const reveal = useViewerTabStore((state) => {
    if (!state.currentThreadId) return null
    const tab = state.getThreadState(state.currentThreadId).tabs.find((entry) => entry.id === tabId)
    return (tab as FileViewerTab | undefined)?.pendingReveal ?? null
  })
  const onRevealed = useCallback(
    (revision: number) => {
      if (threadId) useViewerTabStore.getState().clearPendingReveal(threadId, tabId, revision)
    },
    [threadId, tabId]
  )

  if (data.status === 'error') {
    return (
      <UnavailableNotice
        absolutePath={absolutePath}
        relativePath={relativePath}
        reason={{ kind: 'load', onRetry: () => setAttempt((value) => value + 1) }}
      />
    )
  }

  const result = data.result
  if (!result) {
    return (
      <div className="dc-deck__loading" role="status" aria-busy="true" aria-label={t('quickOpen.loading')}>
        <div className="dc-deck__loading-rail">
          {Array.from({ length: 4 }, (_, index) => (
            <Skeleton key={index} width={96} height={54} radius={3} />
          ))}
        </div>
        <Skeleton width="100%" height="auto" radius={4} style={{ aspectRatio: '16 / 9', flex: 1, maxHeight: '100%' }} />
      </div>
    )
  }

  return (
    <div className="dc-deck" aria-busy={data.status === 'loading' || undefined}>
      {stale && <FileChangedNotice onRefresh={refresh} />}
      {result.slideCount > result.slides.length && (
        <p className="dc-deck__notice" role="status">
          {t('viewer.slides.truncated', { shown: result.slides.length.toLocaleString(), total: result.slideCount.toLocaleString() })}
        </p>
      )}
      <PresentationDeck absolutePath={absolutePath} deck={result} reveal={reveal} onRevealed={onRevealed} />
    </div>
  )
}
