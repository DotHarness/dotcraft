// Searching is the window-wide find overlay's job, not this component's: one query
// walks the file, open diffs, and the conversation together.
import { useCallback, useMemo, useRef, type CSSProperties } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import type { FileNavigationHint } from '../../../../shared/viewer/types'
import {
  VirtualizedLines,
  type VirtualizedLinesHandle
} from '../../code/VirtualizedLines'
import { LineNumber, LineSpans } from '../../code/CodeSpans'
import { ROW_MEASURE_ATTRIBUTE } from '../../code/useLineMetrics'
import codeCss from '../../code/code.module.css'
import {
  fileCacheKey,
  languageFromPath,
  normalizeNewlines,
  splitLines,
  useFileHighlight
} from '../../../highlight'
import { useFindSurface } from '../../../find/useFindSurface'
import type { FindSegment } from '../../../find/types'
import { useFileText } from './useFileText'
import { useNavigationLine } from './useNavigationLine'

interface TextViewerProps {
  absolutePath: string
  /** Undefined is treated as enabled, matching the historical default. */
  wordWrap?: boolean
  navigationHint?: FileNavigationHint
}

const LINE_HEIGHT_RATIO = 1.55
const CODE_FONT_FALLBACK = 12

export function TextViewer({
  absolutePath,
  wordWrap = true,
  navigationHint
}: TextViewerProps): JSX.Element {
  const t = useT()
  const state = useFileText(absolutePath)
  const listRef = useRef<VirtualizedLinesHandle>(null)
  const containerRef = useRef<HTMLDivElement>(null)

  const lines = useMemo(
    () => (state.status === 'ok' ? splitLines(normalizeNewlines(state.text)) : []),
    [state.status, state.text]
  )

  const request = useMemo(() => {
    if (state.status !== 'ok') return undefined
    const lang = languageFromPath(absolutePath)
    return {
      cacheKey: fileCacheKey(absolutePath, lang, state.text),
      name: absolutePath,
      lang,
      contents: state.text
    }
  }, [absolutePath, state.status, state.text])

  const highlighted = useFileHighlight(request)

  const getSegments = useCallback((): FindSegment[] => lines.map((text, index) => ({
    key: String(index),
    rowIndex: index,
    lineId: String(index + 1),
    text
  })), [lines])

  useFindSurface({
    id: state.status === 'ok' ? `file:${absolutePath}` : undefined,
    domain: 'file',
    priority: 30,
    getSegments,
    getContainer: () => containerRef.current,
    reveal: (match) => {
      if (match.rowIndex !== undefined) listRef.current?.scrollToIndex(match.rowIndex)
    },
    contentKey: request?.cacheKey
  })

  useNavigationLine({
    hint: navigationHint,
    lineCount: lines.length,
    ready: state.status === 'ok' && state.absolutePath === absolutePath,
    scrollToIndex: (index) => listRef.current?.scrollToIndex(index)
  })

  if (state.status === 'loading') {
    return <CenteredMessage>{t('quickOpen.loading')}</CenteredMessage>
  }

  if (state.status === 'error') {
    return <CenteredMessage>{t('viewer.readFailed')} — {state.error}</CenteredMessage>
  }

  return (
    <div ref={containerRef} className="dc-code" style={frameStyle}>
      {state.truncated && (
        <div role="status" style={truncatedStyle}>
          {t('viewer.truncatedNotice')}
        </div>
      )}
      <VirtualizedLines
        ref={listRef}
        testId="text-viewer-lines"
        className={`${codeCss.viewport} ${wordWrap ? codeCss.wrapped : ''}`}
        count={lines.length}
        estimatedLineHeight={estimatedLineHeight()}
        variableHeight={wordWrap}
        renderRange={({ start, end }) => {
          const rows: JSX.Element[] = []
          for (let index = start; index < end; index++) {
            rows.push(
              <div key={index} className={codeCss.row} {...{ [ROW_MEASURE_ATTRIBUTE]: index }}>
                <LineNumber value={index + 1} />
                <span className={codeCss.content} data-line={index + 1}>
                  <LineSpans line={highlighted?.lines[index]} text={lines[index] ?? ''} />
                </span>
              </div>
            )
          }
          return rows
        }}
      />
    </div>
  )
}

/** Read from the code type tokens so the estimate tracks the user's code font size. */
function estimatedLineHeight(): number {
  if (typeof window === 'undefined') return CODE_FONT_FALLBACK * LINE_HEIGHT_RATIO
  const style = window.getComputedStyle(document.documentElement)
  const size = Number.parseFloat(style.getPropertyValue('--text-code-size'))
  return (Number.isFinite(size) ? size : CODE_FONT_FALLBACK) * LINE_HEIGHT_RATIO
}

function CenteredMessage({ children }: { children: React.ReactNode }): JSX.Element {
  return <div style={centeredStyle}>{children}</div>
}

const frameStyle: CSSProperties = {
  display: 'flex',
  height: '100%',
  flexDirection: 'column'
}

const truncatedStyle: CSSProperties = {
  flexShrink: 0,
  padding: '4px 12px',
  borderBottom: '1px solid var(--border-default)',
  color: 'var(--warning)',
  backgroundColor: 'var(--warning-bg)',
  fontSize: '12px'
}

const centeredStyle: CSSProperties = {
  display: 'flex',
  height: '100%',
  alignItems: 'center',
  justifyContent: 'center',
  padding: '24px',
  color: 'var(--text-secondary)',
  fontSize: '13px',
  textAlign: 'center'
}
