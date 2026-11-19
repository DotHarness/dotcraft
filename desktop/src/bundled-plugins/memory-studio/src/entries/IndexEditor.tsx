import { Button, Textarea } from '@dotcraft/plugin'
import { useState, type JSX } from 'react'
import { saveIndex } from '../data/entrySave'
import type { MemoryFs } from '../data/memoryFs'
import { analyzeIndex, INDEX_FILE, INDEX_MAX_BYTES, INDEX_MAX_LINES } from '../data/memoryIndex'
import { errorMessage } from '../data/rpc'
import type { StoreContents } from '../data/stores'
import { useLocale, useT } from '../i18n'
import { memoryStudioHost } from '../runtime'
import { Notice } from '../shared/ui'
import styles from './entries.module.css'

export function IndexEditor({ fs, contents, onClose }: {
  fs: MemoryFs
  contents: StoreContents
  onClose(changed: boolean): void
}): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const [expected, setExpected] = useState(contents.indexText)
  const [text, setText] = useState(contents.indexText ?? '')
  const [current, setCurrent] = useState<string | null | undefined>(undefined)
  const [busy, setBusy] = useState(false)
  const health = analyzeIndex(text, contents.entries.map((entry) => entry.fileName))

  const save = async (base: string | null): Promise<void> => {
    const host = memoryStudioHost()
    setBusy(true)
    try {
      const outcome = await saveIndex(fs, contents.store.path, base, text.replace(/\s+$/, '') + '\n')
      if (outcome.kind === 'conflict') {
        setCurrent(outcome.current)
        return
      }
      host.ui.showToast({ tone: 'success', message: t('index.saved') })
      onClose(true)
    } catch (error) {
      host.ui.showToast({ tone: 'error', message: t('index.saveFailed'), description: errorMessage(error) })
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className={styles.editor}>
      {current !== undefined ? (
        <Notice tone="warning">
          <div>{t('index.conflict')}</div>
          <pre className={styles.current}>{current ?? ''}</pre>
          <div className={styles.actions}>
            <Button variant="secondary" size="sm" onClick={() => { setText(current ?? ''); setExpected(current); setCurrent(undefined) }}>{t('editor.useCurrent')}</Button>
            <Button variant="secondary" size="sm" disabled={busy} onClick={() => { setExpected(current); setCurrent(undefined); void save(current) }}>{t('editor.saveMine')}</Button>
          </div>
        </Notice>
      ) : null}
      <div className={styles.field}>
        <Textarea aria-label={INDEX_FILE} rows={18} mono value={text} onChange={(event) => setText(event.target.value)} />
        <span className={styles.hint}>
          {t('index.usage', {
            lines: health.lines.toLocaleString(locale),
            maxLines: INDEX_MAX_LINES.toLocaleString(locale),
            bytes: health.bytes.toLocaleString(locale),
            maxBytes: INDEX_MAX_BYTES.toLocaleString(locale)
          })}
          {' · '}
          {t('index.format')}
        </span>
      </div>
      <div className={styles.actions}>
        <span className={styles.spacer} />
        <Button variant="ghost" onClick={() => onClose(false)}>{t('editor.cancel')}</Button>
        <Button variant="primary" loading={busy} onClick={() => void save(expected)}>{t('editor.save')}</Button>
      </div>
    </div>
  )
}
