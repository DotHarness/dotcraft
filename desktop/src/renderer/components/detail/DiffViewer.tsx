import { useMemo, useRef, type CSSProperties } from 'react'
import hljs from 'highlight.js/lib/core'
import bash from 'highlight.js/lib/languages/bash'
import csharp from 'highlight.js/lib/languages/csharp'
import css from 'highlight.js/lib/languages/css'
import go from 'highlight.js/lib/languages/go'
import javascript from 'highlight.js/lib/languages/javascript'
import json from 'highlight.js/lib/languages/json'
import markdown from 'highlight.js/lib/languages/markdown'
import python from 'highlight.js/lib/languages/python'
import rust from 'highlight.js/lib/languages/rust'
import typescript from 'highlight.js/lib/languages/typescript'
import xml from 'highlight.js/lib/languages/xml'
import { useT } from '../../contexts/LocaleContext'
import type { DiffLine, FileDiff } from '../../types/toolCall'
import { detectLanguage } from './viewers/languageDetect'

hljs.registerLanguage('bash', bash)
hljs.registerLanguage('csharp', csharp)
hljs.registerLanguage('css', css)
hljs.registerLanguage('go', go)
hljs.registerLanguage('html', xml)
hljs.registerLanguage('javascript', javascript)
hljs.registerLanguage('json', json)
hljs.registerLanguage('markdown', markdown)
hljs.registerLanguage('python', python)
hljs.registerLanguage('rust', rust)
hljs.registerLanguage('shell', bash)
hljs.registerLanguage('typescript', typescript)
hljs.registerLanguage('xml', xml)

export type DiffDisplayMode = 'inline' | 'split'

interface DiffViewerProps {
  diff: FileDiff
  workspacePath: string
  mode?: DiffDisplayMode
}

interface NumberedLine {
  num: string
  content: string
  type: DiffLine['type'] | 'blank'
}

interface SplitRow {
  left: NumberedLine
  right: NumberedLine
}

type SplitRenderRow =
  | { kind: 'divider'; count: number }
  | { kind: 'line'; left: NumberedLine; right: NumberedLine }

export function DiffViewer({ diff, workspacePath, mode = 'inline' }: DiffViewerProps): JSX.Element {
  const relativePath = toRelativePath(diff.filePath, workspacePath)
  const language = detectLanguage(diff.filePath)

  return (
    <div
      data-testid="diff-viewer"
      data-mode={mode}
      style={{
        fontFamily: 'var(--font-mono)',
        fontSize: '12px',
        lineHeight: '1.55',
        overflow: 'hidden'
      }}
    >
      {mode === 'split'
        ? <SplitDiffBody diff={diff} relativePath={relativePath} language={language} />
        : <UnifiedDiffBody diff={diff} relativePath={relativePath} language={language} />}
    </div>
  )
}

export function UnifiedDiffBody({
  diff,
  relativePath,
  language = 'plaintext'
}: {
  diff: FileDiff
  relativePath?: string
  language?: string
}): JSX.Element {
  if (diff.diffHunks.length === 0) {
    return <EmptyDiffMessage />
  }

  let previousOldEnd = 1
  let previousNewEnd = 1

  return (
    <div data-testid="unified-diff-body" style={{ overflowX: 'auto' }}>
      {diff.diffHunks.map((hunk, hunkIdx) => {
        let oldLineNum = hunk.oldStart
        let newLineNum = hunk.newStart
        const unchanged = unchangedBeforeHunk(hunk.oldStart, hunk.newStart, previousOldEnd, previousNewEnd)
        previousOldEnd = hunk.oldStart + hunk.oldLines
        previousNewEnd = hunk.newStart + hunk.newLines

        return (
          <div key={hunkIdx} style={{ minWidth: 'max-content' }}>
            {unchanged > 0 && <UnchangedDivider count={unchanged} />}
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
                    background: diffLineBackground(line.type),
                    whiteSpace: 'pre'
                  }}
                >
                  <span style={lineNumberStyle}>{oldNum}</span>
                  <span style={lineNumberStyle}>{newNum}</span>
                  <span style={markerStyle(line.type)}>
                    {line.type === 'add' ? '+' : line.type === 'remove' ? '-' : ' '}
                  </span>
                  <span
                    title={relativePath}
                    style={{
                      padding: '0 10px 0 4px',
                      color: line.type === 'remove' ? 'var(--text-secondary)' : 'var(--text-primary)'
                    }}
                  >
                    <HighlightedLine content={line.content} language={language} />
                  </span>
                </div>
              )
            })}
          </div>
        )
      })}
    </div>
  )
}

