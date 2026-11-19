import { Button } from '@dotcraft/plugin'
import type { DreamsGetResult, DreamsRunState } from '@dotcraft/sdk/contracts'
import { Moon } from 'lucide-react'
import { useEffect, useState, type JSX } from 'react'
import { errorCode, errorMessage } from '../data/rpc'
import type { MemoryStore } from '../data/stores'
import { ALL, useStoreLabel } from '../entries/EntriesTab'
import { useLocale, useT, type MessageKey } from '../i18n'
import { memoryStudioHost } from '../runtime'
import { EmptyState, FileChangeList, formatDateTime, Notice, StatusIndicator, StatusLabel, type FileChange, type StatusTone } from '../shared/ui'
import { changeCount, isPendingReview, matchesRunSearch, matchesRunStatus, orderRuns, type RunStatusFilter } from './dreamRuns'
import styles from './consolidation.module.css'

export interface RunFilters {
  query: string
  store: string
  status: RunStatusFilter
}

function runStatus(run: DreamsRunState): { key: MessageKey; tone: StatusTone } {
  if (isPendingReview(run)) return { key: 'dreams.status.pending', tone: 'warning' }
  if (run.reviewStatus === 'applied') return { key: 'dreams.status.applied', tone: 'neutral' }
  if (run.reviewStatus === 'discarded') return { key: 'dreams.status.discarded', tone: 'neutral' }
  switch (run.status) {
    case 'running': return { key: 'dreams.status.running', tone: 'pending' }
    case 'failed': return { key: 'dreams.status.failed', tone: 'error' }
    case 'skipped': return { key: 'dreams.status.skipped', tone: 'neutral' }
    case 'canceled': return { key: 'dreams.status.canceled', tone: 'neutral' }
    default: return { key: 'dreams.status.succeeded', tone: 'neutral' }
  }
}

function runStore(run: DreamsRunState, stores: readonly MemoryStore[]): MemoryStore | undefined {
  return run.scope ? stores.find((candidate) => candidate.scope === run.scope) : stores.find((candidate) => candidate.kind === 'workspace')
}

function useRunStoreName(stores: readonly MemoryStore[]): (run: DreamsRunState) => string {
  const t = useT()
  const label = useStoreLabel()
  return (run) => {
    const store = runStore(run, stores)
    return store ? label(store) : run.scope ?? t('store.workspace')
  }
}

export function useRunTitle(stores: readonly MemoryStore[]): (run: DreamsRunState) => string {
  const locale = useLocale()
  const storeName = useRunStoreName(stores)
  return (run) => `${storeName(run)} · ${formatDateTime(locale, run.startedAt)}`
}

export function RunReview({ runId, onChanged }: { runId: string; onChanged(): void }): JSX.Element {
  const t = useT()
  const [detail, setDetail] = useState<DreamsGetResult | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState<'apply' | 'discard' | null>(null)
  const [revision, setRevision] = useState(0)

  useEffect(() => {
    let cancelled = false
    memoryStudioHost().appServer.request('dreams/get', { runId }).then(
      (result) => { if (!cancelled) setDetail(result) },
      (failure: unknown) => { if (!cancelled) setError(errorMessage(failure)) }
    )
    return () => { cancelled = true }
  }, [runId, revision])

  const act = async (action: 'apply' | 'discard'): Promise<void> => {
    const host = memoryStudioHost()
    if (action === 'discard') {
      const confirmed = await host.ui.confirm({ title: t('dreams.discardTitle'), message: t('dreams.discardMessage'), confirmLabel: t('dreams.discard'), danger: true })
      if (!confirmed) return
    }
    setBusy(action)
    setError(null)
    try {
      await host.appServer.request(action === 'apply' ? 'dreams/apply' : 'dreams/discard', { runId })
      host.ui.showToast({ tone: 'success', message: t(action === 'apply' ? 'dreams.applied' : 'dreams.discarded') })
      onChanged()
      setRevision((value) => value + 1)
    } catch (failure) {
      setError(errorCode(failure) === 'storeChanged' ? t('dreams.storeChanged') : errorMessage(failure))
    } finally {
      setBusy(null)
    }
  }

  const run = detail?.run
  const status = run ? runStatus(run) : null
  const files = (detail?.files ?? []).map((file) => ({
    fileName: file.name ?? '',
    change: (file.change ?? 'updated') as FileChange,
    before: file.baseline,
    after: file.candidate
  }))
  return (
    <div className={styles.review}>
      {error ? <Notice tone="error">{error}</Notice> : null}
      {status ? <StatusLabel tone={status.tone}>{t(status.key)}</StatusLabel> : null}
      {run?.summary ? <div className={styles.summary}>{run.summary}</div> : null}
      {run?.changes?.indexChanged ? <Notice>{t('dreams.indexChanged')}</Notice> : null}
      {detail && files.length === 0 ? <EmptyState title={t('dreams.noChanges')} /> : <FileChangeList files={files} />}
      {run && isPendingReview(run) ? (
        <div className={styles.actions}>
          <span className={styles.spacer} />
          <Button variant="danger" disabled={busy != null} loading={busy === 'discard'} onClick={() => void act('discard')}>{t('dreams.discard')}</Button>
          <Button variant="primary" disabled={busy != null} loading={busy === 'apply'} onClick={() => void act('apply')}>{t('dreams.apply')}</Button>
        </div>
      ) : null}
    </div>
  )
}

export function ConsolidationTab({ runs, stores, filters, onOpen }: {
  runs: readonly DreamsRunState[]
  stores: readonly MemoryStore[]
  filters: RunFilters
  onOpen(runId: string): void
}): JSX.Element {
  const t = useT()
  const title = useRunTitle(stores)
  const storeName = useRunStoreName(stores)
  const shown = orderRuns(runs).filter((run) => {
    if (filters.store !== ALL && runStore(run, stores)?.key !== filters.store) return false
    return matchesRunStatus(run, filters.status) && matchesRunSearch(run, storeName(run), filters.query)
  })

  return (
    <>
      {runs.length === 0 ? <EmptyState title={t('dreams.empty')} description={t('dreams.emptyHint')} /> : shown.length === 0 ? (
        <EmptyState title={t('dreams.noMatches')} description={t('entries.noMatchesHint')} />
      ) : (
        <ul className={styles.rows}>
          {shown.map((run) => {
            const status = runStatus(run)
            const count = changeCount(run)
            return (
              <li key={run.id}>
                <button type="button" className={styles.row} onClick={() => run.id && onOpen(run.id)}>
                  <span className={styles.mark} aria-hidden="true"><Moon size={18} strokeWidth={1.75} /></span>
                  <span className={styles.text}>
                    <span className={styles.title}>{title(run)}</span>
                    <span className={styles.detail}>{run.summary || run.message || (count === 1 ? t('dreams.changes.one') : t('dreams.changes', { count }))}</span>
                  </span>
                  {status.tone === 'neutral' ? null : (
                    <span className={styles.meta} title={t(status.key)}><StatusIndicator tone={status.tone} label={t(status.key)} /></span>
                  )}
                </button>
              </li>
            )
          })}
        </ul>
      )}
    </>
  )
}
