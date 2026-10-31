import { useCallback, useEffect, useState } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { useViewerTabStore } from '../../../../stores/viewerTabStore'
import type { FileViewerTab } from '../../../../../shared/viewer/types'
import { Skeleton } from '../../../ui/Skeleton'
import { FileChangedNotice } from '../FileChangedNotice'
import type { FilePreviewProps } from '../filePreview'
import { UnavailableNotice } from '../UnavailableNotice'
import { useViewerFileVersion } from '../useViewerFileVersion'
import { SpreadsheetSheetView } from './SpreadsheetSheetView'
import { useSpreadsheetData } from './useSpreadsheetData'
import './spreadsheet.css'

const SKELETON_ROWS = 8

export function SpreadsheetViewer({ absolutePath, relativePath, tabId }: FilePreviewProps): JSX.Element {
  const t = useT()
  const { version, stale, refresh } = useViewerFileVersion(absolutePath)
  const [sheetIndex, setSheetIndex] = useState(0)
  const [attempt, setAttempt] = useState(0)
  const [zoom, setZoom] = useState(1)
  const data = useSpreadsheetData(absolutePath, version, sheetIndex, attempt)
  const threadId = useViewerTabStore((state) => state.currentThreadId)
  const reveal = useViewerTabStore((state) => {
    if (!state.currentThreadId) return null
    const tab = state.getThreadState(state.currentThreadId).tabs.find((entry) => entry.id === tabId)
    return (tab as FileViewerTab | undefined)?.pendingReveal ?? null
  })

  const result = data.result
  useEffect(() => {
    if (!reveal || !result || !threadId) return
    const target = reveal.location.sheet
    if (!target || target === result.sheet.name) return
    const index = result.sheets.findIndex((sheet) => sheet.name === target)
    if (index >= 0) setSheetIndex(index)
    else useViewerTabStore.getState().clearPendingReveal(threadId, tabId, reveal.revision)
  }, [reveal, result, threadId, tabId])

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

  if (!result) {
    return (
      <div className="dc-sheet__loading" role="status" aria-busy="true" aria-label={t('quickOpen.loading')}>
        {Array.from({ length: SKELETON_ROWS }, (_, index) => (
          <Skeleton key={index} height={14} width={index % 3 === 0 ? '72%' : '100%'} />
        ))}
      </div>
    )
  }

  const { sheet } = result
  return (
    <div className="dc-sheet" aria-busy={data.status === 'loading' || undefined}>
      {stale && <FileChangedNotice onRefresh={refresh} />}
      {sheet.truncated && (
        <p className="dc-sheet__notice" role="status">
          {t('viewer.sheet.truncated', {
            rows: sheet.rowCount.toLocaleString(),
            columns: sheet.columnCount.toLocaleString()
          })}
        </p>
      )}
      <SpreadsheetSheetView
        key={`${result.sheetIndex}:${sheet.name}:${version}`}
        absolutePath={absolutePath}
        sheet={sheet}
        sheets={result.sheets}
        sheetIndex={result.sheetIndex}
        onSelectSheet={setSheetIndex}
        zoom={zoom}
        setZoom={setZoom}
        reveal={reveal}
        onRevealed={onRevealed}
      />
    </div>
  )
}
