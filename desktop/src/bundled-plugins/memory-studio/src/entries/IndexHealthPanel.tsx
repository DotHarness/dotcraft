import { Button } from '@dotcraft/plugin'
import { useState, type JSX } from 'react'
import { saveIndex } from '../data/entrySave'
import type { MemoryFs } from '../data/memoryFs'
import {
  INDEX_MAX_BYTES,
  INDEX_MAX_LINES,
  removeIndexLine,
  upsertIndexEntry
} from '../data/memoryIndex'
import { errorMessage } from '../data/rpc'
import type { StoreContents } from '../data/stores'
import { useLocale, useT } from '../i18n'
import { memoryStudioHost } from '../runtime'
import styles from './entries.module.css'

export function IndexHealthPanel({ fs, contents, onEditIndex, onChanged }: {
  fs: MemoryFs
  contents: StoreContents
  onEditIndex(): void
  onChanged(): void
}): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const [busy, setBusy] = useState(false)
  const { store, health, indexText } = contents
  const number = (value: number): string => value.toLocaleString(locale)

  const apply = async (next: string | null): Promise<void> => {
    const host = memoryStudioHost()
    if (next == null) {
      host.ui.showToast({ tone: 'warning', message: t('index.changed') })
      onChanged()
      return
    }
    setBusy(true)
    try {
      const outcome = await saveIndex(fs, store.path, indexText, next)
      if (outcome.kind === 'conflict') host.ui.showToast({ tone: 'warning', message: t('index.changed') })
      onChanged()
    } catch (error) {
      host.ui.showToast({ tone: 'error', message: t('index.fixFailed'), description: errorMessage(error) })
    } finally {
      setBusy(false)
    }
  }

  const addLine = (fileName: string): Promise<void> => {
    const entry = contents.entries.find((candidate) => candidate.fileName === fileName)
    return apply(upsertIndexEntry(indexText ?? '', { file: fileName, description: entry?.description ?? '' }))
  }

  const fix = (label: string, run: () => void): JSX.Element | null =>
    store.writable ? <Button variant="ghost" size="sm" disabled={busy} onClick={run}>{label}</Button> : null

  const contentLines = health.nonPointerLines.length + health.longPointerLines.length
  return (
    <>
      <div className={styles.storeMeta}>
        <span>{t('index.usage', {
          lines: number(health.lines),
          maxLines: number(INDEX_MAX_LINES),
          bytes: number(health.bytes),
          maxBytes: number(INDEX_MAX_BYTES)
        })}</span>
        {store.writable ? <Button variant="ghost" size="sm" onClick={onEditIndex}>{t('index.edit')}</Button> : null}
      </div>
      <ul className={styles.issues}>
        {health.overLines || health.overBytes ? (
          <li className={styles.issue} data-tone="warning">
            <span className={styles.issueText}>{t('index.overLimit')}</span>
            {fix(t('index.edit'), onEditIndex)}
          </li>
        ) : null}
        {contentLines > 0 ? (
          <li className={styles.issue} data-tone="warning">
            <span className={styles.issueText}>
              {health.nonPointerLines.length > 0 ? (health.nonPointerLines.length === 1 ? t('index.nonPointer.one') : t('index.nonPointer', { count: health.nonPointerLines.length })) : null}
              {health.nonPointerLines.length > 0 && health.longPointerLines.length > 0 ? ' · ' : null}
              {health.longPointerLines.length > 0 ? (health.longPointerLines.length === 1 ? t('index.longLines.one') : t('index.longLines', { count: health.longPointerLines.length })) : null}
            </span>
            {fix(t('index.edit'), onEditIndex)}
          </li>
        ) : null}
        {health.missing.map((fileName) => (
          <li key={`missing:${fileName}`} className={styles.issue}>
            <span className={styles.issueText} title={fileName}>{t('index.missing', { file: fileName })}</span>
            {fix(t('index.addLine'), () => void addLine(fileName))}
          </li>
        ))}
        {health.dangling.map((line) => (
          <li key={`dangling:${line.lineNumber}`} className={styles.issue}>
            <span className={styles.issueText} title={line.text}>{t('index.dangling', { line: line.lineNumber })}</span>
            {fix(t('index.removeLine'), () => void apply(removeIndexLine(indexText ?? '', line.lineNumber, line.text)))}
          </li>
        ))}
      </ul>
    </>
  )
}