export function SplitDiffBody({
  diff,
  relativePath,
  language = 'plaintext'
}: {
  diff: FileDiff
  relativePath?: string
  language?: string
}): JSX.Element {
  const leftPaneRef = useRef<HTMLDivElement>(null)
  const rightPaneRef = useRef<HTMLDivElement>(null)
  const rows = useMemo(() => buildSplitRenderRows(diff), [diff])

  if (diff.diffHunks.length === 0) {
    return <EmptyDiffMessage />
  }

  function syncScroll(source: 'left' | 'right'): void {
    const sourcePane = source === 'left' ? leftPaneRef.current : rightPaneRef.current
    const targetPane = source === 'left' ? rightPaneRef.current : leftPaneRef.current
    if (!sourcePane || !targetPane) return
    if (targetPane.scrollLeft === sourcePane.scrollLeft) return
    targetPane.scrollLeft = sourcePane.scrollLeft
  }

  return (
    <div
      data-testid="split-diff-body"
      style={{
        display: 'grid',
        gridTemplateColumns: 'minmax(0, 1fr) minmax(0, 1fr)',
        minWidth: 0,
        overflow: 'hidden'
      }}
    >
      <div
        ref={leftPaneRef}
        data-testid="split-left-pane"
        onScroll={() => syncScroll('left')}
        style={{
          ...splitPaneStyle,
          borderRight: '1px solid var(--border-default)'
        }}
      >
        <SplitPaneRows rows={rows} side="left" title={relativePath} language={language} />
      </div>
      <div
        ref={rightPaneRef}
        data-testid="split-right-pane"
        onScroll={() => syncScroll('right')}
        style={splitPaneStyle}
      >
        <SplitPaneRows rows={rows} side="right" title={relativePath} language={language} />
      </div>
    </div>
  )
}

function SplitPaneRows({
  rows,
  side,
  title,
  language
}: {
  rows: SplitRenderRow[]
  side: 'left' | 'right'
  title?: string
  language: string
}): JSX.Element {
  return (
    <div style={{ minWidth: 'max-content' }}>
      {rows.map((row, index) => {
        if (row.kind === 'divider') {
          return <UnchangedDivider key={`divider-${index}`} count={row.count} />
        }
        return (
          <SplitCell
            key={`line-${index}`}
            line={side === 'left' ? row.left : row.right}
            title={title}
            language={language}
          />
        )
      })}
    </div>
  )
}

function SplitCell({
  line,
  title,
  language
}: {
  line: NumberedLine
  title?: string
  language: string
}): JSX.Element {
  const isBlank = line.type === 'blank'
  return (
    <div
      style={{
        display: 'flex',
        width: 'max-content',
        minWidth: '100%',
        background: isBlank ? 'var(--bg-primary)' : diffLineBackground(line.type),
        whiteSpace: 'pre'
      }}
    >
      <span style={lineNumberStyle}>{line.num}</span>
      <span style={markerStyle(line.type)}>{line.type === 'add' ? '+' : line.type === 'remove' ? '-' : ' '}</span>
      <span
        title={title}
        style={{
          minWidth: 0,
          padding: '0 10px 0 4px',
          color: isBlank ? 'transparent' : line.type === 'remove' ? 'var(--text-secondary)' : 'var(--text-primary)'
        }}
      >
        {isBlank ? ' ' : <HighlightedLine content={line.content} language={language} />}
      </span>
    </div>
  )
}

function buildSplitRenderRows(diff: FileDiff): SplitRenderRow[] {
  const rows: SplitRenderRow[] = []
  let previousOldEnd = 1
  let previousNewEnd = 1

  for (const hunk of diff.diffHunks) {
    const unchanged = unchangedBeforeHunk(hunk.oldStart, hunk.newStart, previousOldEnd, previousNewEnd)
    previousOldEnd = hunk.oldStart + hunk.oldLines
    previousNewEnd = hunk.newStart + hunk.newLines
    if (unchanged > 0) rows.push({ kind: 'divider', count: unchanged })
    for (const row of buildSplitRows(hunk.lines, hunk.oldStart, hunk.newStart)) {
      rows.push({ kind: 'line', ...row })
    }
  }

  return rows
}

