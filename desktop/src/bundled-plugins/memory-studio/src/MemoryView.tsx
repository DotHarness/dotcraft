import { Button, CatalogPage, CatalogToolbarButton, Skeleton, type CatalogPageProps, type DesktopPluginViewProps } from '@dotcraft/plugin'
import { CircleDot, Clock, Database, Plus, RefreshCw, Settings, Tag } from 'lucide-react'
import { useEffect, useMemo, useState, type JSX } from 'react'
import { ConsolidationTab, RunReview, useRunTitle, type RunFilters } from './consolidation/ConsolidationTab'
import type { RunStatusFilter } from './consolidation/dreamRuns'
import { errorMessage } from './data/rpc'
import { clearStore, type MemoryStore } from './data/stores'
import { ALL, EntriesTab, UNTYPED, useStoreLabel, type AgeFilter, type EntryFilters } from './entries/EntriesTab'
import { useDreamRuns, useMemoryFs, usePolicyState, useStores, useWorkspaceName, type Loaded } from './hooks/useMemoryData'
import { useT } from './i18n'
import { Notice } from './shared/ui'
import { onMemoryChanged, openMemoryDialog } from './state/memoryDialog'
import styles from './MemoryView.module.css'

type Tab = 'entries' | 'dreams'

function LoadState<T>({ loaded, unsupported, cards = false, children }: { loaded: Loaded<T>; unsupported: string; cards?: boolean; children: (value: T) => JSX.Element }): JSX.Element {
  if (loaded.status === 'unsupported') return <Notice>{unsupported}</Notice>
  if (loaded.status === 'error') return <Notice tone="error">{loaded.error}</Notice>
  if (loaded.value == null) {
    return cards
      ? <div className={styles.cardSkeletons}>{[0, 1, 2, 3].map((index) => <Skeleton key={index} height={112} radius={12} />)}</div>
      : <div className={styles.stack}><Skeleton height={58} /><Skeleton height={58} /><Skeleton height={58} /></div>
  }
  return children(loaded.value)
}

export function MemoryView({ host }: DesktopPluginViewProps): JSX.Element {
  const t = useT()
  const label = useStoreLabel(useWorkspaceName(host))
  const fs = useMemoryFs(host)
  const stores = useStores(host)
  const policy = usePolicyState(host)
  const dreams = useDreamRuns(host)
  const [tab, setTab] = useState<Tab>('entries')
  const [reviewRunId, setReviewRunId] = useState<string | null>(null)
  const [filters, setFilters] = useState<EntryFilters>({ query: '', store: ALL, type: ALL, age: 'all' })
  const [runFilters, setRunFilters] = useState<RunFilters>({ query: '', store: ALL, status: 'all' })

  const storeList = useMemo(() => stores.value?.map((contents) => contents.store) ?? [], [stores.value])
  const runTitle = useRunTitle(storeList)
  const types = policy.value?.effective.types ?? []
  const declaredTypes = useMemo(() => new Set(types.map((type) => type.name)), [types])
  const dreamsAvailable = dreams.status !== 'unsupported'
  useEffect(() => onMemoryChanged(stores.reload), [stores.reload])

  const refresh = (): void => {
    stores.reload()
    policy.reload()
    dreams.reload()
  }

  const writableStore =
    (filters.store !== ALL ? storeList.find((store) => store.key === filters.store && store.writable) : undefined)
    ?? storeList.find((store) => store.kind === 'workspace' && store.writable)
    ?? storeList.find((store) => store.writable)
  const [clearing, setClearing] = useState(false)
  const clearMemory = async (store: MemoryStore): Promise<void> => {
    const personal = store.kind === 'personal'
    const confirmed = await host.ui.confirm({
      title: t(personal ? 'clear.personalTitle' : 'clear.workspaceTitle'),
      message: t(personal ? 'clear.personalMessage' : 'clear.workspaceMessage'),
      confirmLabel: t(personal ? 'clear.personal' : 'clear.workspace'),
      danger: true
    })
    if (!confirmed) return
    setClearing(true)
    try {
      await clearStore(fs, store)
    } catch (error) {
      host.ui.showToast({ tone: 'error', message: t('clear.failed'), description: errorMessage(error) })
    } finally {
      setClearing(false)
      stores.reload()
    }
  }
  const tabLabel = (value: Tab): string => value === 'dreams' ? t('tab.dreams') : t('view.title')
  const reviewedRun = reviewRunId ? dreams.value?.find((run) => run.id === reviewRunId) : undefined
  const runPageTitle = reviewedRun ? runTitle(reviewedRun) : t('tab.dreams')

  const actions = (
    <>
      <CatalogToolbarButton icon={<RefreshCw size={15} />} label={t('view.refresh')} onClick={refresh} />
      <CatalogToolbarButton
        icon={<Settings size={15} />}
        label={t('view.settings')}
        onClick={() => host.navigation.openSettingsSection('personalization')}
      />
      {writableStore ? (
        <Button
          variant="primary"
          size="toolbar"
          iconLeft={<Plus size={15} aria-hidden />}
          onClick={() => openMemoryDialog({ mode: 'new', storeKey: writableStore.key })}
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

  const chrome: Pick<CatalogPageProps, 'title' | 'tabs' | 'breadcrumb' | 'actions' | 'search' | 'filters'> = reviewRunId
    ? {
        title: runPageTitle,
        breadcrumb: {
          parentLabel: tabLabel('dreams'),
          currentLabel: runPageTitle,
          onBack: () => setReviewRunId(null)
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
    if (reviewRunId) return <RunReview runId={reviewRunId} onChanged={refresh} />
    if (tab === 'dreams') {
      return (
        <LoadState loaded={dreams} unsupported={t('unsupported.dreams')}>
          {(runs) => (
            <ConsolidationTab
              runs={runs}
              stores={storeList}
              filters={runFilters}
              onOpen={(runId) => setReviewRunId(runId)}
            />
          )}
        </LoadState>
      )
    }
    return (
      <LoadState loaded={stores} unsupported={t('unsupported.entries')} cards>
        {(contents) => (
          <EntriesTab
            stores={contents}
            filters={filters}
            declaredTypes={declaredTypes}
            onOpen={(store, entry) => openMemoryDialog({ mode: 'existing', storeKey: store.key, fileName: entry.fileName })}
            clearing={clearing}
            onClear={(store) => void clearMemory(store)}
          />
        )}
      </LoadState>
    )
  }

  return <CatalogPage {...chrome}>{body()}</CatalogPage>
}
