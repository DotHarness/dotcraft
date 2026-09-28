import { useEffect, useMemo, useState, type JSX } from 'react'
import type { ImportCompletedNotification, ImportOutcome } from '@dotcraft/sdk/contracts'
import { useT } from '../../../contexts/LocaleContext'
import { Button } from '../../ui/Button'
import { SettingsGroup, SettingsRow } from '../SettingsGroup'
import { ImportAttention } from './ImportAttention'
import { ImportHistoryBatch } from './ImportHistoryBatch'
import { latestBatchIds } from './importOutcomes'
import styles from './ImportHistory.module.css'

export function ImportHistory({ revision }: { revision: number }): JSX.Element {
  const t = useT()
  const [batches, setBatches] = useState<ImportCompletedNotification[] | null>(null)
  const [attention, setAttention] = useState<ImportOutcome[]>([])
  const [failed, setFailed] = useState(false)
  const [reload, setReload] = useState(0)
  const [showAll, setShowAll] = useState(false)
  const [toggled, setToggled] = useState<ReadonlyMap<string, boolean>>(new Map())

  useEffect(() => {
    let disposed = false
    void window.api.appServer.sendRequest('import/history/list', {}).then(result => {
      if (disposed) return
      setBatches(result.imports)
      setAttention(result.attention)
      setFailed(false)
    }).catch(() => {
      if (!disposed) setFailed(true)
    })
    return () => { disposed = true }
  }, [revision, reload])

  const latest = useMemo(() => latestBatchIds(batches ?? []), [batches])
  const visible = (batches ?? []).filter(batch => showAll || latest.has(batch.importId))
  const isExpanded = (importId: string): boolean => toggled.get(importId) ?? latest.has(importId)

  return (
    <>
      <ImportAttention outcomes={attention} />
      {failed && batches == null ? (
        <SettingsGroup title={t('settings.import.setup.history')}>
          <SettingsRow
            label={<span className={styles.error}>{t('settings.import.history.loadFailed')}</span>}
            control={<Button onClick={() => setReload(value => value + 1)}>{t('common.retry')}</Button>}
          />
        </SettingsGroup>
      ) : batches != null && batches.length > 0 && (
        <SettingsGroup title={t('settings.import.setup.history')} framed={false}>
          <div className={styles.batches}>
            {visible.map(batch => (
              <ImportHistoryBatch
                key={batch.importId}
                batch={batch}
                expanded={isExpanded(batch.importId)}
                onToggle={() => setToggled(previous => new Map(previous).set(batch.importId, !isExpanded(batch.importId)))}
              />
            ))}
            {visible.length < batches.length && (
              <div className={styles.more}>
                <Button size="toolbar" onClick={() => setShowAll(true)}>{t('settings.import.history.viewMore')}</Button>
              </div>
            )}
          </div>
        </SettingsGroup>
      )}
    </>
  )
}