function buildSplitRows(lines: DiffLine[], oldStart: number, newStart: number): SplitRow[] {
  const rows: SplitRow[] = []
  let oldLineNum = oldStart
  let newLineNum = newStart
  let index = 0

  while (index < lines.length) {
    const line = lines[index]
    if (!line) break

    if (line.type === 'context') {
      rows.push({
        left: { num: String(oldLineNum++), content: line.content, type: 'context' },
        right: { num: String(newLineNum++), content: line.content, type: 'context' }
      })
      index++
      continue
    }

    if (line.type === 'remove') {
      const removes: DiffLine[] = []
      while (lines[index]?.type === 'remove') {
        removes.push(lines[index]!)
        index++
      }
      const adds: DiffLine[] = []
      while (lines[index]?.type === 'add') {
        adds.push(lines[index]!)
        index++
      }
      const count = Math.max(removes.length, adds.length)
      for (let i = 0; i < count; i++) {
        const removed = removes[i]
        const added = adds[i]
        rows.push({
          left: removed
            ? { num: String(oldLineNum++), content: removed.content, type: 'remove' }
            : blankLine(),
          right: added
            ? { num: String(newLineNum++), content: added.content, type: 'add' }
            : blankLine()
        })
      }
      continue
    }

    rows.push({
      left: blankLine(),
      right: { num: String(newLineNum++), content: line.content, type: 'add' }
    })
    index++
  }

  return rows
}

function HighlightedLine({ content, language }: { content: string; language: string }): JSX.Element {
  const html = highlightLine(content, language)
  if (html === null) return <>{content}</>
  return (
    <span
      data-testid="highlighted-diff-line"
      dangerouslySetInnerHTML={{ __html: html }}
    />
  )
}

function highlightLine(content: string, language: string): string | null {
  if (language === 'plaintext') return null
  if (!hljs.getLanguage(language)) return null
  try {
    const result = hljs.highlight(content, {
      language,
      ignoreIllegals: true
    })
    return result.value
  } catch {
    return null
  }
}

function blankLine(): NumberedLine {
  return { num: '', content: '', type: 'blank' }
}

function EmptyDiffMessage(): JSX.Element {
  const t = useT()
  return (
    <div style={{ padding: '10px 12px', color: 'var(--text-dimmed)', fontSize: '12px' }}>
      {t('diffViewer.noChanges')}
    </div>
  )
}

function UnchangedDivider({ count }: { count: number }): JSX.Element {
  const t = useT()
  return (
    <div
      style={{
        padding: '4px 8px',
        background: 'var(--bg-secondary)',
        color: 'var(--text-dimmed)',
        fontSize: '11px',
        userSelect: 'none'
      }}
    >
      {t('diffViewer.unchangedLines', { count })}
    </div>
  )
}

function unchangedBeforeHunk(
  oldStart: number,
  newStart: number,
  previousOldEnd: number,
  previousNewEnd: number
): number {
  return Math.max(0, oldStart - previousOldEnd, newStart - previousNewEnd)
}

function diffLineBackground(type: NumberedLine['type']): string {
  if (type === 'add') return 'var(--diff-add-bg)'
  if (type === 'remove') return 'var(--diff-remove-bg)'
  return 'transparent'
}

function markerStyle(type: NumberedLine['type']): CSSProperties {
  return {
    width: '16px',
    flexShrink: 0,
    textAlign: 'center',
    color: type === 'add'
      ? 'var(--success)'
      : type === 'remove'
        ? 'var(--error)'
        : 'var(--text-dimmed)',
    userSelect: 'none'
  }
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

const splitPaneStyle: CSSProperties = {
  minWidth: 0,
  overflowX: 'auto',
  overflowY: 'hidden',
  scrollbarWidth: 'thin'
}

function toRelativePath(filePath: string, workspacePath: string): string {
  if (!workspacePath) return filePath
  const ws = workspacePath.replace(/\\/g, '/').replace(/\/$/, '')
  const fp = filePath.replace(/\\/g, '/')
  if (fp.startsWith(ws + '/')) return fp.slice(ws.length + 1)
  return filePath
}
