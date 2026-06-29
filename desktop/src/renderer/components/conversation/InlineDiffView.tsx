import type { CSSProperties } from 'react'
import type { FileDiff } from '../../types/toolCall'
import { ActionTooltip } from '../ui/ActionTooltip'
import { useUIStore } from '../../stores/uiStore'

interface InlineDiffViewProps {
  diff: FileDiff
  streaming?: boolean
  variant?: 'standalone' | 'embedded'
  headerMode?: 'full' | 'compact'
}

function getFilename(path: string): string {
  return path.split(/[\\/]/).pop() ?? path
}

export function InlineDiffView({
  diff,
  streaming = false,
  variant = 'standalone',
  headerMode = 'full'
}: InlineDiffViewProps): JSX.Element {
  const diffMarkers = useUIStore((s) => s.diffMarkers)
  const signMode = diffMarkers === 'sign'
  const totalAdd = diff.additions
  const totalDel = diff.deletions
  const embedded = variant === 'embedded'
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
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
          padding: '4px 8px',
          background: 'var(--bg-tertiary)',
          borderBottom: '1px solid var(--border-default)',
          color: 'var(--text-secondary)',
          fontSize: '11px'
        }}
      >
        <ActionTooltip label={diff.filePath} wrapperStyle={{ display: 'block', minWidth: 0, overflow: 'hidden', flexShrink: 1 }}>
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
        {headerMode === 'full' && diff.isNewFile && (
          <span style={{ color: 'var(--text-dimmed)', flexShrink: 0 }}>(new file)</span>
        )}
        <span style={{ marginLeft: 'auto', display: 'flex', gap: '6px', flexShrink: 0 }}>
          {/* No streaming spinner or label — the visibly-growing diff below is
              the running cue; the header keeps the +/- counts only. */}
          {totalAdd > 0 && <span style={{ color: 'var(--success)' }}>+{totalAdd}</span>}
          {totalDel > 0 && <span style={{ color: 'var(--error)' }}>-{totalDel}</span>}
        </span>
      </div>

      <div style={{ maxHeight: '360px', overflow: 'auto' }}>
        {diff.diffHunks.map((hunk, hunkIdx) => {
          let oldLineNum = hunk.oldStart
          let newLineNum = hunk.newStart
          return (
            <div key={hunkIdx}>
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
                      minWidth: 'max-content',
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
                      whiteSpace: 'pre'
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

const lineNumberStyle: CSSProperties = {
  width: '40px',
  flexShrink: 0,
  textAlign: 'right',
  paddingRight: '6px',
  color: 'var(--text-dimmed)',
  userSelect: 'none',
  fontSize: '11px'
}
