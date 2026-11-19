import { InlineDiff, Spinner } from '@dotcraft/plugin'
import type { JSX, ReactNode } from 'react'
import { useT } from '../i18n'
import styles from './shared.module.css'

export type Tone = 'neutral' | 'warning' | 'success' | 'info' | 'error'

export function Notice({ tone = 'neutral', action, children }: { tone?: Tone; action?: ReactNode; children: ReactNode }): JSX.Element {
  return (
    <div className={styles.notice} data-tone={tone} role={tone === 'error' ? 'alert' : 'status'}>
      <div className={styles.noticeBody}>{children}</div>
      {action ? <div className={styles.noticeAction}>{action}</div> : null}
    </div>
  )
}

export function Badge({ tone = 'neutral', title, children }: { tone?: Tone; title?: string; children: ReactNode }): JSX.Element {
  return <span className={styles.badge} data-tone={tone} title={title}>{children}</span>
}

export type StatusTone = 'neutral' | 'warning' | 'error' | 'info' | 'pending'

export function StatusIndicator({ tone, label }: { tone: StatusTone; label?: string }): JSX.Element {
  return (
    <span className={styles.indicator} role={label ? 'img' : undefined} aria-label={label} aria-hidden={label ? undefined : true}>
      {tone === 'pending' ? <Spinner size={14} /> : <span className={styles.dot} data-tone={tone === 'neutral' ? undefined : tone} />}
    </span>
  )
}

export function StatusLabel({ tone, children }: { tone: StatusTone; children: ReactNode }): JSX.Element {
  return (
    <span className={styles.status}>
      <StatusIndicator tone={tone} />
      <span className={styles.statusText}>{children}</span>
    </span>
  )
}

export function EmptyState({ title, description }: { title: string; description?: string }): JSX.Element {
  return (
    <div className={styles.empty}>
      <div className={styles.emptyTitle}>{title}</div>
      {description ? <div>{description}</div> : null}
    </div>
  )
}

export type FileChange = 'added' | 'updated' | 'deleted'

export interface FileChangeView {
  fileName: string
  change: FileChange
  before?: string | null
  after?: string | null
}

/** The changed span of an update: lines both versions share at the start and end are left out. */
export function changedSpan(before: string, after: string): { line: number; before: string; after: string } {
  const left = before.replace(/\r\n/g, '\n').split('\n')
  const right = after.replace(/\r\n/g, '\n').split('\n')
  let start = 0
  while (start < left.length && start < right.length && left[start] === right[start]) start++
  let end = 0
  while (end < left.length - start && end < right.length - start && left[left.length - 1 - end] === right[right.length - 1 - end]) end++
  return {
    line: start + 1,
    before: left.slice(start, left.length - end).join('\n'),
    after: right.slice(start, right.length - end).join('\n')
  }
}

function FileChangeCard({ file }: { file: FileChangeView }): JSX.Element {
  const t = useT()
  const tone: Tone = file.change === 'added' ? 'success' : file.change === 'deleted' ? 'error' : 'info'
  const span = file.change === 'updated' ? changedSpan(file.before ?? '', file.after ?? '') : null
  return (
    <div className={styles.diffFile}>
      <div className={styles.diffHeader}>
        <span className={styles.diffName} title={file.fileName}>{file.fileName}</span>
        <Badge tone={tone}>{t(`change.${file.change}` as const)}</Badge>
      </div>
      {span
        ? <InlineDiff filePath={file.fileName} line={span.line} before={span.before} after={span.after} />
        : <pre className={styles.diffText}>{(file.change === 'added' ? file.after : file.before) ?? ''}</pre>}
    </div>
  )
}

export function FileChangeList({ files }: { files: readonly FileChangeView[] }): JSX.Element {
  return <div className={styles.diffList}>{files.map((file) => <FileChangeCard key={file.fileName} file={file} />)}</div>
}

export function formatDate(locale: string, ms: number): string {
  if (!ms) return ''
  try {
    return new Intl.DateTimeFormat(locale, { year: 'numeric', month: 'short', day: 'numeric' }).format(new Date(ms))
  } catch {
    return new Date(ms).toLocaleDateString()
  }
}

export function formatDateTime(locale: string, iso: string | null | undefined): string {
  if (!iso) return ''
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return iso
  try {
    return new Intl.DateTimeFormat(locale, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' }).format(date)
  } catch {
    return date.toLocaleString()
  }
}
