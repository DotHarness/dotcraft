import { Button, CatalogPage, CatalogToolbarButton, Skeleton, type CatalogPageProps, type DesktopPluginViewProps } from '@dotcraft/plugin'
import { CircleDot, Clock, Database, Plus, RefreshCw, Settings, Tag } from 'lucide-react'
import { useEffect, useMemo, useState, type JSX } from 'react'
import { ConsolidationTab, RunReview, useRunTitle, type RunFilters } from './consolidation/ConsolidationTab'
import type { RunStatusFilter } from './consolidation/dreamRuns'
import { isWithin } from './data/hostPath'
import { memoryName } from './data/memoryFile'
import { INDEX_FILE } from './data/memoryIndex'
import type { MemoryStore } from './data/stores'
import { ALL, EntriesTab, UNTYPED, useStoreLabel, type AgeFilter, type EntryFilters } from './entries/EntriesTab'
import { EntryEditor, type EntryTarget } from './entries/EntryEditor'
import { IndexEditor } from './entries/IndexEditor'
import { useDreamRuns, useMemoryFs, usePolicyState, useStores, type Loaded } from './hooks/useMemoryData'
import { useT } from './i18n'
import { PolicySettingEditor } from './policy/PolicySettingEditor'
import { PolicyTab, type PolicyEditorSetting } from './policy/PolicyTab'
import { Notice } from './shared/ui'
import { takeOpenRequest, usePendingOpenRequest, type OpenRequest } from './state/openRequests'
import styles from './MemoryView.module.css'

type Tab = 'entries' | 'dreams'

type Page =
  | { kind: 'policy' }
  | { kind: 'policySetting'; setting: PolicyEditorSetting }
  | { kind: 'entry'; target: EntryTarget }
  | { kind: 'index'; storeKey: string }
  | { kind: 'run'; runId: string }

/** The store a Summary row names: the attached store by id, otherwise the store that contains the file. */
export function resolveRequestStore(stores: readonly MemoryStore[], request: Extract<OpenRequest, { kind: 'entry' }>): MemoryStore | undefined {
  if (request.storeId) return stores.find((store) => store.kind === 'attached' && store.id === request.storeId)
  return stores
    .filter((store) => store.kind !== 'attached' && isWithin(request.path, store.path, request.workspacePath))
    .sort((a, b) => b.path.length - a.path.length)[0]
    ?? stores.find((store) => store.kind === 'workspace')
}

function LoadState<T>({ loaded, unsupported, children }: { loaded: Loaded<T>; unsupported: string; children: (value: T) => JSX.Element }): JSX.Element {
  if (loaded.status === 'unsupported') return <Notice>{unsupported}</Notice>
  if (loaded.status === 'error') return <Notice tone="error">{loaded.error}</Notice>
  if (loaded.value == null) return <div className={styles.stack}><Skeleton height={58} /><Skeleton height={58} /><Skeleton height={58} /></div>
  return children(loaded.value)
}

