import { useEffect, useRef, useState, type CSSProperties } from 'react'
import { Check, Copy } from 'lucide-react'
import type { FileDiff } from '../../types/toolCall'
import { ActionTooltip } from '../ui/ActionTooltip'
import { IconButton } from '../ui/IconButton'
import { useUIStore } from '../../stores/uiStore'
import { translate, type AppLocale } from '../../../shared/locales'
import { addToast } from '../../stores/toastStore'
import { FileDiffStats } from './FileDiffStats'

interface InlineDiffViewProps {
  diff: FileDiff
  streaming?: boolean
  variant?: 'standalone' | 'embedded'
  headerMode?: 'full' | 'compact'
  presentation?: 'default' | 'conversation-file-tool' | 'body-only'
  resolvedPath?: string
  locale?: AppLocale
}

function getFilename(path: string): string {
  return path.split(/[\\/]/).pop() ?? path
}

export function InlineDiffView({
  diff,
  streaming = false,
  variant = 'standalone',
  headerMode = 'full',
  presentation = 'default',
  resolvedPath = diff.filePath,
  locale = 'en'
}: InlineDiffViewProps): JSX.Element {
  const diffMarkers = useUIStore((s) => s.diffMarkers)
  const signMode = diffMarkers === 'sign'
  const totalAdd = diff.additions
  const totalDel = diff.deletions
  const embedded = variant === 'embedded'
  const conversationFileTool = presentation === 'conversation-file-tool'
  const bodyOnly = presentation === 'body-only'
  const wrapBodyLines = conversationFileTool || bodyOnly
  const displayPath = headerMode === 'compact' ? getFilename(diff.filePath) : diff.filePath

  return (
    <div
      className="selectable"
      data-testid="inline-diff-view"
      style={{
        fontFamily: 'var(--font-mono)',
        fontSize: 'var(--text-code-size)',
        lineHeight: '1.5',
        borderRadius: embedded ? 0 : '4px',
        overflow: 'hidden',
        borderWidth: embedded ? 0 : '1px',
        borderStyle: embedded ? 'none' : 'solid',
        borderColor: embedded ? 'transparent' : 'var(--border-default)'
      }}
    >
      {!bodyOnly && (
        <FileResultHeader
          filePath={diff.filePath}
          resolvedPath={resolvedPath}
          displayPath={displayPath}
          additions={totalAdd}
          deletions={totalDel}
          meta={headerMode === 'full' && diff.isNewFile ? '(new file)' : undefined}
          copyPath={conversationFileTool}
          inlineStats={conversationFileTool}
          locale={locale}
        />
      )}

      <div
        data-testid="inline-diff-body"
        style={{
          maxHeight: '360px',
          overflowY: 'auto',
          overflowX: wrapBodyLines ? 'hidden' : 'auto'
        }}
      >
        {diff.diffHunks.map((hunk, hunkIdx) => {
          let oldLineNum = hunk.oldStart
          let newLineNum = hunk.newStart
          return (
            <div key={hunkIdx}>
              {!conversationFileTool && !bodyOnly && (
                <div
                  style={{
                    padding: '2px 8px',
                    background: 'var(--bg-secondary)',
                    color: 'var(--text-dimmed)',
                    fontSize: '11px',
                    userSelect: 'none',
                    minWidth: 'max-content'
                  }}
                >
                  @@ -{hunk.oldStart},{hunk.oldLines} +{hunk.newStart},{hunk.newLines} @@
                </div>
              )}
              {hunk.lines.map((line, lineIdx) => {
                const oldNum = line.type === 'add' ? '' : String(oldLineNum)
                const newNum = line.type === 'remove' ? '' : String(newLineNum)
                if (line.type !== 'add') oldLineNum++
                if (line.type !== 'remove') newLineNum++
                return (
                  <div
                    key={lineIdx}
                    style={{
                      display: 'flex',
                      alignItems: 'flex-start',
                      minWidth: wrapBodyLines ? 0 : 'max-content',
                      background:
                        line.type === 'add'
                          ? 'var(--diff-add-bg)'
                          : line.type === 'remove'
                            ? 'var(--diff-remove-bg)'
                            : 'transparent',
                      // Color mode marks the change with a left accent bar; +/- mode uses the gutter sign.
                      boxShadow: signMode
                        ? undefined
                        : line.type === 'add'
                          ? 'inset 2px 0 0 var(--success)'
                          : line.type === 'remove'
                            ? 'inset 2px 0 0 var(--error)'
                            : undefined,
                      whiteSpace: wrapBodyLines ? 'pre-wrap' : 'pre'
                    }}
                  >
                    <span style={lineNumberStyle}>{oldNum}</span>
                    <span style={lineNumberStyle}>{newNum}</span>
                    {signMode && (
                      <span
                        style={{
                          width: '16px',
                          flexShrink: 0,
                          textAlign: 'center',
                          color:
                            line.type === 'add'
                              ? 'var(--success)'
                              : line.type === 'remove'
                                ? 'var(--error)'
                                : 'var(--text-dimmed)',
                          userSelect: 'none'
                        }}
                      >
                        {line.type === 'add' ? '+' : line.type === 'remove' ? '-' : ' '}
                      </span>
                    )}
                    <span
                      style={{
                        padding: '0 8px',
                        minWidth: 0,
                        flex: '1 1 auto',
                        whiteSpace: wrapBodyLines ? 'pre-wrap' : 'pre',
                        overflowWrap: wrapBodyLines ? 'anywhere' : undefined,
                        color:
                          line.type === 'add'
                            ? 'var(--text-primary)'
                            : 'var(--text-secondary)'
                      }}
                    >
                      {line.content}
                      {streaming && line.type === 'add' && hunkIdx === diff.diffHunks.length - 1 && lineIdx === hunk.lines.length - 1 && (
                        <span style={{ color: 'var(--accent)', marginLeft: '2px' }}>|</span>
                      )}
                    </span>
                  </div>
                )
              })}
            </div>
          )
        })}
        {diff.diffHunks.length === 0 && !streaming && (
          <div style={{ padding: '8px', color: 'var(--text-dimmed)' }}>
            No changes
          </div>
        )}
      </div>
    </div>
  )
}

