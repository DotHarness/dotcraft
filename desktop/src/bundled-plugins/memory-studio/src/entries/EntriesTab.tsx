import { Button, CatalogSection } from '@dotcraft/plugin'
import { FileText } from 'lucide-react'
import type { JSX } from 'react'
import { useLocale, useT } from '../i18n'
import { INDEX_MAX_BYTES, INDEX_MAX_LINES, type IndexUsage } from '../data/memoryIndex'
import { isStale, matchesSearch, type MemoryEntry, type MemoryStore, type StoreContents } from '../data/stores'
import { EmptyState, formatDate, StatusIndicator } from '../shared/ui'
import styles from './entries.module.css'

export type AgeFilter = 'all' | 'stale' | 'recent'

export interface EntryFilters {
  query: string
  store: string
  type: string
  age: AgeFilter
}

export const ALL = ':all'
export const UNTYPED = ':untyped'

export function filterEntries(contents: StoreContents, filters: EntryFilters, declaredTypes: ReadonlySet<string>, now: number): MemoryEntry[] {
  return contents.entries.filter((entry) => {
    if (!matchesSearch(entry, filters.query)) return false
    if (filters.type === UNTYPED && declaredTypes.has(entry.type)) return false
    if (filters.type !== ALL && filters.type !== UNTYPED && entry.type !== filters.type) return false
    if (filters.age === 'stale' && !isStale(entry, now)) return false
    if (filters.age === 'recent' && isStale(entry, now)) return false
    return true
  })
}

export function useStoreLabel(): (store: MemoryStore) => string {
  const t = useT()
  return (store) => store.kind === 'workspace' ? t('store.workspace') : store.displayName
}

function EntryRow({ entry, declared, now, onOpen }: {
  entry: MemoryEntry
  declared: boolean
  now: number
  onOpen(): void
}): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const stale = isStale(entry, now)
  return (
    <li>
      <button type="button" className={styles.row} onClick={onOpen}>
        <span className={styles.mark} aria-hidden="true"><FileText size={18} strokeWidth={1.75} /></span>
        <span className={styles.text}>
          <span className={styles.titleLine}>
            <span className={styles.name}>{entry.name}</span>
            <span className={styles.typeLabel} title={declared ? undefined : t('entries.undeclaredType')}>
              {entry.type && declared ? entry.type : t('entries.untyped')}
            </span>
          </span>
          <span className={styles.description}>{entry.description || entry.fileName}</span>
        </span>
        <span className={styles.meta} title={stale ? `${t('entries.stale')} · ${t('filter.stale')}` : undefined}>
          <span className={styles.indicatorSlot}>{stale ? <StatusIndicator tone="warning" label={t('entries.stale')} /> : null}</span>
          <span className={styles.date}>{formatDate(locale, entry.modifiedAtMs)}</span>
        </span>
      </button>
    </li>
  )
}

function IndexLoadNotice({ usage }: { usage: IndexUsage }): JSX.Element | null {
  const t = useT()
  const locale = useLocale()
  if (usage.load === 'ok') return null
  const number = (value: number): string => value.toLocaleString(locale)
  const values = { lines: number(usage.lines), maxLines: number(INDEX_MAX_LINES), bytes: number(usage.bytes), maxBytes: number(INDEX_MAX_BYTES) }
  return (
    <p className={styles.indexLoad} data-tone={usage.load === 'over' ? 'warning' : undefined}>
      {usage.load === 'over' ? t('index.over', values) : t('index.near', values)}
    </p>
  )
}

export function EntriesTab({ stores, filters, declaredTypes, onOpen, onNew }: {
  stores: readonly StoreContents[]
  filters: EntryFilters
  declaredTypes: ReadonlySet<string>
  onOpen(store: MemoryStore, entry: MemoryEntry): void
  onNew(store: MemoryStore): void
}): JSX.Element {
  const t = useT()
  const label = useStoreLabel()
  const now = Date.now()
  const shown = stores.filter((contents) => filters.store === ALL || contents.store.key === filters.store)
  const filtering = filters.query.trim() !== '' || filters.type !== ALL || filters.age !== 'all'

  return (
    <>
      {shown.map((contents) => {
        const entries = filterEntries(contents, filters, declaredTypes, now)
        if (filtering && entries.length === 0) return null
        const title = contents.store.writable ? label(contents.store) : t('store.readOnly', { name: label(contents.store) })
        return (
          <CatalogSection key={contents.store.key} title={title}>
            <IndexLoadNotice usage={contents.usage} />
            {entries.length > 0 ? (
              <ul className={styles.rows}>
                {entries.map((entry) => (
                  <EntryRow
                    key={entry.fileName}
                    entry={entry}
                    declared={declaredTypes.has(entry.type)}
                    now={now}
                    onOpen={() => onOpen(contents.store, entry)}
                  />
                ))}
              </ul>
            ) : (
              <div className={styles.actions}>
                <EmptyState title={t('entries.emptyStore')} />
                {contents.store.writable ? <Button variant="secondary" size="sm" onClick={() => onNew(contents.store)}>{t('entries.new')}</Button> : null}
              </div>
            )}
          </CatalogSection>
        )
      })}
      {filtering && shown.every((contents) => filterEntries(contents, filters, declaredTypes, now).length === 0)
        ? <EmptyState title={t('entries.noMatches')} description={t('entries.noMatchesHint')} />
        : null}
    </>
  )
}