export function MemoryView({ host }: DesktopPluginViewProps): JSX.Element {
  const t = useT()
  const label = useStoreLabel()
  const fs = useMemoryFs(host)
  const stores = useStores(host)
  const policy = usePolicyState(host)
  const dreams = useDreamRuns(host)
  const [tab, setTab] = useState<Tab>('entries')
  const [page, setPage] = useState<Page | null>(null)
  const [filters, setFilters] = useState<EntryFilters>({ query: '', store: ALL, type: ALL, age: 'all' })
  const [runFilters, setRunFilters] = useState<RunFilters>({ query: '', store: ALL, status: 'all' })
  const pending = usePendingOpenRequest()

  const storeList = useMemo(() => stores.value?.map((contents) => contents.store) ?? [], [stores.value])
  const runTitle = useRunTitle(storeList)
  const types = policy.value?.effective.types ?? []
  const declaredTypes = useMemo(() => new Set(types.map((type) => type.name)), [types])
  const dreamsAvailable = dreams.status !== 'unsupported'
  useEffect(() => {
    if (!pending || !stores.value) return
    const request = takeOpenRequest()
    if (!request) return
    setTab('entries')
    setPage(null)
    if (request.kind === 'new') {
      const store = storeList.find((candidate) => candidate.kind === 'workspace' && candidate.writable) ?? storeList.find((candidate) => candidate.writable)
      if (store) setPage({ kind: 'entry', target: { mode: 'new', storeKey: store.key, body: request.body } })
      return
    }
    const store = resolveRequestStore(storeList, request)
    if (store) setPage({ kind: 'entry', target: { mode: 'existing', storeKey: store.key, fileName: request.fileName } })
  }, [pending, stores.value, storeList])

  const refresh = (): void => {
    stores.reload()
    policy.reload()
    dreams.reload()
  }
  const closeEditor = (changed: boolean): void => {
    setPage(null)
    if (changed) stores.reload()
  }

  const writableStore = storeList.find((store) => store.writable)
  const tabLabel = (value: Tab): string => value === 'dreams' ? t('tab.dreams') : t('view.title')
  const editingIndex = page?.kind === 'index' ? stores.value?.find((contents) => contents.store.key === page.storeKey) : undefined
  const reviewedRun = page?.kind === 'run' ? dreams.value?.find((run) => run.id === page.runId) : undefined

  const pageTitle = (current: Page): string => {
    switch (current.kind) {
      case 'policy': return t('tab.policy')
      case 'policySetting': return t(`policy.setting.${current.setting}` as const)
      case 'entry': return current.target.mode === 'new' ? t('editor.newTitle') : memoryName(current.target.fileName)
      case 'index': return INDEX_FILE
      case 'run': return reviewedRun ? runTitle(reviewedRun) : t('tab.dreams')
    }
  }

  const actions = (
    <>
      <CatalogToolbarButton icon={<RefreshCw size={15} />} label={t('view.refresh')} onClick={refresh} />
      <CatalogToolbarButton icon={<Settings size={15} />} label={t('tab.policy')} onClick={() => setPage({ kind: 'policy' })} />
      {writableStore ? (
        <Button
          variant="primary"
          size="toolbar"
          iconLeft={<Plus size={15} aria-hidden />}
          onClick={() => {
            setTab('entries')
            setPage({ kind: 'entry', target: { mode: 'new', storeKey: writableStore.key, body: '' } })
          }}
        >
          {t('view.new')}
        </Button>
      ) : null}
    </>
  )

  const storeFilter = (value: string, onValueChange: (store: string) => void) => ({
    label: t('filter.store'),
    icon: <Database size={14} />,
    value,
    options: [{ value: ALL, label: t('filter.allStores') }, ...storeList.map((store) => ({ value: store.key, label: label(store) }))],
    onValueChange
  })

  const chrome: Pick<CatalogPageProps, 'title' | 'tabs' | 'breadcrumb' | 'actions' | 'search' | 'filters'> = page
    ? {
        title: page.kind === 'index' && editingIndex ? t('index.editorTitle', { file: INDEX_FILE, store: label(editingIndex.store) }) : pageTitle(page),
        breadcrumb: {
          parentLabel: tabLabel(page.kind === 'run' ? 'dreams' : page.kind === 'policy' || page.kind === 'policySetting' ? tab : 'entries'),
          currentLabel: pageTitle(page),
          onBack: () => setPage(null),
          trail: page.kind === 'policySetting' ? [{ label: t('tab.policy'), onClick: () => setPage({ kind: 'policy' }) }] : undefined
        }
      }
    : {
        title: t('view.heroTitle'),
        tabs: dreamsAvailable ? {
          value: tab,
          items: [
            { value: 'entries', label: tabLabel('entries') },
            { value: 'dreams', label: tabLabel('dreams') }
          ],
          onValueChange: (value: string) => setTab(value as Tab)
        } : undefined,
        actions,
        ...(tab === 'entries' ? {
          search: stores.status === 'ready' ? {
            value: filters.query,
            placeholder: t('entries.search'),
            onValueChange: (query: string) => setFilters((previous) => ({ ...previous, query }))
          } : undefined,
          filters: stores.status === 'ready' ? {
            ariaLabel: t('filter.label'),
            groups: [
              storeFilter(filters.store, (store) => setFilters((previous) => ({ ...previous, store }))),
              {
                label: t('filter.type'),
                icon: <Tag size={14} />,
                value: filters.type,
                options: [
                  { value: ALL, label: t('filter.allTypes') },
                  ...types.map((type) => ({ value: type.name, label: type.name })),
                  { value: UNTYPED, label: t('entries.untyped') }
                ],
                onValueChange: (type: string) => setFilters((previous) => ({ ...previous, type }))
              },
              {
                label: t('filter.age'),
                icon: <Clock size={14} />,
                value: filters.age,
                options: [
                  { value: 'all', label: t('filter.anyAge') },
                  { value: 'recent', label: t('filter.recent') },
                  { value: 'stale', label: t('filter.stale') }
                ],
                onValueChange: (age: string) => setFilters((previous) => ({ ...previous, age: age as AgeFilter }))
              }
            ]
          } : undefined
        } : {
          search: dreams.status === 'ready' ? {
            value: runFilters.query,
            placeholder: t('dreams.search'),
            onValueChange: (query: string) => setRunFilters((previous) => ({ ...previous, query }))
          } : undefined,
          filters: dreams.status === 'ready' ? {
            ariaLabel: t('dreams.filter.label'),
            groups: [
              storeFilter(runFilters.store, (store) => setRunFilters((previous) => ({ ...previous, store }))),
              {
                label: t('dreams.filter.status'),
                icon: <CircleDot size={14} />,
                value: runFilters.status,
                options: [
                  { value: 'all', label: t('dreams.filter.anyStatus') },
                  { value: 'pending', label: t('dreams.status.pending') },
                  { value: 'applied', label: t('dreams.status.applied') },
                  { value: 'discarded', label: t('dreams.status.discarded') },
                  { value: 'failed', label: t('dreams.status.failed') }
                ],
                onValueChange: (status: string) => setRunFilters((previous) => ({ ...previous, status: status as RunStatusFilter }))
              }
            ]
          } : undefined
        })
      }

  const body = (): JSX.Element => {
    if (page?.kind === 'policy') {
      return (
        <LoadState loaded={policy} unsupported={t('unsupported.policy')}>
          {(state) => (
            <PolicyTab
              state={state}
              onEdit={(setting) => setPage({ kind: 'policySetting', setting })}
              onChanged={policy.reload}
            />
          )}
        </LoadState>
      )
    }
    if (page?.kind === 'policySetting') {
      return (
        <LoadState loaded={policy} unsupported={t('unsupported.policy')}>
          {(state) => (
            <PolicySettingEditor
              key={page.setting}
              state={state}
              setting={page.setting}
              onClose={() => setPage({ kind: 'policy' })}
              onChanged={policy.reload}
            />
          )}
        </LoadState>
      )
    }
    if (page?.kind === 'run') return <RunReview runId={page.runId} onChanged={refresh} />
    if (tab === 'dreams' && page == null) {
      return (
        <LoadState loaded={dreams} unsupported={t('unsupported.dreams')}>
          {(runs) => (
            <ConsolidationTab
              runs={runs}
              stores={storeList}
              filters={runFilters}
              onOpen={(runId) => setPage({ kind: 'run', runId })}
            />
          )}
        </LoadState>
      )
    }
    return (
      <LoadState loaded={stores} unsupported={t('unsupported.entries')}>
        {(contents) => {
          if (editingIndex) return <IndexEditor fs={fs} contents={editingIndex} onClose={closeEditor} />
          return (
            <EntriesTab
              fs={fs}
              stores={contents}
              filters={filters}
              declaredTypes={declaredTypes}
              onOpen={(store, entry) => setPage({ kind: 'entry', target: { mode: 'existing', storeKey: store.key, fileName: entry.fileName } })}
              onNew={(store) => setPage({ kind: 'entry', target: { mode: 'new', storeKey: store.key, body: '' } })}
              onEditIndex={(store) => setPage({ kind: 'index', storeKey: store.store.key })}
              onChanged={stores.reload}
            />
          )
        }}
      </LoadState>
    )
  }

  // An entry renders its own CatalogPage so its name and description can be the editable title and subtitle.
  if (page?.kind === 'entry' && stores.value && chrome.breadcrumb) {
    return (
      <EntryEditor
        key={JSON.stringify(page.target)}
        fs={fs}
        stores={storeList}
        target={page.target}
        types={types}
        breadcrumb={chrome.breadcrumb}
        onClose={closeEditor}
      />
    )
  }
  return <CatalogPage {...chrome}>{body()}</CatalogPage>
}