interface FileResultHeaderProps {
  filePath: string
  resolvedPath?: string
  displayPath?: string
  additions?: number
  deletions?: number
  meta?: string
  copyPath?: boolean
  inlineStats?: boolean
  locale?: AppLocale
}

export function FileResultHeader({
  filePath,
  resolvedPath = filePath,
  displayPath = getFilename(filePath),
  additions = 0,
  deletions = 0,
  meta,
  copyPath = false,
  inlineStats = false,
  locale = 'en'
}: FileResultHeaderProps): JSX.Element {
  const [copied, setCopied] = useState(false)
  const resetTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  useEffect(() => () => {
    if (resetTimerRef.current != null) clearTimeout(resetTimerRef.current)
  }, [])

  async function handleCopyPath(): Promise<void> {
    try {
      await navigator.clipboard.writeText(resolvedPath)
      setCopied(true)
      addToast(translate(locale, 'toast.copied'), 'success', 2000)
      if (resetTimerRef.current != null) clearTimeout(resetTimerRef.current)
      resetTimerRef.current = setTimeout(() => {
        setCopied(false)
        resetTimerRef.current = null
      }, 1500)
    } catch {
      // Ignore clipboard failures silently.
    }
  }

  const stats = (
    <FileDiffStats
      additions={additions}
      deletions={deletions}
      testId="file-result-diff-stats"
      style={{ marginLeft: inlineStats ? 0 : 'auto' }}
    />
  )

  return (
    <div
      data-testid="file-result-header"
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: '6px',
        padding: '4px 8px',
        background: 'var(--bg-tertiary)',
        borderBottom: '1px solid var(--border-default)',
        color: 'var(--text-secondary)',
        fontSize: '11px'
      }}
    >
      <ActionTooltip
        label={resolvedPath}
        wrapperStyle={{ display: 'block', minWidth: 0, overflow: 'hidden', flexShrink: 1 }}
      >
        <span
          style={{
            color: 'var(--text-primary)',
            fontWeight: 500,
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            whiteSpace: 'nowrap',
            display: 'block'
          }}
        >
          {displayPath}
        </span>
      </ActionTooltip>
      {meta && <span style={{ color: 'var(--text-dimmed)', flexShrink: 0 }}>{meta}</span>}
      {stats}
      {copyPath && (
          <IconButton
            size={24}
            data-testid="file-path-copy"
            label={translate(locale, 'viewer.copyPath')}
            tooltipLabel={translate(locale, 'viewer.copyPath')}
            tooltipPlacement="top"
            tooltipWrapperStyle={{ marginLeft: 'auto', flexShrink: 0 }}
            onClick={() => { void handleCopyPath() }}
            style={{
              margin: '-2px -4px -2px 0',
              borderRadius: '6px',
              color: copied ? 'var(--success)' : 'var(--text-dimmed)'
            }}
            icon={copied ? <Check size={13} aria-hidden /> : <Copy size={13} aria-hidden />}
          />
      )}
    </div>
  )
}

const lineNumberStyle: CSSProperties = {
  width: '40px',
  flexShrink: 0,
  textAlign: 'right',
  paddingRight: '6px',
  color: 'var(--text-dimmed)',
  userSelect: 'none',
  fontSize: '11px'
}
