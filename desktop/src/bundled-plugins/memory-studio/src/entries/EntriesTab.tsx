import { CatalogSection, IconButton, Menu, WorkspaceName } from '@dotcraft/plugin'
import { MoreHorizontal } from 'lucide-react'
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

export function useStoreLabel(workspaceName: string | null = null): (store: MemoryStore) => string {
  const t = useT()
  return (store) => {
    if (store.kind === 'workspace') return workspaceName ? t('store.workspaceNamed', { name: workspaceName }) : t('store.workspace')
    return store.kind === 'personal' ? t('store.personal') : store.displayName
  }
}

function WorkspaceTitle(): JSX.Element {
  const t = useT()
  const template = t('store.workspaceNamed')
  const at = template.indexOf('{{name}}')
  if (at < 0) return <>{t('store.workspace')}</>
  return <>{template.slice(0, at)}<WorkspaceName />{template.slice(at + '{{name}}'.length)}</>
}

function EntryCard({ entry, declared, now, onOpen }: {
  entry: MemoryEntry
  declared: boolean
  now: number
  onOpen(): void
}): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const stale = isStale(entry, now)
  return (
    <li className={styles.cardItem}>
      <button type="button" className={styles.card} onClick={onOpen}>
        <span className={styles.cardHead}>
          <span className={styles.typeChip} data-untyped={entry.type && declared ? undefined : true} title={declared ? undefined : t('entries.undeclaredType')}>
            {entry.type && declared ? entry.type : t('entries.untyped')}
          </span>
          <span className={styles.cardDate} data-stale={stale ? true : undefined} title={stale ? t('entries.stale') : undefined}>
            {stale ? <StatusIndicator tone="warning" label={t('entries.stale')} /> : null}
            {formatDate(locale, entry.modifiedAtMs)}
          </span>
        </span>
        <span className={styles.cardName}>{entry.name}</span>
        <span className={styles.cardDescription}>{entry.description || entry.fileName}</span>
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

export function EntriesTab({ stores, filters, declaredTypes, onOpen, clearing, onClear }: {
  stores: readonly StoreContents[]
  filters: EntryFilters
  declaredTypes: ReadonlySet<string>
  onOpen(store: MemoryStore, entry: MemoryEntry): void
  clearing: boolean
  onClear(store: MemoryStore): void
}): JSX.Element {
  const t = useT()
  const label = useStoreLabel()
  const now = Date.now()
  const shown = stores.filter((contents) => filters.store === ALL || contents.store.key === filters.store)
  const filtering = filters.query.trim() !== '' || filters.type !== ALL || filters.age !== 'all'
  const allEmpty = !filtering && filters.store === ALL && shown.every((contents) => contents.entries.length === 0)

  if (allEmpty) {
    return <EmptyState title={t('entries.empty')} />
  }

  return (
    <>
      {shown.map((contents) => {
        const entries = filterEntries(contents, filters, declaredTypes, now)
        if (filtering && entries.length === 0) return null
        const title = !contents.store.writable
          ? t('store.readOnly', { name: label(contents.store) })
          : contents.store.kind === 'workspace' ? <WorkspaceTitle /> : label(contents.store)
        return (
          <CatalogSection
            key={contents.store.key}
            title={title}
            actions={(contents.store.kind === 'personal' || contents.store.kind === 'workspace') && contents.store.writable && contents.entries.length > 0 ? (
              <Menu items={[{ label: t(contents.store.kind === 'personal' ? 'clear.personal' : 'clear.workspace'), danger: true, disabled: clearing, onSelect: () => onClear(contents.store) }]}>
                <IconButton icon={<MoreHorizontal size={15} />} label={t('editor.moreActions')} size={28} />
              </Menu>
            ) : undefined}
          >
            <IndexLoadNotice usage={contents.usage} />
            {entries.length > 0 ? (
              <ul className={styles.cards}>
                {entries.map((entry) => (
                  <EntryCard
                    key={entry.fileName}
                    entry={entry}
                    declared={declaredTypes.has(entry.type)}
                    now={now}
                    onOpen={() => onOpen(contents.store, entry)}
                  />
                ))}
              </ul>
            ) : (
              <EmptyState title={t('entries.empty')} />
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
